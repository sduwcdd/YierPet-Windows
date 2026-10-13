using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using YierPet.Windows.Core;

namespace YierPet.Windows;

public sealed class PairWindow : Window
{
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private bool _bubuOnLeft;
    public PairWindow(DuoCoordinator coordinator)
    {
        Width = 308; Height = 166;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        Cursor = Cursors.Hand;
        Title = "一二和布布 · 在一起";
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/YierPet.Windows;component/Assets/app-icon.png"));
        var root = new Grid { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
        root.Children.Add(_image);
        Content = root;
        MouseLeftButtonDown += (_, e) =>
        {
            var point = PointToScreen(e.GetPosition(this));
            var pet = (e.GetPosition(this).X < Width / 2) == _bubuOnLeft ? coordinator.Bubu : coordinator.Yier;
            coordinator.CancelInteraction();
            pet.BeginPointerDrag(point);
            e.Handled = true;
        };
        MouseRightButtonUp += (_, e) =>
        {
            var pet = (e.GetPosition(this).X < Width / 2) == _bubuOnLeft ? coordinator.Bubu : coordinator.Yier;
            coordinator.CancelInteraction();
            pet.OpenMenu();
            e.Handled = true;
        };
    }
    public void SetSticker(StickerLibrary library, StickerAsset asset, int frame, string leftCharacter)
    {
        _bubuOnLeft = leftCharacter == "bubu";
        _image.Source = library.Frame(asset, frame);
    }
}
