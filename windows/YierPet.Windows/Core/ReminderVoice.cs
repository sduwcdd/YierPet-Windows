using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;

namespace YierPet.Windows.Core;

public sealed class ReminderVoice : IDisposable
{
    private readonly MediaPlayer _player = new();
    private readonly VoiceStore _store;
    private readonly string? _contentRoot;
    public string? LastError { get; private set; }
    public ReminderVoice(VoiceStore? store = null, string? contentRoot = null)
    {
        _store = store ?? new(); _contentRoot = contentRoot;
        _player.MediaEnded += (_, _) => _player.Close();
        _player.MediaFailed += (_, e) => { LastError = e.ErrorException.Message; _player.Close(); };
    }
    // Exact displayed text is the cache key, so unrelated or outdated speech is never substituted.
    public static string FileName(PetCharacter role, string text, bool clarity) =>
        $"{role.ToString().ToLowerInvariant()}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()[..16]}-{(clarity ? "clarity" : "original")}.wav";

    public static string? Resolve(PetCharacter role, string text, bool clarity, string? contentRoot = null)
    {
        string path = Path.Combine(contentRoot ?? Path.Combine(AppContext.BaseDirectory, "Content", "voice", "reminders"), FileName(role, text, clarity));
        return File.Exists(path) ? path : null;
    }

    public void Play(PetCharacter role, string text)
    {
        Stop();
        try
        {
            var settings = _store.Load();
            if (!settings.ReminderSpeechEnabled) { Stop(); return; }
            string? path = Resolve(role, text, settings.ClarityEnabled, _contentRoot);
            if (path is null) { LastError = "这句提醒的语音文件缺失，已保留文字提醒。"; return; }
            LastError = null;
            _player.Open(new Uri(path)); _player.Volume = 0.8; _player.Play();
        }
        catch (Exception ex) { LastError = ex.Message; Stop(); }
    }
    public void Stop() => _player.Close();
    public void Dispose() => _player.Close();
}
