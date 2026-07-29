using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using YierPet.Windows.Core;

namespace YierPet.Windows;

public partial class MainWindow : Window
{
    private const double ThrowThreshold = 550;
    private const double Gravity = 3000;
    private const double Bounce = 0.45;
    private const double WallBounce = 0.60;

    private readonly DispatcherTimer _frameTimer = new();
    private readonly DispatcherTimer _behaviorTimer = new();
    private readonly DispatcherTimer _walkTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _walkStopTimer = new();
    private readonly DispatcherTimer _throwTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _healthTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _speechTimer = new();
    private readonly List<(Point Point, DateTime At)> _samples = [];
    private readonly SpriteAnimator _animator;
    private readonly AppSettings _settings;
    private readonly ReminderService _reminders;

    private PetState _state = PetState.Idle;
    private int _frameIndex;
    private int _remainingLoops = -1;
    private bool _dragging;
    private bool _moved;
    private Point _dragStartScreen;
    private Point _dragStartWindow;
    private double _walkDirection = 1;
    private Vector _throwVelocity;
    private double _maxImpact;

    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsStore.Load();
        _animator = new SpriteAnimator();
        _reminders = new ReminderService(_settings, HandleReminder, SetHeat);

        _frameTimer.Tick += (_, _) => AdvanceFrame();
        _behaviorTimer.Tick += (_, _) => RandomBehavior();
        _walkTimer.Tick += (_, _) => AdvanceWalk();
        _walkStopTimer.Tick += (_, _) => StopWalking();
        _throwTimer.Tick += (_, _) => AdvanceThrow();
        _healthTimer.Tick += (_, _) => _reminders.Tick();
        _speechTimer.Tick += (_, _) => SpeechPopup.IsOpen = false;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 48;
        Top = area.Bottom - Height - 36;
        SetState(PetState.Idle);
        _healthTimer.Start();
        ScheduleBehavior();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        foreach (var timer in new[] { _frameTimer, _behaviorTimer, _walkTimer, _walkStopTimer, _throwTimer, _healthTimer, _speechTimer })
            timer.Stop();
    }

    private void SetState(PetState state)
    {
        _remainingLoops = -1;
        SetStateCore(state);
    }

    private void PlayOnce(PetState state, int loops = 1)
    {
        _remainingLoops = Math.Max(1, loops);
        SetStateCore(state);
    }

    private void SetStateCore(PetState state)
    {
        _state = state;
        _frameIndex = 0;
        DisplayFrame();
        _frameTimer.Start();
    }

    private void AdvanceFrame()
    {
        _frameIndex++;
        if (_frameIndex >= _state.Durations().Count)
        {
            _frameIndex = 0;
            if (_remainingLoops > 0 && --_remainingLoops == 0)
            {
                SetState(PetState.Idle);
                return;
            }
        }
        DisplayFrame();
    }

    private void DisplayFrame()
    {
        var frame = _animator.Frame(_state, _frameIndex);
        PetImage.Source = frame;
        HeatOverlay.OpacityMask = new ImageBrush(frame) { Stretch = Stretch.Fill };
        _frameTimer.Interval = TimeSpan.FromMilliseconds(_state.Durations()[_frameIndex]);
    }

    private void ScheduleBehavior()
    {
        _behaviorTimer.Stop();
        if (!_settings.RandomBehaviourEnabled) return;
        _behaviorTimer.Interval = TimeSpan.FromSeconds(Random.Shared.Next(7, 16));
        _behaviorTimer.Start();
    }

    private void RandomBehavior()
    {
        ScheduleBehavior();
        if (!_settings.RandomBehaviourEnabled || _dragging || _throwTimer.IsEnabled || _walkTimer.IsEnabled) return;
        switch (Random.Shared.Next(10))
        {
            case <= 4: StartWalking(Random.Shared.Next(2) == 0 ? -1 : 1); break;
            case <= 6: PlayOnce(PetState.Jumping, 2); break;
            case 7: PlayOnce(PetState.Waving, 2); break;
            case 8: PlayOnce(PetState.Review, 2); break;
            default: PlayOnce(PetState.Waiting, 2); break;
        }
    }

    private void StartWalking(double direction)
    {
        _walkDirection = Math.Sign(direction) == 0 ? 1 : Math.Sign(direction);
        SetState(_walkDirection > 0 ? PetState.RunningRight : PetState.RunningLeft);
        _walkTimer.Start();
        _walkStopTimer.Interval = TimeSpan.FromSeconds(Random.Shared.Next(2, 6));
        _walkStopTimer.Start();
    }

    private void AdvanceWalk()
    {
        var area = SystemParameters.WorkArea;
        var next = Left + _walkDirection * 2;
        if (next <= area.Left || next + Width >= area.Right)
        {
            _walkDirection *= -1;
            SetState(_walkDirection > 0 ? PetState.RunningRight : PetState.RunningLeft);
        }
        Left = Math.Clamp(next, area.Left, area.Right - Width);
        Top = Math.Clamp(Top, area.Top, area.Bottom - Height);
    }

    private void StopWalking()
    {
        _walkTimer.Stop();
        _walkStopTimer.Stop();
        if (!_dragging && !_throwTimer.IsEnabled) SetState(PetState.Idle);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        StopThrow();
        StopWalking();
        _dragging = true;
        _moved = false;
        _dragStartScreen = PointToScreen(e.GetPosition(this));
        _dragStartWindow = new Point(Left, Top);
        _samples.Clear();
        RecordSample(_dragStartScreen);
        Mouse.Capture(this);
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var screen = PointToScreen(e.GetPosition(this));
        var delta = screen - _dragStartScreen;
        _moved |= delta.Length > 3;
        Left = _dragStartWindow.X + delta.X;
        Top = _dragStartWindow.Y + delta.Y;
        RecordSample(screen);
        SetState(delta.X >= 0 ? PetState.RunningRight : PetState.RunningLeft);
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        RecordSample(PointToScreen(e.GetPosition(this)));
        _dragging = false;
        Mouse.Capture(null);
        var velocity = ReleaseVelocity();
        if (_moved && velocity.Length >= ThrowThreshold) StartThrow(velocity);
        else if (!_moved) PlayOnce(PetState.Waving, 2);
        else SetState(PetState.Idle);
        e.Handled = true;
    }

    private void RecordSample(Point point)
    {
        var now = DateTime.UtcNow;
        _samples.Add((point, now));
        _samples.RemoveAll(sample => now - sample.At > TimeSpan.FromMilliseconds(120));
    }

    private Vector ReleaseVelocity()
    {
        if (_samples.Count < 2) return new Vector();
        var first = _samples[0];
        var last = _samples[^1];
        var seconds = (last.At - first.At).TotalSeconds;
        return seconds <= 0 ? new Vector() : (last.Point - first.Point) / seconds;
    }

    private void StartThrow(Vector velocity)
    {
        StopWalking();
        _throwVelocity = velocity;
        _maxImpact = 0;
        _throwTimer.Start();
        SetState(PetState.Jumping);
    }

    private void AdvanceThrow()
    {
        const double dt = 1d / 60d;
        var area = SystemParameters.WorkArea;
        _throwVelocity.Y += Gravity * dt;
        Left += _throwVelocity.X * dt;
        Top += _throwVelocity.Y * dt;

        if (Left < area.Left)
        {
            Left = area.Left;
            _throwVelocity.X = Math.Abs(_throwVelocity.X) * WallBounce;
        }
        else if (Left + Width > area.Right)
        {
            Left = area.Right - Width;
            _throwVelocity.X = -Math.Abs(_throwVelocity.X) * WallBounce;
        }

        if (Top < area.Top)
        {
            Top = area.Top;
            _throwVelocity.Y = Math.Abs(_throwVelocity.Y) * Bounce;
        }
        else if (Top + Height > area.Bottom)
        {
            Top = area.Bottom - Height;
            _maxImpact = Math.Max(_maxImpact, Math.Abs(_throwVelocity.Y));
            _throwVelocity.Y = -Math.Abs(_throwVelocity.Y) * Bounce;
            _throwVelocity.X *= 0.88;
            if (Math.Abs(_throwVelocity.Y) < 120) StopThrow();
        }
    }

    private void StopThrow()
    {
        if (!_throwTimer.IsEnabled) return;
        _throwTimer.Stop();
        if (_maxImpact >= 700)
        {
            PlayOnce(PetState.Failed, 2);
            Say("请轻拿轻放一二大王！");
        }
        else SetState(PetState.Idle);
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = CreateMenu(Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private ContextMenu CreateMenu(bool includeTests)
    {
        var menu = new ContextMenu();
        var actions = new MenuItem { Header = "切换动作" };
        foreach (var state in Enum.GetValues<PetState>())
        {
            var item = new MenuItem { Header = state.DisplayName(), IsCheckable = true, IsChecked = _state == state };
            item.Click += (_, _) => { StopWalking(); StopThrow(); SetState(state); };
            actions.Items.Add(item);
        }

        var pause = new MenuItem { Header = "暂停随机行为", IsCheckable = true, IsChecked = !_settings.RandomBehaviourEnabled };
        pause.Click += (_, _) =>
        {
            _settings.RandomBehaviourEnabled = !pause.IsChecked;
            SettingsStore.Save(_settings);
            if (_settings.RandomBehaviourEnabled) ScheduleBehavior(); else _behaviorTimer.Stop();
        };

        menu.Items.Add(actions);
        menu.Items.Add(pause);
        menu.Items.Add(ReminderGroup("健康提醒", _reminders.HealthKinds));
        menu.Items.Add(ReminderGroup("系统哨兵", _reminders.SystemKinds));
        if (includeTests)
        {
            var tests = new MenuItem { Header = "测试提醒" };
            foreach (var kind in Enum.GetValues<ReminderKind>())
            {
                var item = new MenuItem { Header = ReminderTitle(kind) };
                item.Click += (_, _) => _reminders.TriggerTest(kind);
                tests.Items.Add(item);
            }
            menu.Items.Add(tests);
        }
        menu.Items.Add(new Separator());
        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exit);
        return menu;
    }

    private MenuItem ReminderGroup(string title, IEnumerable<ReminderKind> kinds)
    {
        var group = new MenuItem { Header = title };
        foreach (var kind in kinds)
        {
            var item = new MenuItem { Header = ReminderTitle(kind), IsCheckable = true, IsChecked = _settings.IsEnabled(kind) };
            item.Click += (_, _) => { _settings.SetEnabled(kind, item.IsChecked); SettingsStore.Save(_settings); };
            group.Items.Add(item);
        }
        return group;
    }

    private void HandleReminder(ReminderKind kind, string message)
    {
        switch (kind)
        {
            case ReminderKind.Sedentary: PlayOnce(PetState.Jumping, 3); break;
            case ReminderKind.Water:
            case ReminderKind.BatteryFull: PlayOnce(PetState.Waving, 2); break;
            case ReminderKind.LateNight:
            case ReminderKind.MemoryPressure: PlayOnce(PetState.Waiting, 2); break;
            case ReminderKind.Slacking:
            case ReminderKind.DiskFull: PlayOnce(PetState.Review, 2); break;
            case ReminderKind.CpuHigh: PlayOnce(PetState.Running, 3); break;
            case ReminderKind.BatteryLow: PlayOnce(PetState.Failed, 2); break;
        }
        Say(message);
    }

    private void SetHeat(double level) => HeatOverlay.Opacity = Math.Clamp(level * 0.72, 0, 0.72);

    private void Say(string message)
    {
        SpeechText.Text = message;
        SpeechPopup.IsOpen = true;
        _speechTimer.Stop();
        _speechTimer.Interval = TimeSpan.FromSeconds(4);
        _speechTimer.Start();
    }

    private static string ReminderTitle(ReminderKind kind) => kind switch
    {
        ReminderKind.Sedentary => "久坐提醒",
        ReminderKind.Water => "喝水提醒",
        ReminderKind.LateNight => "深夜关怀",
        ReminderKind.Slacking => "摸鱼检测",
        ReminderKind.CpuHigh => "CPU 红温",
        ReminderKind.MemoryPressure => "内存压力",
        ReminderKind.BatteryLow => "电量提醒",
        ReminderKind.BatteryFull => "充满提醒",
        ReminderKind.DiskFull => "磁盘空间",
        _ => kind.ToString()
    };
}
