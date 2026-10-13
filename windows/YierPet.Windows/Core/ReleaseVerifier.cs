using System.IO;
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
            Check(library.Assets.Count == 522, "Built-in asset count differs from v0.0.1.");
            Check(library.Rules.Interactions.Count == 225, "Interaction count differs from v0.0.1.");
            foreach (var character in Enum.GetValues<PetCharacter>())
                Check(library.Baseline(character).PixelWidth > 0, "Missing baseline.");
            foreach (var asset in library.Assets)
            {
                Check(library.Poster(asset).PixelWidth > 0, "Missing poster: " + asset.Id);
                Check(library.Frame(asset, 0).PixelWidth == asset.Width, "Invalid first frame: " + asset.Id);
                Check(library.Frame(asset, asset.FrameCount - 1).PixelHeight == asset.Height, "Invalid last frame: " + asset.Id);
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
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new
            {
                success = true, version = "0.0.1", assets = library.Assets.Count,
                interactions = library.Rules.Interactions.Count, sampledFrames = library.Assets.Count * 2,
                userDataIsolated = true,
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
