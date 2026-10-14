using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace YierPet.Windows.Core;

public sealed class VoiceProfile
{
    public string GptPath { get; set; } = "";
    public string SovitsPath { get; set; } = "";
    public string ReferencePath { get; set; } = "";
    public string ReferenceText { get; set; } = "";
    public string ModelVersion { get; set; } = "v2";
    public string Text { get; set; } = "你好呀，今天也要开开心心！";
}

public sealed class VoiceSettings
{
    public string Endpoint { get; set; } = "http://127.0.0.1:9880";
    public bool ClarityEnabled { get; set; } = true;
    public bool ReminderSpeechEnabled { get; set; } = true;
    public string FfmpegPath { get; set; } = "";
    public bool LoadOnDemand { get; set; } = true;
    public string RuntimePythonPath { get; set; } = "";
    public string RuntimeRepositoryPath { get; set; } = "";
    public string RuntimeConfigPath { get; set; } = "";
    public VoiceProfile Yier { get; set; } = new();
    public VoiceProfile Bubu { get; set; } = new();
}

public sealed class VoiceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string Root { get; }
    public string AudioDirectory => Path.Combine(Root, "audio");
    public string SettingsPath => Path.Combine(Root, "voices.json");
    public string DefaultsPath => Path.Combine(Root, "defaults.json");

    public VoiceStore(string? root = null) => Root = root ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YierPet-Windows", "Voice");

    public VoiceSettings Load()
    {
        var value = File.Exists(SettingsPath) ? JsonSerializer.Deserialize<VoiceSettings>(File.ReadAllText(SettingsPath), JsonOptions)
            : new VoiceSettings();
        value = value
            ?? throw new InvalidDataException("语音配置为空。请先备份并检查 voices.json。");
        if (value.Yier is null || value.Bubu is null || string.IsNullOrWhiteSpace(value.Endpoint))
            throw new InvalidDataException("语音配置不完整。请先备份并检查 voices.json。");
        VoiceSettings? defaults = null;
        try { defaults = LoadDefaults(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { /* A moved optional preset must not break saved user settings. */ }
        if (defaults is not null)
        {
            // Only populate entirely empty profiles: never mix a user's models with a different reference.
            if (IsEmpty(value.Yier)) value.Yier = defaults.Yier;
            if (IsEmpty(value.Bubu)) value.Bubu = defaults.Bubu;
            if (string.IsNullOrWhiteSpace(value.FfmpegPath)) value.FfmpegPath = defaults.FfmpegPath;
            if (string.IsNullOrWhiteSpace(value.RuntimePythonPath) && string.IsNullOrWhiteSpace(value.RuntimeRepositoryPath)
                && string.IsNullOrWhiteSpace(value.RuntimeConfigPath))
            {
                value.RuntimePythonPath = defaults.RuntimePythonPath;
                value.RuntimeRepositoryPath = defaults.RuntimeRepositoryPath;
                value.RuntimeConfigPath = defaults.RuntimeConfigPath;
            }
        }
        return value;
    }

    private static bool IsEmpty(VoiceProfile p) => string.IsNullOrWhiteSpace(p.GptPath)
        && string.IsNullOrWhiteSpace(p.SovitsPath) && string.IsNullOrWhiteSpace(p.ReferencePath)
        && string.IsNullOrWhiteSpace(p.ReferenceText);

    public VoiceSettings? LoadDefaults()
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, "Content", "voice", "defaults.json");
        string? path = File.Exists(DefaultsPath) ? DefaultsPath : File.Exists(bundled) ? bundled : null;
        return path is null ? null : ImportPreset(path);
    }

    public static VoiceSettings ImportPreset(string path)
    {
        var settings = JsonSerializer.Deserialize<VoiceSettings>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("声音预设为空。");
        if (settings.Yier is null || settings.Bubu is null) throw new InvalidDataException("预设缺少角色配置。");
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string Resolve(string p) => string.IsNullOrWhiteSpace(p) ? "" : Path.GetFullPath(p, folder);
        foreach (var p in new[] { settings.Yier, settings.Bubu })
        {
            p.GptPath = Resolve(p.GptPath); p.SovitsPath = Resolve(p.SovitsPath);
            p.ReferencePath = Resolve(p.ReferencePath); ValidateFiles(p);
        }
        settings.FfmpegPath = Resolve(settings.FfmpegPath);
        settings.RuntimePythonPath = Resolve(settings.RuntimePythonPath);
        settings.RuntimeRepositoryPath = Resolve(settings.RuntimeRepositoryPath);
        settings.RuntimeConfigPath = Resolve(settings.RuntimeConfigPath);
        VoiceService.Endpoint(settings.Endpoint);
        return settings;
    }

    public void Save(VoiceSettings settings)
    {
        Directory.CreateDirectory(Root);
        WriteAtomic(SettingsPath, JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions));
    }

    public static void ValidateFiles(VoiceProfile profile)
    {
        foreach (var (name, path) in new[] { ("GPT 模型", profile.GptPath), ("SoVITS 模型", profile.SovitsPath), ("参考录音", profile.ReferencePath) })
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
                throw new InvalidDataException($"请选择有效的{name}本机文件。");
        if (string.IsNullOrWhiteSpace(profile.ReferenceText))
            throw new InvalidDataException("请填写与参考录音逐字一致的文字。");
    }

    // Explicit archive: preserve original files and only update the profile after every copy succeeds.
    public VoiceProfile Archive(PetCharacter character, VoiceProfile profile)
    {
        ValidateFiles(profile);
        var folder = Path.Combine(Root, "models", character == PetCharacter.Yier ? "yier" : "bubu", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var written = new List<string>();
        try
        {
            string Copy(string source, string name)
            {
                string target = Path.Combine(folder, name + Path.GetExtension(source));
                written.Add(target);
                File.Copy(source, target);
                return target;
            }
            var archived = new VoiceProfile
            {
                GptPath = Copy(profile.GptPath, "gpt"), SovitsPath = Copy(profile.SovitsPath, "sovits"),
                ReferencePath = Copy(profile.ReferencePath, "reference"), ReferenceText = profile.ReferenceText,
                Text = profile.Text, ModelVersion = profile.ModelVersion
            };
            var files = new[] { archived.GptPath, archived.SovitsPath, archived.ReferencePath }.Select(path =>
            {
                using var stream = File.OpenRead(path);
                return new { file = Path.GetFileName(path), sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() };
            }).ToArray();
            var manifest = Path.Combine(folder, "manifest.json");
            written.Add(manifest);
            File.WriteAllText(manifest, JsonSerializer.Serialize(new
            {
                character = character.ToString(), createdUtc = DateTimeOffset.UtcNow,
                modelVersion = profile.ModelVersion, referenceText = profile.ReferenceText, files,
                note = "角色权重与参考录音归档；运行仍需要匹配版本的 GPT-SoVITS 基础模型和环境。"
            }, JsonOptions));
            return archived;
        }
        catch
        {
            foreach (var file in written) if (File.Exists(file)) File.Delete(file);
            Directory.Delete(folder);
            throw;
        }
    }

    public string SaveAudio(PetCharacter character, byte[] wav)
    {
        Directory.CreateDirectory(AudioDirectory);
        string path = Path.Combine(AudioDirectory, $"{character.ToString().ToLowerInvariant()}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.wav");
        WriteAtomic(path, wav);
        return path;
    }

    public static void WriteAtomic(string path, byte[] bytes)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, path, overwrite: true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
