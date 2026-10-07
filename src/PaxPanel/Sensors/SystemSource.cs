using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace PaxPanel.Sensors;

/// <summary>Values Windows provides without a driver: RAM, volumes, network counters.</summary>
public sealed class SystemSource
{
    readonly RateMeter _up = new();
    readonly RateMeter _down = new();
    readonly double? _ramSpeed = ReadRamSpeed();
    readonly ProcessCpuTracker _processes = new();
    readonly GpuProcessTracker _gpuProcesses = new();
    string? _nicId;

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    public RamData ReadRam()
    {
        var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref m)) return new RamData(null, null, _ramSpeed);
        const double mb = 1024 * 1024;
        return new RamData((m.TotalPhys - m.AvailPhys) / mb, m.TotalPhys / mb, _ramSpeed);
    }

    static double? ReadRamSpeed()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ConfiguredClockSpeed FROM Win32_PhysicalMemory");
            foreach (ManagementBaseObject o in searcher.Get())
                using (o)
                    if (o["ConfiguredClockSpeed"] is { } v && Convert.ToDouble(v) > 0) return Convert.ToDouble(v);
        }
        catch (Exception e)
        {
            Log.Once("ramspeed", $"RAM speed unavailable: {e.Message}");
        }
        return null;
    }

    public List<VolumeInfo> ReadVolumes()
    {
        const double gb = 1024d * 1024 * 1024;
        var list = new List<VolumeInfo>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType is not (DriveType.Fixed or DriveType.Removable) || !d.IsReady) continue;
                list.Add(new VolumeInfo(d.Name[0], d.VolumeLabel, d.DriveType == DriveType.Removable,
                    d.TotalSize / gb, d.TotalFreeSpace / gb));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A drive vanished between enumeration and query; skip it this tick.
            }
        }
        return list;
    }

    /// <summary>Uptime, the three processes using the most CPU and the one using the most GPU since the previous call.</summary>
    public SysData ReadSys(DateTime now)
    {
        var processes = new List<(int, string, TimeSpan)>();
        var names = new Dictionary<int, string>();
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            using (p)
            {
                names[p.Id] = p.ProcessName;
                try
                {
                    processes.Add((p.Id, p.ProcessName, p.TotalProcessorTime));
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Protected or already exited; skip it.
                }
            }
        }
        var topCpu = _processes.Update(processes, now, Environment.ProcessorCount, 3);
        var topGpu = SnapshotBuilder.Safe("gpu processes", () => _gpuProcesses.Update(ReadGpuEngines(), names, 1), []);
        return new SysData(Environment.TickCount64 / 1000.0, topCpu, topGpu);
    }

    /// <summary>One raw sample per GPU engine instance from the Windows "GPU Engine" performance counters.</summary>
    static List<(string, long, long)> ReadGpuEngines()
    {
        var data = new System.Diagnostics.PerformanceCounterCategory("GPU Engine").ReadCategory();
        var utilization = data["Utilization Percentage"];
        var list = new List<(string, long, long)>();
        if (utilization is null) return list;
        foreach (System.Diagnostics.InstanceData d in utilization.Values)
            list.Add((d.InstanceName, d.RawValue, d.Sample.TimeStamp100nSec));
        return list;
    }

    public NetData ReadNet(string? adapterMatch, DateTime now)
    {
        var all = NetworkInterface.GetAllNetworkInterfaces();
        var infos = all.Select(n => new NicInfo(n.Id, n.Description, n.OperationalStatus == OperationalStatus.Up,
            n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel
            || NicPicker.LooksVirtual(n.Description)));
        var pick = NicPicker.Pick(infos, adapterMatch);
        if (pick is null) return new NetData(null, null, null);
        if (pick.Id != _nicId)
        {
            _nicId = pick.Id;
            _up.Reset();
            _down.Reset();
        }
        var nic = all.First(n => n.Id == pick.Id);
        var stats = nic.GetIPStatistics();
        return new NetData(_up.Update(stats.BytesSent, now), _down.Update(stats.BytesReceived, now), nic.Speed / 1_000_000d);
    }
}
