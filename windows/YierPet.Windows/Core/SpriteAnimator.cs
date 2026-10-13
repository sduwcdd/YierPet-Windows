using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YierPet.Windows.Core;

public enum PetCharacter { Yier, Bubu }
public enum PairAction { Greet, Hug, Nuzzle }

public sealed class SpriteAnimator
{
    private readonly PetCharacter _character;
    private readonly Dictionary<(PetState, int), BitmapSource> _frames = [];

    public SpriteAnimator(PetCharacter character = PetCharacter.Yier) => _character = character;

    public BitmapSource Frame(PetState state, int frameIndex)
    {
        var index = ((frameIndex % 4) + 4) % 4;
        var key = (state, index);
        if (_frames.TryGetValue(key, out var cached)) return cached;
        BitmapSource frame;
        if (state is PetState.Idle or PetState.RunningRight or PetState.RunningLeft or PetState.Waving)
        {
            var row = state switch { PetState.Idle => 0, PetState.Waving => 2, _ => 1 };
            row += _character == PetCharacter.Bubu ? 3 : 0;
            frame = SpriteAtlas.Solo[row][index];
        }
        else
        {
            var row = state switch
            {
                PetState.Jumping => 0, PetState.Failed => 1, PetState.Waiting => 2,
                PetState.Running => 3, PetState.Review => 4, _ => 2
            };
            frame = (_character == PetCharacter.Yier ? SpriteAtlas.YierExtra : SpriteAtlas.BubuExtra)[row][index];
        }
        if (state == PetState.RunningLeft)
        {
            frame = new TransformedBitmap(frame, new ScaleTransform(-1, 1));
            frame.Freeze();
        }
        return _frames[key] = frame;
    }

    public static BitmapSource PairFrame(PairAction action, int index) =>
        SpriteAtlas.Pair[action == PairAction.Nuzzle ? 1 : 0][((index % 4) + 4) % 4];
}

// Measured gutters avoid clipping feet. Alpha bounds normalize drawing placement
// at load time; the original generated PNG files remain unchanged.
internal static class SpriteAtlas
{
    private static readonly Lazy<BitmapSource[][]> SoloSource = new(() =>
        Read("duo-solo-v1.png", [0, 280, 520, 776, 1030, 1270, 1536], 192));
    private static readonly Lazy<BitmapSource[][]> PairSource = new(() =>
        Read("duo-pair-v1.png", [0, 444, 887], 384));
    private static readonly Lazy<BitmapSource[][]> YierSource = new(() =>
        Read("yier-extra-v1.png", [0, 320, 604, 866, 1120, 1402], 192));
    private static readonly Lazy<BitmapSource[][]> BubuSource = new(() =>
        Read("bubu-extra-v1.png", [0, 320, 604, 866, 1120, 1402], 192));
    internal static BitmapSource[][] Solo => SoloSource.Value;
    internal static BitmapSource[][] Pair => PairSource.Value;
    internal static BitmapSource[][] YierExtra => YierSource.Value;
    internal static BitmapSource[][] BubuExtra => BubuSource.Value;

    private static BitmapSource[][] Read(string name, int[] rows, int outputWidth)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/YierPet.Windows;component/Assets/{name}");
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        if (image.PixelHeight != rows[^1]) throw new InvalidOperationException($"动画图集尺寸不匹配：{name}");
        var bitmap = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var result = new BitmapSource[rows.Length - 1][];
        for (int row = 0; row < result.Length; row++)
        {
            var bounds = new Int32Rect[4];
            for (int col = 0; col < 4; col++)
            {
                int left = col * bitmap.PixelWidth / 4, right = (col + 1) * bitmap.PixelWidth / 4;
                int minX = right, maxX = left, minY = rows[row + 1], maxY = rows[row];
                for (int y = rows[row]; y < rows[row + 1]; y++)
                for (int x = left; x < right; x++)
                {
                    if (pixels[y * stride + x * 4 + 3] < 24) continue;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
                if (minX > maxX || minY > maxY) throw new InvalidOperationException($"空动画帧：{name}/{row}/{col}");
                bounds[col] = new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
            }
            double scale = Math.Min((outputWidth - 24d) / bounds.Max(b => b.Width), 180d / bounds.Max(b => b.Height));
            result[row] = new BitmapSource[4];
            for (int col = 0; col < 4; col++)
            {
                var frameBounds = bounds[col];
                double width = frameBounds.Width * scale, height = frameBounds.Height * scale;
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                    dc.DrawImage(new CroppedBitmap(image, frameBounds), new Rect((outputWidth - width) / 2, 200 - height, width, height));
                var frame = new RenderTargetBitmap(outputWidth, 208, 96, 96, PixelFormats.Pbgra32);
                frame.Render(visual);
                frame.Freeze();
                result[row][col] = frame;
            }
        }
        return result;
    }
}
