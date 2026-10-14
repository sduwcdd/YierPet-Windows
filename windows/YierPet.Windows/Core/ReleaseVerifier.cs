using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace YierPet.Windows.Core;

// Invoked by release automation before publishing the portable executable.
internal static class ReleaseVerifier
{
    public static int Run(string reportPath)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "YierPet-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var settings = new AppSettings { RandomBehaviourEnabled = false, AutomaticInteractionsEnabled = false };
            var library = new StickerLibrary(settings, userRoot: temporary);
            void Check(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
            // Verify spoken reminders are complete and match the exact displayed scripts in a standalone build.
            string voiceRoot = Path.Combine(AppContext.BaseDirectory, "Content", "voice", "reminders");
            using (var voiceManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(voiceRoot, "manifest.json"))))
            {
                var clips = voiceManifest.RootElement.GetProperty("clips").EnumerateArray().ToArray();
                Check(clips.Length == ReminderService.Scripts.Count() * 2, "Reminder voice count mismatch.");
                foreach (var role in Enum.GetValues<PetCharacter>())
                    foreach (var (_, text) in ReminderService.Scripts)
                        foreach (bool clarity in new[] { false, true })
                        {
                            string name = ReminderVoice.FileName(role, text, clarity);
                            string key = clarity ? "clarity" : "original";
                            var entry = clips.Single(c => c.GetProperty(key).GetString() == name);
                            Check(entry.GetProperty("text").GetString() == text, "Reminder text mismatch.");
                            byte[] bytes = File.ReadAllBytes(Path.Combine(voiceRoot, name));
                            VoiceService.ValidateWave(bytes);
                            Check(Convert.ToHexString(SHA256.HashData(bytes)).Equals(entry.GetProperty(key + "Sha256").GetString(), StringComparison.OrdinalIgnoreCase), "Reminder audio hash mismatch.");
                        }
            }
            Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "Docs", "voice-guide.md")), "Voice guide missing.");
            Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "Docs", "voice-preset.example.json")), "Voice preset template missing.");
            Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "Tools", "voice_runtime.py")), "On-demand voice launcher missing.");
            Check(new VoiceSettings().LoadOnDemand, "Voice models must load on demand by default.");
            Check(library.Assets.Count >= 522, "Built-in base assets are missing.");
            Check(library.Rules.Interactions.Count >= 225, "Built-in base interactions are missing.");
            foreach (var character in Enum.GetValues<PetCharacter>())
                Check(library.Baseline(character).PixelWidth > 0, "Missing baseline.");
            foreach (var asset in library.Assets)
            {
                Check(library.Poster(asset).PixelWidth > 0, "Missing poster: " + asset.Id);
                Check(library.Frame(asset, 0).PixelWidth == asset.Width, "Invalid first frame: " + asset.Id);
                Check(library.Frame(asset, asset.FrameCount - 1).PixelHeight == asset.Height, "Invalid last frame: " + asset.Id);
                string exported = Path.Combine(temporary, library.ExportFileName(asset));
                library.ExportOriginal(asset, exported);
                using var file = File.OpenRead(exported);
                Check(Convert.ToHexString(SHA256.HashData(file)).Equals(asset.OriginalSha256, StringComparison.OrdinalIgnoreCase), "Export hash mismatch: " + asset.Id);
            }
            Check(library.MenuActions(PetCharacter.Yier).Count() == 5 && library.MenuActions(PetCharacter.Bubu).Count() == 5, "Invalid action menu.");
            Check(library.MenuInteractions(false).Count() == 6 && library.MenuInteractions(true).Count() == 6, "Invalid interaction menus.");
            foreach (var id in new[] { "wx-0150", "wx-0183" })
            {
                var asset = library.Find(id)!;
                for (int i = 0; i < asset.FrameCount; i++)
                    Check(library.Frame(asset, i, stabilizeLocomotion: true).PixelWidth == asset.Width, "Invalid walk frame.");
            }
            // Verify that editing never writes to the extracted, bundled content.
            var item = library.Find("wx-0079")!;
            var edit = library.EditFor(item); edit.Name = "Verification only"; library.SaveAsset(item, edit);
            var reloaded = new StickerLibrary(settings, userRoot: temporary);
            Check(reloaded.Find(item.Id)!.Name == edit.Name, "User metadata did not persist.");
            var untouched = new StickerLibrary(settings, userRoot: Path.Combine(temporary, "untouched"));
            Check(untouched.Find(item.Id)!.Name != edit.Name, "Built-in content was modified.");
            string archivePath = Path.Combine(temporary, "all-originals.zip");
            Check(library.ExportArchive(library.Assets, archivePath) == library.Assets.Count, "Batch export count mismatch.");
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                Check(archive.Entries.Count == library.Assets.Count + 2, "Batch export entries mismatch.");
                Check(archive.GetEntry("manifest.json") is not null, "Batch export description missing.");
                foreach (var asset in library.Assets)
                {
                    var entry = archive.GetEntry("originals/" + library.ExportFileName(asset));
                    Check(entry is not null, "Batch export file missing: " + asset.Id);
                    using var stream = entry!.Open();
                    Check(Convert.ToHexString(SHA256.HashData(stream)).Equals(asset.OriginalSha256, StringComparison.OrdinalIgnoreCase), "Batch export hash mismatch: " + asset.Id);
                }
            }
            var bad = library.Find("wx-0001")!;
            string expectedHash = bad.OriginalSha256, protectedFile = Path.Combine(temporary, "keep.gif");
            File.WriteAllBytes(protectedFile, [1, 2, 3]);
            try
            {
                bad.OriginalSha256 = new string('0', 64);
                bool rejected = false;
                try { library.ExportOriginal(bad, protectedFile); } catch (InvalidDataException) { rejected = true; }
                Check(rejected && File.ReadAllBytes(protectedFile).SequenceEqual(new byte[] { 1, 2, 3 }), "Failed export overwrote an existing file.");
            }
            finally { bad.OriginalSha256 = expectedHash; }
            // Imported and pre-export-version user data must also retain downloadable originals.
            string png = Path.Combine(temporary, "input.png");
            library.ExportOriginal(library.Find("gsp-0001")!, png);
            var upload = StickerImporter.Import(png, temporary); upload.Role = "bubu";
            library.SaveAsset(upload, new StickerEdit { Name = "Upload export check" }, isNew: true);
            library.ExportOriginal(upload, Path.Combine(temporary, "new-upload.png"));
            upload.Original = ""; upload.OriginalSha256 = "";
            library.ExportOriginal(upload, Path.Combine(temporary, "legacy-upload.png"));
            Check(File.ReadAllBytes(png).SequenceEqual(File.ReadAllBytes(Path.Combine(temporary, "legacy-upload.png"))), "Legacy upload export differs.");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new
            {
                success = true, version = typeof(ReleaseVerifier).Assembly.GetName().Version?.ToString(3), assets = untouched.Assets.Count,
                interactions = library.Rules.Interactions.Count, sampledFrames = untouched.Assets.Count * 2,
                exportedOriginals = untouched.Assets.Count, batchExport = true, userUploadExport = true,
                userDataIsolated = true,
                reminderVoiceClips = ReminderService.Scripts.Count() * 2,
                reminderVoiceFiles = ReminderService.Scripts.Count() * 4,
                voiceGuideIncluded = true,
                onDemandVoiceLauncherIncluded = true,
                voiceLoadsOnDemandByDefault = true,
                bundledFilesExtracted = !string.Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
                    Path.GetDirectoryName(Environment.ProcessPath), StringComparison.OrdinalIgnoreCase)
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { success = false, error = exception.ToString() }));
            return 1;
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
