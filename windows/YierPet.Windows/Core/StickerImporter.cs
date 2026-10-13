using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YierPet.Windows.Core;

public static class StickerImporter
{
    public static StickerAsset Import(string file, string userRoot)
    {
        if (new FileInfo(file).Length > 50 * 1024 * 1024) throw new InvalidDataException("文件不能超过 50 MB。");
        using var stream = File.OpenRead(file);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        bool gif = decoder is GifBitmapDecoder;
        if (!gif && decoder is not PngBitmapDecoder) throw new InvalidDataException("请选择 GIF 或 PNG 文件。");
        int Query(BitmapMetadata? m, string key, int fallback = 0)
        {
            try { return m?.GetQuery(key) is { } value ? Convert.ToInt32(value) : fallback; }
            catch (Exception e) when (e is NotSupportedException or ArgumentException) { return fallback; }
        }
        int width = gif ? Query(decoder.Metadata as BitmapMetadata, "/logscrdesc/Width") : decoder.Frames[0].PixelWidth;
        int height = gif ? Query(decoder.Metadata as BitmapMetadata, "/logscrdesc/Height") : decoder.Frames[0].PixelHeight;
        if (width <= 0) width = decoder.Frames.Max(f => f.PixelWidth + Query(f.Metadata as BitmapMetadata, "/imgdesc/Left"));
        if (height <= 0) height = decoder.Frames.Max(f => f.PixelHeight + Query(f.Metadata as BitmapMetadata, "/imgdesc/Top"));
        int count = decoder.Frames.Count;
        if (width > 2048 || height > 2048 || count > 500 || (long)width * height * count > 160_000_000)
            throw new InvalidDataException("图片过大：最多 2048×2048、500 帧，累计不超过 1.6 亿像素。");
        string id = "user-" + Guid.NewGuid().ToString("N"), directory = Path.Combine(userRoot, id);
        Directory.CreateDirectory(directory);
        try
        {
            var asset = new StickerAsset { Id = id, Name = Path.GetFileNameWithoutExtension(file), Reviewed = true,
                Width = width, Height = height, FrameCount = count, DurationsMs = new int[count],
                Poster = $"user/{id}/poster.png", SourceLabel = "我的上传" };
            int columns = Math.Min(6, 4096 / width), capacity = columns * Math.Min(6, 4096 / height);
            var pages = new List<StickerPage>();
            byte[] canvas = new byte[width * height * 4];
            var background = Colors.Transparent;
            int backgroundIndex = Query(decoder.Metadata as BitmapMetadata, "/logscrdesc/BackgroundColorIndex", -1);
            if (gif && decoder.Palette is { } palette && backgroundIndex >= 0 && backgroundIndex < palette.Colors.Count
                && Query(decoder.Frames[0].Metadata as BitmapMetadata, "/grctlext/TransparencyFlag") == 0)
                background = palette.Colors[backgroundIndex];
            void Clear(int left, int top, int w, int h, Color color)
            {
                for (int y = top; y < top + h; y++) for (int x = left; x < left + w; x++)
                { int p = (y * width + x) * 4; canvas[p] = color.B; canvas[p + 1] = color.G; canvas[p + 2] = color.R; canvas[p + 3] = color.A; }
            }
            Clear(0, 0, width, height, background);
            void Save(string path, byte[] pixels, int w, int h)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4)));
                using var output = File.Create(path); encoder.Save(output);
            }
            for (int first = 0; first < count; first += capacity)
            {
                int length = Math.Min(capacity, count - first), atlasWidth = columns * width, atlasHeight = ((length + columns - 1) / columns) * height;
                byte[] atlas = new byte[atlasWidth * atlasHeight * 4];
                for (int i = first; i < first + length; i++)
                {
                    var frame = decoder.Frames[i]; var metadata = frame.Metadata as BitmapMetadata;
                    int left = gif ? Query(metadata, "/imgdesc/Left") : 0, top = gif ? Query(metadata, "/imgdesc/Top") : 0;
                    int fw = frame.PixelWidth, fh = frame.PixelHeight;
                    if (left < 0 || top < 0 || left + fw > width || top + fh > height) throw new InvalidDataException("GIF 帧超出画布。");
                    int disposal = gif ? Query(metadata, "/grctlext/Disposal") : 0;
                    byte[]? previous = disposal == 3 ? (byte[])canvas.Clone() : null;
                    var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                    byte[] source = new byte[fw * fh * 4]; converted.CopyPixels(source, fw * 4, 0);
                    for (int y = 0; y < fh; y++) for (int x = 0; x < fw; x++)
                    {
                        int s = (y * fw + x) * 4, d = ((top + y) * width + left + x) * 4;
                        if (source[s + 3] != 0) Buffer.BlockCopy(source, s, canvas, d, 4);
                    }
                    int local = i - first, ox = local % columns * width, oy = local / columns * height;
                    for (int y = 0; y < height; y++) Buffer.BlockCopy(canvas, y * width * 4, atlas, ((oy + y) * atlasWidth + ox) * 4, width * 4);
                    if (i == 0) Save(Path.Combine(directory, "poster.png"), canvas, width, height);
                    int delay = gif ? Query(metadata, "/grctlext/Delay") * 10 : 1000;
                    asset.DurationsMs[i] = delay == 0 ? 100 : Math.Max(20, delay);
                    if (disposal == 2) Clear(left, top, fw, fh, Query(metadata, "/grctlext/TransparencyFlag") != 0 ? Colors.Transparent : background);
                    if (previous is not null) canvas = previous;
                }
                string page = $"page-{pages.Count}.png";
                Save(Path.Combine(directory, page), atlas, atlasWidth, atlasHeight);
                pages.Add(new StickerPage { File = $"user/{id}/{page}", Columns = columns, FirstFrame = first, FrameCount = length });
            }
            asset.Pages = pages.ToArray();
            File.Copy(file, Path.Combine(directory, gif ? "original.gif" : "original.png"));
            return asset;
        }
        catch { Directory.Delete(directory, true); throw; }
    }
}
