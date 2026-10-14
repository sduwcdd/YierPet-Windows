using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace YierPet.Windows.Core;

/// <summary>Owns only the short-lived inference process started for this workshop.</summary>
public sealed class VoiceRuntime : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _sync = new();
    private Process? _process;
    private string _tail = "";
    private bool _disposed;

    public static void ValidateSettings(VoiceSettings settings)
    {
        VoiceService.Endpoint(settings.Endpoint);
        foreach (var (path, label) in new[] {
            (settings.RuntimePythonPath, "语音环境的 Python"),
            (Path.Combine(settings.RuntimeRepositoryPath, "api_v2.py"), "GPT-SoVITS 目录中的 api_v2.py"),
            (settings.RuntimeConfigPath, "推理配置 YAML"),
            (Path.Combine(AppContext.BaseDirectory, "Tools", "voice_runtime.py"), "应用的按需启动脚本") })
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidDataException($"找不到{label}，请在高级设置中补齐运行环境。\n{path}");
    }

    public async Task<byte[]> GenerateAsync(VoiceService service, VoiceSettings settings, VoiceProfile profile,
        string text, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        if (!settings.LoadOnDemand)
            return await service.SynthesizeAsync(settings.Endpoint, profile, text, token);
        ValidateSettings(settings);
        VoiceStore.ValidateFiles(profile);
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > 1000)
            throw new InvalidDataException("请输入 1–1000 字的台词。");
        var endpoint = VoiceService.Endpoint(settings.Endpoint);
        if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p => p.Port == endpoint.Port))
            throw new IOException("语音端口已被占用。请关闭之前手动启动的语音服务，或更换服务地址中的端口；应用不会关闭其他程序。");
        string folder = Path.Combine(Path.GetTempPath(), "YierPet-voice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string config = Path.Combine(folder, "inference.yaml");
            File.Copy(settings.RuntimeConfigPath, config);
            string identity = Guid.NewGuid().ToString("N");
            var start = new ProcessStartInfo(settings.RuntimePythonPath)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = settings.RuntimeRepositoryPath
            };
            foreach (string arg in new[] { "-u", Path.Combine(AppContext.BaseDirectory, "Tools", "voice_runtime.py"),
                "--repo", settings.RuntimeRepositoryPath, "--config", config,
                "--host", endpoint.Host.Trim('[', ']'), "--port", endpoint.Port.ToString(),
                "--parent-pid", Environment.ProcessId.ToString(), "--token", identity }) start.ArgumentList.Add(arg);
            start.Environment["PYTHONIOENCODING"] = "utf-8";
            if (File.Exists(settings.FfmpegPath))
                start.Environment["PATH"] = Path.GetDirectoryName(settings.FfmpegPath) + Path.PathSeparator + start.Environment["PATH"];
            lock (_sync)
            {
                token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_process is not null) throw new InvalidOperationException("已有生成任务正在运行。");
                _tail = "";
                _process = new Process { StartInfo = start };
                _process.OutputDataReceived += Capture;
                _process.ErrorDataReceived += Capture;
                _process.Start();
                _process.BeginOutputReadLine(); _process.BeginErrorReadLine();
            }
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
                { Timeout = TimeSpan.FromSeconds(2) };
            var timer = Stopwatch.StartNew();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                lock (_sync)
                    if (_process is null || _process.HasExited)
                        throw new IOException("语音环境启动失败：\n" + _tail);
                try
                {
                    using var response = await http.GetAsync(new Uri(endpoint, "yierpet-runtime"), token);
                    if (response.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                        if (doc.RootElement.TryGetProperty("token", out var value) && value.GetString() == identity) break;
                        throw new IOException("端口上出现了其他服务，已停止本次启动。请更换端口。");
                    }
                }
                catch (HttpRequestException) { }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
                if (timer.Elapsed > TimeSpan.FromMinutes(3)) throw new TimeoutException("语音环境启动超过三分钟，请检查配置或可用内存。\n" + _tail);
                await Task.Delay(400, token);
            }
            VoiceService.ResetAfterOwnedShutdown();
            return await service.SynthesizeAsync(settings.Endpoint, profile, text, token);
        }
        finally
        {
            StopOwnedProcess();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private void Capture(object sender, DataReceivedEventArgs args)
    {
        if (args.Data is null) return;
        lock (_sync) { _tail += args.Data + "\n"; if (_tail.Length > 4000) _tail = _tail[^4000..]; }
    }

    private void StopOwnedProcess()
    {
        Process? process;
        lock (_sync) { process = _process; _process = null; }
        if (process is null) return;
        using (process)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(10000)) throw new IOException("语音进程未能及时退出，请退出桌宠后检查。");
                VoiceService.ResetAfterOwnedShutdown();
            }
            catch (InvalidOperationException) { /* Process.Start failed or already exited. */ }
        }
    }

    public void Dispose()
    {
        lock (_sync) { if (_disposed) return; _disposed = true; }
        _lifetime.Cancel();
        StopOwnedProcess();
        // An in-flight request owns a linked token; let it finish before collecting this source.
    }
}
