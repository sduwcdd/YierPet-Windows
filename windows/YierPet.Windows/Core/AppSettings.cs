using System.IO;
using System.Text.Json;

namespace YierPet.Windows.Core;

public sealed class AppSettings
{
    public bool RandomBehaviourEnabled { get; set; } = true;
    public bool AutomaticInteractionsEnabled { get; set; } = true;
    public bool BubuFollowsYier { get; set; }
    public bool YierVisible { get; set; } = true;
    public bool BubuVisible { get; set; } = true;
    public HashSet<string> DisabledStickerIds { get; set; } = [];
    public Dictionary<string, bool> ReminderEnabled { get; set; } = [];

    public bool IsEnabled(ReminderKind kind) =>
        !ReminderEnabled.TryGetValue(kind.ToString(), out var enabled) || enabled;

    public void SetEnabled(ReminderKind kind, bool enabled) =>
        ReminderEnabled[kind.ToString()] = enabled;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YierPet-Windows");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
            }
        }
        catch (JsonException)
        {
            // Keep the pet usable if a manually edited settings file is invalid.
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, Options));
        File.Move(temporaryPath, FilePath, overwrite: true);
    }
}
