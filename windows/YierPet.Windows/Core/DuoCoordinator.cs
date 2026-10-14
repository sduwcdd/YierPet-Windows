using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace YierPet.Windows.Core;

public enum InteractionPhase { Independent, Approaching, Greeting, Together, Linked }

public sealed class DuoCoordinator : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private readonly Stopwatch _clock = new();
    private double _lastTick, _phaseSeconds, _autoSeconds, _cooldown, _healthSeconds;
    private PairAction _action;
    private Point _yierTarget, _bubuTarget;
    private bool _disposed;
    private StickerInteraction? _interaction;
    private string _interactionLeftCharacter = "yier";
    private int _responseIndex;
    private double _linkedDuration, _togetherDuration, _chaseDirection = 1;
    private StickerLibraryWindow? _libraryWindow;
    private VoiceWindow? _voiceWindow;
    private readonly ReminderVoice _reminderVoice = new();
    public StickerLibrary Library { get; }
    public AppSettings Settings { get; }
    public MainWindow Yier { get; }
    public MainWindow Bubu { get; }
    public PairWindow Pair { get; }
    public ReminderService Reminders { get; }
    public InteractionPhase Phase { get; private set; }
    public bool IsInteracting => Phase != InteractionPhase.Independent;
    public bool BothEnabled => Settings.YierVisible && Settings.BubuVisible;

    public DuoCoordinator(AppSettings settings, string? contentRoot = null, string? userRoot = null)
    {
        Settings = settings;
        Library = new StickerLibrary(settings, contentRoot, userRoot);
        if (!settings.YierVisible && !settings.BubuVisible) settings.YierVisible = true;
        Yier = new MainWindow(PetCharacter.Yier, this);
        Bubu = new MainWindow(PetCharacter.Bubu, this);
        Pair = new PairWindow(this);
        Library.Changed += OnLibraryChanged;
        Reminders = new ReminderService(settings, (kind, message) =>
        {
            CancelInteraction();
            var speaker = Settings.YierVisible ? Yier : Bubu;
            speaker.HandleReminder(kind, message);
            if (_voiceWindow?.IsPreviewActive != true) _reminderVoice.Play(speaker.Character, message);
        });
        _autoSeconds = 35;
        _timer.Tick += (_, _) =>
        {
            double now = _clock.Elapsed.TotalSeconds;
            Advance(Math.Clamp(now - _lastTick, 0, 0.1));
            _lastTick = now;
        };
    }

    public void Start()
    {
        Recall();
        _clock.Restart();
        _lastTick = 0;
        _timer.Start();
    }

    public void Advance(double seconds)
    {
        if (_disposed || seconds <= 0) return;
        _cooldown = Math.Max(0, _cooldown - seconds);
        _healthSeconds += seconds;
        if (_healthSeconds >= 30) { _healthSeconds = 0; Reminders.Tick(); }
        if (IsInteracting)
        {
            if (!BothEnabled || Yier.IsUserBusy || Bubu.IsUserBusy) { CancelInteraction(); return; }
            if (_interaction is not null && !Library.IsAvailable(_interaction)) { CancelInteraction(); return; }
            _phaseSeconds += seconds;
            if (Phase == InteractionPhase.Linked) { AdvanceLinked(seconds); return; }
            if (Phase == InteractionPhase.Approaching)
            {
                bool a = Yier.MoveCoordinated(_yierTarget, seconds);
                bool b = Bubu.MoveCoordinated(_bubuTarget, seconds);
                if (_phaseSeconds > 25) { CancelInteraction(); return; }
                if (!a || !b) return;
                _phaseSeconds = 0;
                if (_interaction?.IsLinked == true)
                {
                    StartLinked();
                }
                else if (_interaction?.Mode == "response")
                {
                    Phase = InteractionPhase.Greeting;
                    _responseIndex = 0;
                    PlayResponsePart();
                }
                else if (_action == PairAction.Greet || _interaction is null)
                {
                    Phase = InteractionPhase.Greeting;
                    Yier.PlayGesture(PetState.Waving, 3);
                    Bubu.PlayGesture(PetState.Waving, 3);
                }
                else
                {
                    Phase = InteractionPhase.Together;
                    _togetherDuration = Math.Max(3, Library.Find(_interaction.AssetIds[0])!.DurationSeconds * 2);
                    Pair.Left = (_yierTarget.X + _bubuTarget.X + Yier.Width) / 2 - Pair.Width / 2;
                    Pair.Top = _yierTarget.Y;
                    Pair.SetSticker(Library, Library.Find(_interaction.AssetIds[0])!, 0, _interactionLeftCharacter);
                    Pair.Show();
                    Yier.Hide(); Bubu.Hide();
                }
            }
            else if (Phase == InteractionPhase.Greeting)
            {
                if (_interaction?.Mode == "response")
                {
                    var part = Library.Find(_interaction.AssetIds[_responseIndex])!;
                    if (_phaseSeconds >= part.DurationSeconds)
                    {
                        _phaseSeconds = 0;
                        _responseIndex++;
                        if (_responseIndex >= _interaction.AssetIds.Length) CancelInteraction();
                        else PlayResponsePart();
                    }
                }
                else if (_phaseSeconds >= 3) CancelInteraction();
            }
            else
            {
                if (_interaction is null || !Library.IsAvailable(_interaction)) { CancelInteraction(); return; }
                var asset = Library.Find(_interaction.AssetIds[0])!;
                if (_phaseSeconds >= _togetherDuration) { CancelInteraction(); return; }
                Pair.SetSticker(Library, asset, asset.FrameAt(_phaseSeconds), _interactionLeftCharacter);
            }
            return;
        }

        if (Settings.BubuFollowsYier && BothEnabled && !Bubu.IsUserBusy && !Yier.IsUserBusy
            && !Bubu.IsPlayingGesture)
        {
            var area = SystemParameters.WorkArea;
            double x = Yier.Left + 170;
            if (x + Bubu.Width > area.Right) x = Yier.Left - 170;
            var target = new Point(Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - Bubu.Width)), Yier.Top);
            Bubu.MoveFollowing(target, seconds);
        }
        _autoSeconds -= seconds;
        if (_autoSeconds <= 0)
        {
            _autoSeconds = Random.Shared.Next(35, 66);
            if (Settings.AutomaticInteractionsEnabled && BothEnabled && _cooldown == 0
                && !Yier.IsUserBusy && !Bubu.IsUserBusy && !Yier.IsPlayingGesture && !Bubu.IsPlayingGesture)
            {
                var rules = Library.AvailableInteractions.Where(r => r.Automatic).ToArray();
                if (rules.Length > 0) RequestInteraction(rules[Random.Shared.Next(rules.Length)]);
            }
        }
    }

    public bool RequestPair(PairAction action)
    {
        string id = action == PairAction.Hug ? "hug" : "nuzzle";
        var rule = action == PairAction.Greet ? null : Library.AvailableInteractions.FirstOrDefault(r => r.Id == id);
        if (action != PairAction.Greet && rule is null) return false;
        return BeginInteraction(action, rule);
    }

    public bool RequestInteraction(StickerInteraction rule) =>
        Library.IsAvailable(rule) && BeginInteraction(PairAction.Hug, rule);

    public bool RequestManualInteraction(StickerInteraction rule)
    {
        if (!BothEnabled || Yier.IsUserBusy || Bubu.IsUserBusy || !Library.IsAvailable(rule)) return false;
        CancelInteraction();
        Yier.ResumeIndependent(); Bubu.ResumeIndependent();
        return RequestInteraction(rule);
    }

    // A suggested partner is optional: never steal a busy pet's independent clip.
    public bool TryPlaySemantic(StickerAsset trigger)
    {
        if (!Settings.AutomaticInteractionsEnabled || _cooldown > 0 || IsInteracting || !BothEnabled
            || Yier.IsUserBusy || Bubu.IsUserBusy || Yier.IsPlayingGesture || Bubu.IsPlayingGesture) return false;
        var matches = Library.LinkedCandidates(trigger.Id).ToArray();
        return matches.Length > 0 && RequestInteraction(matches[Random.Shared.Next(matches.Length)]);
    }

    private bool BeginInteraction(PairAction action, StickerInteraction? rule)
    {
        if (_disposed || !BothEnabled || Yier.IsUserBusy || Bubu.IsUserBusy) return false;
        if (rule?.IsLinked == true && (IsInteracting || Yier.IsPlayingGesture || Bubu.IsPlayingGesture)) return false;
        CancelInteraction();
        _interaction = rule;
        var area = SystemParameters.WorkArea;
        double separation = rule?.IsLinked == true ? 100 : action == PairAction.Greet ? 90 : 65;
        double halfWidth = Math.Max(Pair.Width / 2, separation + Yier.Width / 2);
        double center = Math.Clamp((Yier.Left + Bubu.Left + Yier.Width) / 2,
            area.Left + halfWidth, Math.Max(area.Left + halfWidth, area.Right - halfWidth));
        double top = Math.Clamp((Yier.Top + Bubu.Top) / 2, area.Top, Math.Max(area.Top, area.Bottom - Yier.Height));
        // Screen-space order is semantic data. Unconstrained scenes retain the
        // current order; fixed scenes walk into their reviewed positions first.
        bool bubuLeft = rule?.PositionRequirement switch
        {
            "bubu-left" => true,
            "yier-left" => false,
            "any" => Bubu.Left < Yier.Left,
            _ => rule is null ? Bubu.Left < Yier.Left : rule.LeftCharacter == "bubu"
        };
        if (rule?.Mode == "chase")
        {
            _chaseDirection = area.Right - center >= center - area.Left ? 1 : -1;
            bubuLeft = _chaseDirection < 0;
        }
        _interactionLeftCharacter = bubuLeft ? "bubu" : "yier";
        _yierTarget = new Point(center + (bubuLeft ? separation : -separation) - Yier.Width / 2, top);
        _bubuTarget = new Point(center + (bubuLeft ? -separation : separation) - Bubu.Width / 2, top);
        _action = action;
        _phaseSeconds = 0;
        Phase = InteractionPhase.Approaching;
        Yier.BeginCoordinated(); Bubu.BeginCoordinated();
        return true;
    }

    private void StartLinked()
    {
        var rule = _interaction!;
        var yierAsset = Library.Find(rule.AssetIds[0])!;
        var bubuAsset = Library.Find(rule.AssetIds[1])!;
        // Preserve at least one complete original cycle, including slower clips.
        _linkedDuration = Math.Max(rule.DurationSeconds, Math.Max(yierAsset.DurationSeconds, bubuAsset.DurationSeconds));
        string[] mirrored = rule.Mode != "chase" ? []
            : _chaseDirection > 0 ? rule.MirrorWhenMovingRight : rule.MirrorWhenMovingLeft;
        Yier.PlaySticker(yierAsset, (int)Math.Ceiling(_linkedDuration / yierAsset.DurationSeconds), mirrored.Contains(yierAsset.Id));
        Bubu.PlaySticker(bubuAsset, (int)Math.Ceiling(_linkedDuration / bubuAsset.DurationSeconds), mirrored.Contains(bubuAsset.Id));
        Phase = InteractionPhase.Linked;
        _phaseSeconds = 0;
    }

    public bool TryCatchThrownYier(MainWindow thrown, Point previousCenter, Point nextCenter)
    {
        if (_disposed || thrown != Yier || !Yier.CanBeCaught || !BothEnabled || IsInteracting
            || !Yier.IsVisible || !Bubu.IsVisible || Bubu.IsUserBusy) return false;
        var rule = Library.AvailableInteractions.FirstOrDefault(r => r.Id == "pair-0401"
            && r.Mode == "composite" && r.AssetIds.SequenceEqual(new[] { "wx-0401" }));
        if (rule is null) return false;
        // Catch around Bubu's upper body; hovering overhead or falling elsewhere does not count.
        var reach = new Rect(Bubu.Left + Bubu.Width / 2 - 100, Bubu.Top + Bubu.Height * .1,
            200, Bubu.Height * .7);
        if (!CrossesCatchArea(previousCenter, nextCenter, reach)) return false;

        Yier.EndCaughtThrow(); // Do not run the normal impact complaint on a successful catch.
        Yier.BeginCoordinated(); Bubu.BeginCoordinated();
        _interaction = rule;
        _interactionLeftCharacter = "bubu";
        _phaseSeconds = 0;
        Phase = InteractionPhase.Together;
        var catchAsset = Library.Find("wx-0401")!;
        _togetherDuration = Math.Max(2, Math.Ceiling(3 / catchAsset.DurationSeconds)) * catchAsset.DurationSeconds;
        var area = SystemParameters.WorkArea;
        Pair.Left = Math.Clamp(Bubu.Left + Bubu.Width / 2 - Pair.Width / 2,
            area.Left, Math.Max(area.Left, area.Right - Pair.Width));
        Pair.Top = Math.Clamp(Bubu.Top, area.Top, Math.Max(area.Top, area.Bottom - Pair.Height));
        double center = Pair.Left + Pair.Width / 2;
        // Restore the pair beside the catch, with Bubu on the left, when playback ends.
        Bubu.Left = Math.Clamp(center - 65 - Bubu.Width / 2, area.Left, Math.Max(area.Left, area.Right - Bubu.Width));
        Yier.Left = Math.Clamp(center + 65 - Yier.Width / 2, area.Left, Math.Max(area.Left, area.Right - Yier.Width));
        Yier.Top = Bubu.Top = Pair.Top;
        Pair.SetSticker(Library, catchAsset, 0, "bubu");
        Pair.Show(); Yier.Hide(); Bubu.Hide();
        return true;
    }

    private static bool CrossesCatchArea(Point from, Point to, Rect area)
    {
        double enter = 0, exit = 1;
        bool Clip(double start, double delta, double min, double max)
        {
            if (Math.Abs(delta) < 1e-9) return start >= min && start <= max;
            double a = (min - start) / delta, b = (max - start) / delta;
            enter = Math.Max(enter, Math.Min(a, b));
            exit = Math.Min(exit, Math.Max(a, b));
            return enter <= exit;
        }
        return Clip(from.X, to.X - from.X, area.Left, area.Right)
            && Clip(from.Y, to.Y - from.Y, area.Top, area.Bottom);
    }

    private void AdvanceLinked(double seconds)
    {
        if (_interaction is null || _phaseSeconds >= _linkedDuration) { CancelInteraction(); return; }
        if (_interaction.Mode != "chase") return;
        var area = SystemParameters.WorkArea;
        double delta = _chaseDirection * 95 * seconds;
        double left = Math.Min(Yier.Left, Bubu.Left), right = Math.Max(Yier.Left + Yier.Width, Bubu.Left + Bubu.Width);
        // End at the edge without teleporting or letting the chaser overtake.
        if (left + delta < area.Left || right + delta > area.Right) { CancelInteraction(); return; }
        Yier.Left += delta;
        Bubu.Left += delta;
    }

    private void PlayResponsePart()
    {
        var asset = Library.Find(_interaction!.AssetIds[_responseIndex])!;
        (asset.Role == "bubu" ? Bubu : Yier).PlaySticker(asset);
    }

    public void PlayLibraryAsset(StickerAsset asset)
    {
        if (!Library.IsEnabled(asset) || !asset.Reviewed) return;
        if (asset.Role is "pair" or "scene")
        {
            var rule = Library.AvailableInteractions.FirstOrDefault(r => r.Mode is "composite" or "scene" && r.AssetIds.Contains(asset.Id));
            if (rule is not null) RequestInteraction(rule);
        }
        else if (asset.Role is "yier" or "bubu")
        {
            var pet = asset.Role == "yier" ? Yier : Bubu;
            if (pet.IsUserBusy) return;
            if (!(asset.Role == "yier" ? Settings.YierVisible : Settings.BubuVisible)) return;
            CancelInteraction();
            if (!TryPlaySemantic(asset)) pet.PlaySticker(asset, 2);
        }
    }

    public void OpenLibrary()
    {
        CancelInteraction();
        if (_libraryWindow is null)
        {
            _libraryWindow = new StickerLibraryWindow(this);
            _libraryWindow.Closed += (_, _) => _libraryWindow = null;
            _libraryWindow.Show();
        }
        else _libraryWindow.Activate();
    }

    public void OpenVoice(PetCharacter character = PetCharacter.Yier)
    {
        if (_voiceWindow is null)
        {
            _voiceWindow = new VoiceWindow(character, stopReminder: _reminderVoice.Stop);
            _voiceWindow.Closed += (_, _) => _voiceWindow = null;
            _voiceWindow.Show();
        }
        else { if (_voiceWindow.WindowState == WindowState.Minimized) _voiceWindow.WindowState = WindowState.Normal; _voiceWindow.Activate(); }
    }

    public void StopReminderSpeech() => _reminderVoice.Stop();

    private void OnLibraryChanged()
    {
        if (IsInteracting) CancelInteraction();
        Yier.RefreshStickerAvailability(); Bubu.RefreshStickerAvailability();
    }

    public void CancelInteraction()
    {
        if (_disposed) return;
        bool wasInteracting = IsInteracting;
        Phase = InteractionPhase.Independent;
        _interaction = null;
        Pair.Hide();
        if (wasInteracting)
        {
            Yier.ResumeIndependent(); Bubu.ResumeIndependent();
            ApplyVisibility();
            _cooldown = 12;
            _autoSeconds = Random.Shared.Next(35, 66);
        }
    }

    public void OnReleased(MainWindow pet, bool moved)
    {
        if (!moved || !Settings.AutomaticInteractionsEnabled || !BothEnabled || _cooldown > 0
            || Yier.IsUserBusy || Bubu.IsUserBusy) return;
        double distance = Math.Abs(Yier.Left - Bubu.Left);
        if (distance < 180 && Math.Abs(Yier.Top - Bubu.Top) < 65) RequestPair(PairAction.Hug);
    }

    public void UserInterrupted()
    {
        CancelInteraction();
        _autoSeconds = Math.Max(20, _autoSeconds);
    }

    public void SettingsChanged()
    {
        CancelInteraction();
        Yier.ResumeIndependent(); Bubu.ResumeIndependent();
        ApplyVisibility();
        SettingsStore.Save(Settings);
    }

    private void ApplyVisibility()
    {
        if (Settings.YierVisible) Yier.Show(); else Yier.Hide();
        if (Settings.BubuVisible) Bubu.Show(); else Bubu.Hide();
    }

    public void Recall()
    {
        CancelInteraction();
        Yier.ResumeIndependent(); Bubu.ResumeIndependent();
        var area = SystemParameters.WorkArea;
        Bubu.Left = Math.Max(area.Left, area.Right - 390);
        Yier.Left = Math.Max(area.Left, area.Right - 210);
        Yier.Top = Bubu.Top = Math.Max(area.Top, area.Bottom - Yier.Height - 20);
        ApplyVisibility();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop(); _clock.Stop();
        Library.Changed -= OnLibraryChanged;
        _libraryWindow?.Close();
        _voiceWindow?.Close();
        _reminderVoice.Dispose();
        Pair.Close(); Yier.Close(); Bubu.Close();
    }
}
