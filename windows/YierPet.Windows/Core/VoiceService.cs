using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace YierPet.Windows.Core;

public sealed class VoiceService : IDisposable
{
    // api_v2 has one mutable model pair. Keep both weight switches and synthesis in one transaction.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _serviceStateUncertain;
    private readonly HttpClient _http;
    public VoiceService(HttpClient? http = null) => _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMinutes(3), MaxResponseContentBufferSize = 64 * 1024 * 1024
    };

    public static Uri Endpoint(string value)
    {
        if (!Uri.TryCreate(value.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || uri.Scheme != "http" || !uri.IsLoopback || uri.AbsolutePath != "/"
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("请填写本机服务地址，例如 http://127.0.0.1:9880。");
        return uri;
    }

    public async Task CheckAsync(string endpoint)
    {
        using var response = await _http.GetAsync(new Uri(Endpoint(endpoint), "openapi.json"));
        await EnsureSuccess(response);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("paths", out var paths)
            || !paths.TryGetProperty("/tts", out var tts) || !tts.TryGetProperty("post", out _)
            || !paths.TryGetProperty("/set_gpt_weights", out _) || !paths.TryGetProperty("/set_sovits_weights", out _))
            throw new InvalidDataException("服务不是兼容的 GPT-SoVITS api_v2，请按准备说明启动。");
    }

    internal static void ResetAfterOwnedShutdown() => _serviceStateUncertain = false;

    public async Task<byte[]> SynthesizeAsync(string endpoint, VoiceProfile profile, string text, CancellationToken cancellationToken = default)
    {
        var uri = Endpoint(endpoint);
        VoiceStore.ValidateFiles(profile);
        text = text.Trim();
        if (text.Length == 0 || text.Length > 1000) throw new InvalidDataException("请输入 1–1000 字的台词。");
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (_serviceStateUncertain) throw new InvalidOperationException("上次请求超时，服务可能仍在处理。请重启 GPT-SoVITS 服务和桌宠后再生成。");
            foreach (var (route, path) in new[] { ("set_gpt_weights", profile.GptPath), ("set_sovits_weights", profile.SovitsPath) })
            {
                using var change = await _http.GetAsync(new Uri(uri, route + "?weights_path=" + Uri.EscapeDataString(path)), cancellationToken);
                await EnsureSuccess(change);
            }
            using var response = await _http.PostAsJsonAsync(new Uri(uri, "tts"), new
            {
                text, text_lang = "zh", ref_audio_path = profile.ReferencePath,
                prompt_text = profile.ReferenceText, prompt_lang = "zh", text_split_method = "cut5",
                batch_size = 1, media_type = "wav", streaming_mode = false, speed_factor = 1.0
            }, cancellationToken);
            await EnsureSuccess(response);
            var wav = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            ValidateWave(wav);
            return wav;
        }
        catch (OperationCanceledException ex)
        {
            _serviceStateUncertain = true;
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException("语音请求超时。请重启 GPT-SoVITS 服务和桌宠，再尝试更短的文本。", ex);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            _serviceStateUncertain = true;
            throw new HttpRequestException("连接中断或服务未启动。请启动（或重启）GPT-SoVITS 服务并重启桌宠，再生成语音。", ex);
        }
        finally { Gate.Release(); }
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        string detail = await response.Content.ReadAsStringAsync();
        if (detail.Length > 700) detail = detail[..700];
        throw new HttpRequestException($"语音服务返回 {(int)response.StatusCode}：{detail}", null, response.StatusCode);
    }

    public static void ValidateWave(byte[] bytes)
    {
        if (bytes.Length < 44 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF"
            || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE"
            || (long)BitConverter.ToUInt32(bytes, 4) + 8 != bytes.Length)
            throw new InvalidDataException("服务没有返回完整 WAV 音频，请检查模型和参考录音。");
        bool format = false, data = false;
        long offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            int index = (int)offset;
            string name = Encoding.ASCII.GetString(bytes, index, 4);
            uint size = BitConverter.ToUInt32(bytes, index + 4);
            if (offset + 8 + size > bytes.Length) throw new InvalidDataException("语音文件不完整。");
            if (name == "fmt " && size >= 16) format = BitConverter.ToUInt16(bytes, index + 10) > 0;
            if (name == "data" && size > 0) data = true;
            offset += 8L + size + size % 2;
        }
        if (!format || !data) throw new InvalidDataException("语音文件缺少格式或声音数据。");
    }

    public void Dispose() => _http.Dispose();
}
