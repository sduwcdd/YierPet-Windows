using System.IO;
namespace YierPet.Windows.Core;

public sealed class SystemMonitor
{
    private CpuTicks? _previousCpuTicks;

    public double? CpuUsage()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)) return null;

        var current = new CpuTicks(idle.Value, kernel.Value, user.Value);
        if (_previousCpuTicks is not { } previous)
        {
            _previousCpuTicks = current;
            return null;
        }

        _previousCpuTicks = current;
        var total = (current.Kernel - previous.Kernel) + (current.User - previous.User);
        var idleDelta = current.Idle - previous.Idle;
        if (total == 0 || idleDelta > total) return null;
        return Math.Clamp((double)(total - idleDelta) / total, 0, 1);
    }

    public bool IsMemoryPressure()
    {
        var status = new NativeMethods.MemoryStatusEx
        {
            Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MemoryStatusEx>()
        };
        return NativeMethods.GlobalMemoryStatusEx(ref status) && status.MemoryLoad >= 90;
    }

    public BatteryState? BatteryState()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var status) || status.BatteryLifePercent == byte.MaxValue)
        {
            return null;
        }

        var onAcPower = status.AcLineStatus == 1;
        var charging = onAcPower && (status.BatteryFlag & 0x08) != 0;
        return new BatteryState(status.BatteryLifePercent, charging, onAcPower);
    }

    public DiskState? SystemDiskState()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrWhiteSpace(root)) return null;
            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize == 0) return null;
            return new DiskState((double)drive.AvailableFreeSpace / drive.TotalSize,
                drive.AvailableFreeSpace / 1024d / 1024d / 1024d);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private readonly record struct CpuTicks(ulong Idle, ulong Kernel, ulong User);
}

public readonly record struct BatteryState(byte Percent, bool IsCharging, bool OnAcPower);
public readonly record struct DiskState(double FreeRatio, double FreeGb);
