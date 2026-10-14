using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace YierPet.Windows.Core;

public static class VoiceAudioProcessor
{
    // Same EQ as the approved listening trial. No pitch shift, denoising, or model modification.
    public const string Equalizer = "highpass=f=65,equalizer=f=350:t=q:w=0.7:g=-2,equalizer=f=2600:t=q:w=0.8:g=2.5,treble=g=1.5:f=4500";

    public static async Task<byte[]> ClarifyAsync(byte[] wav, string ffmpeg)
    {
        if (!Path.IsPathFullyQualified(ffmpeg) || !File.Exists(ffmpeg))
            throw new InvalidDataException("轻度清晰化需要 FFmpeg。请在高级设置选择 ffmpeg.exe，或取消轻度清晰化。");
        VoiceService.ValidateWave(wav);
        string folder = Path.Combine(Path.GetTempPath(), "YierPet-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string source = Path.Combine(folder, "source.wav"), mono = Path.Combine(folder, "mono.wav"), output = Path.Combine(folder, "clarity.wav");
            await File.WriteAllBytesAsync(source, wav);
            await Run(ffmpeg, "-y", "-i", source, "-ar", "32000", "-ac", "1", "-c:a", "pcm_f32le", mono);
            string report = await Run(ffmpeg, "-i", mono, "-af", Equalizer + ",loudnorm=I=-18:TP=-1.5:LRA=11:print_format=json", "-f", "null", "-");
            int start = report.LastIndexOf('{'), end = report.LastIndexOf('}');
            if (start < 0 || end < start) throw new InvalidDataException("无法测量语音音量。");
            using var doc = JsonDocument.Parse(report[start..(end + 1)]);
            double loudness = double.Parse(doc.RootElement.GetProperty("input_i").GetString()!, CultureInfo.InvariantCulture);
            double peak = double.Parse(doc.RootElement.GetProperty("input_tp").GetString()!, CultureInfo.InvariantCulture);
            if (!double.IsFinite(loudness) || !double.IsFinite(peak)) throw new InvalidDataException("音频没有可测量的人声，请检查生成结果。");
            // Peak headroom takes precedence for very dynamic speech; no clipping or extra compression.
            double gain = Math.Min(-18 - loudness, -1.5 - peak);
            await Run(ffmpeg, "-y", "-i", mono, "-af", Equalizer + ",volume=" + gain.ToString("R", CultureInfo.InvariantCulture) + "dB:precision=double",
                "-ar", "32000", "-ac", "1", "-c:a", "pcm_s16le", output);
            var result = await File.ReadAllBytesAsync(output);
            VoiceService.ValidateWave(result);
            return result;
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    private static async Task<string> Run(string exe, params string[] args)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("-hide_banner"); info.ArgumentList.Add("-nostdin");
        foreach (string arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("无法启动 FFmpeg。");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("清晰化处理超时，原生成音已保留。");
        }
        string detail = await error; await output;
        if (process.ExitCode != 0) throw new IOException("清晰化处理失败：" + detail[^Math.Min(600, detail.Length)..]);
        return detail;
    }
}
