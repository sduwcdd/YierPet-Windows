using System.Diagnostics;

namespace YierPet.Windows.Core;

public enum ReminderKind
{
    Sedentary,
    Water,
    LateNight,
    Slacking,

}

public sealed class ReminderService
{
    private static readonly HashSet<string> VideoProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "vlc", "potplayer", "qmplay2", "iina", "qqlive", "youku", "iqiyi", "bilibili", "tencentvideo"
    };

    private static readonly IReadOnlyDictionary<ReminderKind, string[]> Messages =
        new Dictionary<ReminderKind, string[]>
        {
            [ReminderKind.Sedentary] = ["坐了一个小时啦，起来动动嘛！", "屁股要生根啦～站起来伸个懒腰吧！", "陪我走两步？你都坐一小时了！", "久坐伤腰！起来倒杯水顺便活动一下～"],
            [ReminderKind.Water] = ["咕噜咕噜～该喝水啦！", "你已经半小时没喝水了哦，抿一口嘛", "多喝水才有精神呀，去接杯水吧！", "我都替你渴了……喝口水好不好？"],
            [ReminderKind.LateNight] = ["这么晚了还在忙呀，早点休息嘛……", "夜深了，工作再多也要爱惜自己哦", "我都困了 zzZ……你也快睡吧？", "熬夜会秃的！明天再做也来得及～"],
            [ReminderKind.Slacking] = ["都摸鱼半小时了哦……我可什么都没看见", "视频好看吗？要不要考虑干点活？", "嘘——老板来了！（骗你的，但也该收心啦）", "摸鱼一时爽，一直摸鱼一直慌哦～"],

        };

    private readonly AppSettings _settings;
    public static IEnumerable<(ReminderKind Kind, string Text)> Scripts =>
        Messages.SelectMany(pair => pair.Value.Select(text => (pair.Key, text)));
    private readonly Action<ReminderKind, string> _onReminder;
    private readonly TimeSpan _tickInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _presenceIdleLimit = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _sedentaryLimit = TimeSpan.FromHours(1);
    private readonly TimeSpan _waterInterval = TimeSpan.FromMinutes(30);
    private readonly TimeSpan _slackingLimit = TimeSpan.FromMinutes(30);
    private readonly TimeSpan _lateCareInterval = TimeSpan.FromMinutes(30);
    private readonly TimeSpan _globalGap = TimeSpan.FromSeconds(90);

    private TimeSpan _activeDuration;
    private TimeSpan _waterDuration;
    private DateTime _lastSlackReminder = DateTime.MinValue;
    private DateTime _lastLateReminder = DateTime.MinValue;
    private DateTime _lastReminder = DateTime.MinValue;
    private string? _foregroundProcess;
    private DateTime _foregroundSince = DateTime.Now;

    public ReminderService(AppSettings settings, Action<ReminderKind, string> onReminder)
    {
        _settings = settings;
        _onReminder = onReminder;
    }

    public IEnumerable<ReminderKind> HealthKinds =>
        [ReminderKind.Sedentary, ReminderKind.Water, ReminderKind.LateNight, ReminderKind.Slacking];

    public void Tick()
    {
        var now = DateTime.Now;
        UpdatePresenceAndForeground(now);
        TryHealthReminder(now);
    }

    public void TriggerTest(ReminderKind kind) => Notify(kind, bypassGap: true);

    private void UpdatePresenceAndForeground(DateTime now)
    {
        var idle = IdleDuration();
        if (idle < _presenceIdleLimit)
        {
            _activeDuration += _tickInterval;
            _waterDuration += _tickInterval;
        }
        else
        {
            _activeDuration = TimeSpan.Zero;
            _waterDuration = TimeSpan.Zero;
        }

        var process = ForegroundProcessName();
        if (!string.Equals(process, _foregroundProcess, StringComparison.OrdinalIgnoreCase))
        {
            _foregroundProcess = process;
            _foregroundSince = now;
        }
    }

    private void TryHealthReminder(DateTime now)
    {
        if (_activeDuration >= _sedentaryLimit && Notify(ReminderKind.Sedentary))
        {
            _activeDuration = TimeSpan.Zero;
            return;
        }

        if (IsLateNight() && now - _lastLateReminder >= _lateCareInterval && Notify(ReminderKind.LateNight))
        {
            _lastLateReminder = now;
            return;
        }

        if (_waterDuration >= _waterInterval && Notify(ReminderKind.Water))
        {
            _waterDuration = TimeSpan.Zero;
            return;
        }

        if (_foregroundProcess is { } process && VideoProcesses.Contains(process)
            && now - _foregroundSince >= _slackingLimit && now - _lastSlackReminder >= TimeSpan.FromMinutes(15)
            && Notify(ReminderKind.Slacking))
        {
            _lastSlackReminder = now;
        }
    }

    private bool Notify(ReminderKind kind, string? detail = null, bool bypassGap = false)
    {
        var now = DateTime.Now;
        if (!_settings.IsEnabled(kind) || (!bypassGap && now - _lastReminder < _globalGap)) return false;

        var template = Messages[kind][Random.Shared.Next(Messages[kind].Length)];
        var message = detail is null ? template : string.Format(template, detail);
        _lastReminder = now;
        _onReminder(kind, message);
        return true;
    }

    private static bool IsLateNight() => DateTime.Now.Hour is >= 23 or < 5;

    private static TimeSpan IdleDuration()
    {
        var lastInput = new NativeMethods.LastInputInfo
        {
            CbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.LastInputInfo>()
        };
        if (!NativeMethods.GetLastInputInfo(ref lastInput)) return TimeSpan.Zero;
        var elapsed = unchecked((uint)Environment.TickCount - lastInput.DwTime);
        return TimeSpan.FromMilliseconds(elapsed);
    }

    private static string? ForegroundProcessName()
    {
        try
        {
            var handle = NativeMethods.GetForegroundWindow();
            if (handle == IntPtr.Zero) return null;
            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            return processId == 0 ? null : Process.GetProcessById((int)processId).ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
