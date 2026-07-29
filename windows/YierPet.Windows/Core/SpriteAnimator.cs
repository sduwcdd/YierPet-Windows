using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YierPet.Windows.Core;

public sealed class SpriteAnimator
{
    private const int Columns = 8;
    private const int CellWidth = 192;
    private const int CellHeight = 208;
    private readonly BitmapImage _sheet;
    private readonly Dictionary<(PetState State, int Frame), BitmapSource> _frames = [];

    public SpriteAnimator()
    {
        _sheet = new BitmapImage();
        _sheet.BeginInit();
        _sheet.UriSource = new Uri("pack://application:,,,/Assets/spritesheet.png", UriKind.Absolute);
        _sheet.CacheOption = BitmapCacheOption.OnLoad;
        _sheet.EndInit();
        _sheet.Freeze();
    }

    public BitmapSource Frame(PetState state, int frameIndex)
    {
        var count = state.Durations().Count;
        var index = ((frameIndex % count) + count) % count;
        var key = (state, index);
        if (_frames.TryGetValue(key, out var frame)) return frame;

        frame = new CroppedBitmap(
            _sheet,
            new Int32Rect(index * CellWidth, state.Row() * CellHeight, CellWidth, CellHeight));
        frame.Freeze();
        _frames[key] = frame;
        return frame;
    }
}
