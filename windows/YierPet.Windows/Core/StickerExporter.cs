using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YierPet.Windows.Core;

public sealed partial class StickerLibrary
{
    private string OriginalPath(StickerAsset asset)
    {
        if (asset.Original.Length > 0)
        {
            string file = Resolve(asset.Original);
            if (File.Exists(file)) return file;
        }
        // Uploads saved by v0.0.1 already retain original.gif/png, but have no Original field.
        if (IsUserAsset(asset) && asset.Poster.StartsWith("user/", StringComparison.Ordinal))
        {
            string directory = Path.GetDirectoryName(Resolve(asset.Poster))!;
            foreach (var name in new[] { "original.gif", "original.png" })
                if (File.Exists(Path.Combine(directory, name))) return Path.Combine(directory, name);
        }
        throw new FileNotFoundException("这个表情的原文件缺失，无法下载。请重新导入原素材。");
    }

    public string ExportFileName(StickerAsset asset)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string name = new(asset.Name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        name = name.Trim().TrimEnd('.'); if (name.Length == 0) name = "表情";
        return $"{asset.Id}-{name}{Path.GetExtension(OriginalPath(asset)).ToLowerInvariant()}";
    }

    private static void CheckHash(StickerAsset asset, string hash)
    {
        if (asset.OriginalSha256.Length > 0 && !hash.Equals(asset.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{asset.Id} 原文件校验失败，下载已取消。");
    }

    public void ExportOriginal(StickerAsset asset, string destination)
    {
        string source = OriginalPath(asset);
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new IOException("请选择其他保存位置，避免覆盖表情库里的原文件。");
        string temporary = Path.GetFullPath(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var input = File.OpenRead(source))
            using (var output = File.Create(temporary))
            {
                string hash = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
                CheckHash(asset, hash); input.Position = 0; input.CopyTo(output);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public int ExportArchive(IEnumerable<StickerAsset> selected, string destination)
    {
        var assets = selected.DistinctBy(a => a.Id).ToArray();
        if (assets.Length == 0) throw new ArgumentException("当前筛选没有表情可导出。");
        var sources = assets.Select(a => (Asset: a, Path: OriginalPath(a), Name: ExportFileName(a))).ToArray();
        if (sources.Any(s => s.Path.Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)))
            throw new IOException("请选择其他保存位置，避免覆盖原文件。");
        string temporary = Path.GetFullPath(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var manifest = new List<object>();
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var (asset, path, name) in sources)
                {
                    using var input = File.OpenRead(path);
                    string hash = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(); CheckHash(asset, hash); input.Position = 0;
                    var entry = archive.CreateEntry("originals/" + name, CompressionLevel.NoCompression);
                    using (var output = entry.Open()) input.CopyTo(output);
                    manifest.Add(new { asset.Id, asset.Name, asset.Role, file = entry.FullName, sha256 = hash,
                        asset.SourceLabel, asset.SourceUrl, description = SemanticFor(asset.Id) });
                }
                var ids = assets.Select(a => a.Id).ToHashSet();
                using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open(), new UTF8Encoding(false)))
                    writer.Write(JsonSerializer.Serialize(new { schemaVersion = 1, assets = manifest,
                        interactions = Rules.Interactions.Where(r => r.AssetIds.All(ids.Contains)) }, new JsonSerializerOptions { WriteIndented = true }));
                using var readme = new StreamWriter(archive.CreateEntry("使用说明.txt").Open(), new UTF8Encoding(false));
                readme.Write("originals 文件夹包含原始 GIF / PNG / JPG，不是预览缩略图。\nmanifest.json 记录动作说明、来源、哈希及完整包含的互动关系。\n将 originals 中的 GIF 或 PNG 逐张上传即可再次使用；当前应用不支持直接导入整个 ZIP。\n");
            }
            File.Move(temporary, destination, overwrite: true);
            return assets.Length;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
