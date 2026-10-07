using System.Text.Json;

namespace PaxPanel.Sensors;

public sealed record CoreData(string Name, bool Performance, double? LoadPct, double? TempC);

public sealed record CpuData(double? TempC, double? LoadPct, double? ClockMHz, double? VoltV, double? PowerW,
    double? PClockMHz = null, double? EClockMHz = null, IReadOnlyList<CoreData>? Cores = null);

public sealed record GpuData(double? TempC, double? HotspotC, double? LoadPct, double? ClockMHz, double? PowerW,
    double? VramUsedMB, double? VramTotalMB,
    double? MemJunctionC = null, double? PowerPct = null, double? PcieRxBps = null, double? PcieTxBps = null);

public sealed record RamData(double? UsedMB, double? TotalMB, double? SpeedMTs);

/// <summary>A fan reading. MaxRpm is filled in by FanMaxTracker; DutyPct is the controller's duty when reported.</summary>
public sealed record FanData(string Label, double? Rpm, double? MaxRpm = null, double? DutyPct = null);

public sealed record DriveData(string Letter, string Label, bool Mounted, bool Removable, bool Extra,
    double? UsedGB, double? TotalGB, double? TempC,
    double? ReadBps = null, double? WriteBps = null, double? LifePct = null);

/// <summary>Per physical disk values from LHM: temperature, live throughput and remaining life (SSDs).</summary>
public sealed record DiskStats(double? TempC, double? ReadBps, double? WriteBps, double? LifePct);

public sealed record NetData(double? UpBps, double? DownBps, double? LinkMbps);

public sealed record BoardTemp(string Label, double? TempC);

public sealed record ProcessLoad(string Name, double CpuPct);

/// <summary>Top is by CPU (% of all logical CPUs), TopGpu by GPU (busiest engine, like Task Manager).</summary>
public sealed record SysData(double UptimeSec, IReadOnlyList<ProcessLoad> Top, IReadOnlyList<ProcessLoad>? TopGpu = null);

public sealed record UiData(BrandConfig Cpu, BrandConfig Gpu, MemoryConfig Memory,
    IReadOnlyList<string> StorageLogos, string? NetLogo);

public sealed record Snapshot(string Time, UiData Ui, CpuData Cpu, GpuData Gpu, RamData Ram,
    IReadOnlyList<FanData> Fans, IReadOnlyList<DriveData> Drives, int MoreDrives, NetData Net,
    IReadOnlyList<string> Warnings, IReadOnlyList<BoardTemp>? Board = null, SysData? Sys = null)
{
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
