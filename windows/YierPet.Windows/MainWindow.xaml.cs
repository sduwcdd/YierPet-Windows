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
    // Tight-cropped baseline art otherwise fills 166 DIP, while common original
    // animations display figures around 138-153 DIP tall. Keep the feet anchored.
    private const double BaselineScale = 0.85;

    private readonly DispatcherTimer _frameTimer = new();
    private readonly DispatcherTimer _behaviorTimer = new();
    private readonly DispatcherTimer _walkTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _walkStopTimer = new();
    private readonly DispatcherTimer _throwTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _speechTimer = new();
    private readonly List<(Point Point, DateTime At)> _samples = [];
    private StickerAsset? _sticker;
    private bool _mirrorFrame;
    private string? _lastRandomStickerId;
    private readonly AppSettings _settings;
    private readonly DuoCoordinator _coordinator;
    private ReminderService Reminders => _coordinator.Reminders;
    private bool _coordinated;
    private bool _menuOpen;
    public PetCharacter Character { get; }
    public string PetName => Character == PetCharacter.Yier ? "一二" : "布布";
    public bool IsUserBusy => _dragging || _throwTimer.IsEnabled || _menuOpen;
    public bool IsPlayingGesture => _remainingLoops > 0;
    public bool IsCoordinated => _coordinated;
    public bool CanBeCaught => _throwTimer.IsEnabled && _throwVelocity.Y > 0 && _maxImpact == 0;

    private PetState _state = PetState.Idle;
    private int _frameIndex;
    private int _remainingLoops = -1;
    private bool _dragging;
    private bool _moved;
    private Point _dragStartScreenPixels;
    private Point _dragStartWindow;
    private double _walkDirection = 1;
    private Vector _throwVelocity;
    private double _maxImpact;

    public MainWindow(PetCharacter character, DuoCoordinator coordinator)
    {
        InitializeComponent();
        Character = character;
        _coordinator = coordinator;
        _settings = coordinator.Settings;
        Title = $"{PetName} · 一二和布布";

        _frameTimer.Tick += (_, _) => AdvanceFrame();
        _behaviorTimer.Tick += (_, _) => RandomBehavior();
        _walkTimer.Tick += (_, _) => AdvanceWalk();
        _walkStopTimer.Tick += (_, _) => StopWalking();
        _throwTimer.Tick += (_, _) => AdvanceThrow();
        _speechTimer.Tick += (_, _) => SpeechPopup.IsOpen = false;
        LostMouseCapture += (_, _) =>
        {
            if (!_dragging) return;
            _dragging = false;
            SetState(PetState.Idle);
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SetState(PetState.Idle);
        ScheduleBehavior();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        foreach (var timer in new[] { _frameTimer, _behaviorTimer, _walkTimer, _walkStopTimer, _throwTimer, _speechTimer })
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
        _sticker = _coordinator.Library.State(Character, state);
        _mirrorFrame = _coordinator.Library.StateMirrored(Character, state);
        _frameIndex = 0;
        DisplayFrame();
        _frameTimer.Start();
    }

    private void AdvanceFrame()
    {
        _frameIndex++;
        if (_frameIndex >= (_sticker?.FrameCount ?? _state.Durations().Count))
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
        var frame = _sticker is { } clip ? _coordinator.Library.Frame(clip, _frameIndex, _mirrorFrame, stabilizeLocomotion: clip.IsLocomotion)
            : _coordinator.Library.Baseline(Character);
        double scale = _sticker is null ? BaselineScale : 1;
        PetImage.Width = 154 * scale;
        PetImage.Height = 166 * scale;
        PetImage.Source = frame;
        _frameTimer.Interval = TimeSpan.FromMilliseconds(_sticker?.DurationsMs[_frameIndex] ?? _state.Durations()[_frameIndex]);
    }

    public void PlaySticker(StickerAsset asset, int loops = 1, bool mirror = false)
    {
        StopWalking(); StopThrow();
        _state = PetState.Idle; _sticker = asset; _frameIndex = 0;
        _mirrorFrame = mirror;
        _remainingLoops = Math.Max(asset.FrameCount == 1 ? (int)Math.Ceiling(3 / asset.DurationSeconds) : 1, loops);
        DisplayFrame(); _frameTimer.Start();
    }

    public void RefreshStickerAvailability()
    {
        if (_sticker is not null && !_coordinator.Library.IsEnabled(_sticker)) SetState(PetState.Idle);
    }

    private void ScheduleBehavior()
    {
        _behaviorTimer.Stop();
        if (!_settings.RandomBehaviourEnabled) return;
        _behaviorTimer.Interval = TimeSpan.FromSeconds(4 + Random.Shared.NextDouble() * 4);
        _behaviorTimer.Start();
    }

    private void RandomBehavior()
    {
        ScheduleBehavior();
        if (!_settings.RandomBehaviourEnabled || !IsVisible || IsUserBusy || _coordinated || IsPlayingGesture
            || _walkTimer.IsEnabled) return;
        var pool = _coordinator.Library.SoloCandidates(Character, _lastRandomStickerId);
        if (pool.Count > 0 && Random.Shared.Next(10) < 8)
        {
            var asset = pool[Random.Shared.Next(pool.Count)];
            _lastRandomStickerId = asset.Id;
            if (!_coordinator.TryPlaySemantic(asset))
                PlaySticker(asset, Math.Clamp((int)Math.Ceiling(3 / asset.DurationSeconds), 1, 4));
        }
        else StartWalking(Random.Shared.Next(2) == 0 ? -1 : 1);
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
        BeginPointerDrag(PointToScreen(e.GetPosition(this)));
        e.Handled = true;
    }

    public void BeginPointerDrag(Point screenPixels)
    {
        _coordinator.UserInterrupted();
        _coordinated = false;
        StopThrow();
        StopWalking();
        _dragging = true;
        _moved = false;
        _dragStartScreenPixels = screenPixels;
        _dragStartWindow = new Point(Left, Top);
        _samples.Clear();
        RecordSample(_dragStartScreenPixels);
        Mouse.Capture(this);
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var screenPixels = PointToScreen(e.GetPosition(this));
        var delta = DeviceVectorToDips(screenPixels - _dragStartScreenPixels);
        _moved |= delta.Length > 3;
        Left = _dragStartWindow.X + delta.X;
        Top = _dragStartWindow.Y + delta.Y;
        RecordSample(screenPixels);
        var direction = delta.X >= 0 ? PetState.RunningRight : PetState.RunningLeft;
        if (_moved && _state != direction) SetState(direction);
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
        _coordinator.OnReleased(this, _moved);
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
        return seconds <= 0 ? new Vector() : DeviceVectorToDips(last.Point - first.Point) / seconds;
    }

    private Vector DeviceVectorToDips(Vector deviceVector)
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget is { } target
            ? target.TransformFromDevice.Transform(deviceVector)
            : deviceVector;
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
        var previous = new Point(Left + Width / 2, Top + Height / 2);
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

        // Sweep the falling path, not just its endpoint, so a fast throw cannot skip Bubu.
        // Stop the sweep at first ground contact; never catch a trajectory after it hit the floor.
        var next = new Point(Left + Width / 2, Top + Height / 2);
        double groundCenter = area.Bottom - Height / 2;
        if (next.Y > groundCenter && next.Y > previous.Y)
            next = previous + (next - previous) * Math.Clamp((groundCenter - previous.Y) / (next.Y - previous.Y), 0, 1);
        if (CanBeCaught && _coordinator.TryCatchThrownYier(this, previous, next)) return;

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
            Say(Character == PetCharacter.Yier ? "请轻拿轻放一二大王！" : "晕乎乎……轻一点嘛～");
        }
        else SetState(PetState.Idle);
    }

    internal void EndCaughtThrow()
    {
        _throwTimer.Stop();
        _throwVelocity = default;
        _maxImpact = 0;
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        OpenMenu();
        e.Handled = true;
    }

    public void OpenMenu()
    {
        _coordinator.UserInterrupted();
        StopWalking();
        _menuOpen = true;
        var menu = CreateMenu(Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
        menu.Closed += (_, _) => _menuOpen = false;
        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private ContextMenu CreateMenu(bool includeTests)
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = $"{PetName} · 今天也要在一起", IsEnabled = false });
        var library = new MenuItem { Header = "表情与互动 · 上传 / 编辑" };
        library.Click += (_, _) => _coordinator.OpenLibrary();
        menu.Items.Add(library);
        var voice = new MenuItem { Header = "语音工坊 · 生成 / 下载" };
        voice.Click += (_, _) => _coordinator.OpenVoice(Character);
        menu.Items.Add(voice);
        var sourceInteractions = new MenuItem { Header = "原表情关联互动", IsEnabled = _coordinator.BothEnabled };
        foreach (var rule in _coordinator.Library.MenuInteractions(false))
        {
            var link = new MenuItem { Header = rule.Name };
            link.Click += (_, _) => { menu.IsOpen = false; _menuOpen = false; _coordinator.RequestManualInteraction(rule); };
            sourceInteractions.Items.Add(link);
        }
        menu.Items.Add(sourceInteractions);
        var together = new MenuItem { Header = "和对方互动", IsEnabled = _coordinator.BothEnabled };
        foreach (var rule in _coordinator.Library.MenuInteractions(true))
        {
            var item = new MenuItem { Header = rule.Name };
            item.Click += (_, _) => { menu.IsOpen = false; _menuOpen = false; _coordinator.RequestManualInteraction(rule); };
            together.Items.Add(item);
        }
        menu.Items.Add(together);
        var automatic = new MenuItem { Header = "自动情侣互动", IsCheckable = true, IsChecked = _settings.AutomaticInteractionsEnabled };
        automatic.Click += (_, _) => { _settings.AutomaticInteractionsEnabled = automatic.IsChecked; _coordinator.SettingsChanged(); };
        menu.Items.Add(automatic);
        var follow = new MenuItem { Header = "布布跟着一二", IsCheckable = true, IsChecked = _settings.BubuFollowsYier };
        follow.Click += (_, _) => { _settings.BubuFollowsYier = follow.IsChecked; _coordinator.SettingsChanged(); };
        menu.Items.Add(follow);
        foreach (var character in Enum.GetValues<PetCharacter>())
        {
            bool isYier = character == PetCharacter.Yier;
            bool visible = isYier ? _settings.YierVisible : _settings.BubuVisible;
            var show = new MenuItem { Header = isYier ? "显示一二" : "显示布布", IsCheckable = true, IsChecked = visible,
                IsEnabled = !visible || _coordinator.BothEnabled };
            show.Click += (_, _) =>
            {
                if (isYier) _settings.YierVisible = show.IsChecked; else _settings.BubuVisible = show.IsChecked;
                _coordinator.SettingsChanged();
            };
            menu.Items.Add(show);
        }
        var recall = new MenuItem { Header = "回到屏幕右下角" };
        recall.Click += (_, _) => _coordinator.Recall();
        menu.Items.Add(recall);
        menu.Items.Add(new Separator());
        var actions = new MenuItem { Header = "切换动作" };
        foreach (var (label, direction) in new[] { ("站立休息", 0), ("向左走", -1), ("向右走", 1) })
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) =>
            {
                menu.IsOpen = false; _menuOpen = false; _coordinator.CancelInteraction(); StopWalking(); StopThrow();
                if (direction == 0) SetState(PetState.Idle); else StartWalking(direction);
            };
            actions.Items.Add(item);
        }
        foreach (var asset in _coordinator.Library.MenuActions(Character))
        {
            var item = new MenuItem { Header = asset.Name };
            item.Click += (_, _) => { menu.IsOpen = false; _menuOpen = false; _coordinator.PlayLibraryAsset(asset); };
            actions.Items.Add(item);
        }

        var pause = new MenuItem { Header = "暂停随机行为", IsCheckable = true, IsChecked = !_settings.RandomBehaviourEnabled };
        pause.Click += (_, _) =>
        {
            _settings.RandomBehaviourEnabled = !pause.IsChecked;
            _coordinator.SettingsChanged();
        };

        menu.Items.Add(actions);
        menu.Items.Add(pause);
        menu.Items.Add(ReminderGroup("健康提醒", Reminders.HealthKinds));
        if (includeTests)
        {
            var tests = new MenuItem { Header = "测试提醒" };
            foreach (var kind in Enum.GetValues<ReminderKind>())
            {
                var item = new MenuItem { Header = ReminderTitle(kind) };
                item.Click += (_, _) => Reminders.TriggerTest(kind);
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
        group.Items.Add(new Separator());
        var speak = new MenuItem { Header = "提醒时自动说话", IsCheckable = true };
        try { speak.IsChecked = new VoiceStore().Load().ReminderSpeechEnabled; }
        catch { speak.IsEnabled = false; }
        speak.Click += (_, _) =>
        {
            try
            {
                var store = new VoiceStore(); var settings = store.Load();
                settings.ReminderSpeechEnabled = speak.IsChecked; store.Save(settings);
                if (!speak.IsChecked) _coordinator.StopReminderSpeech();
            }
            catch (Exception ex) { MessageBox.Show("无法保存语音设置：" + ex.Message); }
        };
        group.Items.Add(speak);
        var preview = new MenuItem { Header = "试听一条提醒" };
        preview.Click += (_, _) =>
        {
            if (!Reminders.HealthKinds.Any(_settings.IsEnabled)) { Say("请先启用至少一类健康提醒。"); return; }
            Reminders.TriggerTest(Reminders.HealthKinds.First(_settings.IsEnabled));
        };
        group.Items.Add(preview);
        return group;
    }

    public void HandleReminder(ReminderKind kind, string message)
    {
        switch (kind)
        {
            case ReminderKind.Sedentary: PlayOnce(PetState.Jumping, 3); break;
            case ReminderKind.Water:
            PlayOnce(PetState.Waving, 2); break;
            case ReminderKind.LateNight:
            PlayOnce(PetState.Waiting, 2); break;
            case ReminderKind.Slacking:
            PlayOnce(PetState.Review, 2); break;
        }
        Say(message);
    }


    public void BeginCoordinated()
    {
        if (_coordinated) return;
        StopWalking(); StopThrow();
        SpeechPopup.IsOpen = false;
        _coordinated = true;
        SetState(PetState.Idle);
    }

    public bool MoveCoordinated(Point target, double seconds)
    {
        var delta = target - new Point(Left, Top);
        double step = 115 * seconds;
        if (delta.Length <= Math.Max(1, step))
        {
            Left = target.X; Top = target.Y;
            if (_state != PetState.Idle) SetState(PetState.Idle);
            return true;
        }
        var direction = delta.X >= 0 ? PetState.RunningRight : PetState.RunningLeft;
        if (_state != direction) SetState(direction);
        delta.Normalize();
        Left += delta.X * step; Top += delta.Y * step;
        return false;
    }

    public void MoveFollowing(Point target, double seconds)
    {
        // Following adjusts only this pet's position; it must not acquire the
        // two-character interaction lock or block the independent behavior timer.
        if (IsUserBusy || IsPlayingGesture) return;
        if (_walkTimer.IsEnabled) StopWalking();
        MoveCoordinated(target, seconds);
    }

    public void ResumeIndependent()
    {
        _coordinated = false;
        StopWalking(); StopThrow();
        SetState(PetState.Idle);
        ScheduleBehavior();
    }

    public void PlayGesture(PetState state, int loops = 1) => PlayOnce(state, loops);

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
        _ => kind.ToString()
    };
}
