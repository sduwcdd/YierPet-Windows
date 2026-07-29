using System.Diagnostics;

namespace YierPet.Windows.Core;

public enum ReminderKind
{
    Sedentary,
    Water,
    LateNight,
    Slacking,
    CpuHigh,
    MemoryPressure,
    BatteryLow,
    BatteryFull,
    DiskFull
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
            [ReminderKind.CpuHigh] = ["好烫好烫！CPU 都 {0} 了，是谁在偷偷挖矿！", "呼——风扇都要起飞啦，CPU 飙到 {0}！", "红温警告！我要被烤熟了……快看看哪个 App 在发疯！"],
            [ReminderKind.MemoryPressure] = ["内存要被挤爆啦，关几个 App 让我喘口气～", "好挤呀……内存不够用了，清理一下嘛", "App 开太多啦，我都快被挤出屏幕了！"],
            [ReminderKind.BatteryLow] = ["电量只剩 {0} 啦，快给我充电！", "肚子饿了……电池只有 {0} 了，插电插电！", "再不充电我们就要一起睡着了哦（{0}）"],
            [ReminderKind.BatteryFull] = ["吃饱啦～电池充满了，可以拔线啦！", "满电出发！记得拔掉充电线哦～"],
            [ReminderKind.DiskFull] = ["磁盘只剩 {0} 空位啦，该大扫除了！", "装不下啦……磁盘剩 {0}，删点东西嘛"]
        };

    private readonly AppSettings _settings;
    private readonly SystemMonitor _systemMonitor = new();
    private readonly Action<ReminderKind, string> _onReminder;
    private readonly Action<double> _onHeatChanged;
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
    private DateTime _lastCpuReminder = DateTime.MinValue;
    private DateTime _lastMemoryReminder = DateTime.MinValue;
    private DateTime _lastDiskReminder = DateTime.MinValue;
    private string? _foregroundProcess;
    private DateTime _foregroundSince = DateTime.Now;
    private int _cpuHighTicks;
    private int _batteryStage;
    private bool _batteryFullReminded;

    public ReminderService(AppSettings settings, Action<ReminderKind, string> onReminder, Action<double> onHeatChanged)
    {
        _settings = settings;
        _onReminder = onReminder;
        _onHeatChanged = onHeatChanged;
        _ = _systemMonitor.CpuUsage(); // Establish a baseline before the first 30-second tick.
    }

    public IEnumerable<ReminderKind> HealthKinds =>
        [ReminderKind.Sedentary, ReminderKind.Water, ReminderKind.LateNight, ReminderKind.Slacking];

    public IEnumerable<ReminderKind> SystemKinds =>
        [ReminderKind.CpuHigh, ReminderKind.MemoryPressure, ReminderKind.BatteryLow, ReminderKind.BatteryFull, ReminderKind.DiskFull];

    public void Tick()
    {
        var now = DateTime.Now;
        UpdatePresenceAndForeground(now);
        if (TrySystemReminder(now)) return;
        TryHealthReminder(now);
    }

    public void TriggerTest(ReminderKind kind)
    {
        var detail = kind switch
        {
            ReminderKind.CpuHigh => "97%",
            ReminderKind.BatteryLow => "18%",
            ReminderKind.DiskFull => "9GB",
            _ => null
        };
        Notify(kind, detail, bypassGap: true);
    }

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

    private bool TrySystemReminder(DateTime now)
    {
        var cpu = _systemMonitor.CpuUsage();
        var heat = cpu is { } usage ? Math.Clamp((usage - 0.60) / 0.40, 0, 1) : 0;
        _onHeatChanged(heat);

        if (cpu >= 0.85) _cpuHighTicks++; else _cpuHighTicks = 0;
        if (_cpuHighTicks >= 3 && now - _lastCpuReminder >= TimeSpan.FromMinutes(10)
            && Notify(ReminderKind.CpuHigh, $"{cpu!.Value:P0}"))
        {
            _lastCpuReminder = now;
            _cpuHighTicks = 0;
            return true;
        }

        if (_systemMonitor.IsMemoryPressure() && now - _lastMemoryReminder >= TimeSpan.FromMinutes(10)
            && Notify(ReminderKind.MemoryPressure))
        {
            _lastMemoryReminder = now;
            return true;
        }

        if (_systemMonitor.BatteryState() is { } battery)
        {
            if (battery.IsCharging || battery.OnAcPower)
            {
                _batteryStage = 0;
                if (battery.Percent >= 98 && !_batteryFullReminded && Notify(ReminderKind.BatteryFull))
                {
                    _batteryFullReminded = true;
                    return true;
                }
            }
            else
            {
                _batteryFullReminded = false;
                var stage = battery.Percent <= 10 ? 2 : battery.Percent <= 20 ? 1 : 0;
                if (stage > _batteryStage && Notify(ReminderKind.BatteryLow, $"{battery.Percent}%"))
                {
                    _batteryStage = stage;
                    return true;
                }
            }
        }

        if (_systemMonitor.SystemDiskState() is { } disk && disk.FreeRatio < 0.10
            && now - _lastDiskReminder >= TimeSpan.FromHours(6)
            && Notify(ReminderKind.DiskFull, $"{disk.FreeGb:F0}GB"))
        {
            _lastDiskReminder = now;
            return true;
        }

        return false;
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
