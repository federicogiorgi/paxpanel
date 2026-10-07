using System.Text.Json;

namespace PaxPanel.Sensors;

public sealed record CpuData(double? TempC, double? LoadPct, double? ClockMHz, double? VoltV, double? PowerW);

public sealed record GpuData(double? TempC, double? HotspotC, double? LoadPct, double? ClockMHz, double? PowerW,
    double? VramUsedMB, double? VramTotalMB);

public sealed record RamData(double? UsedMB, double? TotalMB, double? SpeedMTs);

public sealed record FanData(string Label, double? Rpm);

public sealed record DriveData(string Letter, string Label, bool Mounted, bool Removable, bool Extra,
    double? UsedGB, double? TotalGB, double? TempC);

public sealed record NetData(double? UpBps, double? DownBps, double? LinkMbps);

public sealed record UiData(BrandConfig Cpu, BrandConfig Gpu, MemoryConfig Memory,
    IReadOnlyList<string> StorageLogos, string? NetLogo);

public sealed record Snapshot(string Time, UiData Ui, CpuData Cpu, GpuData Gpu, RamData Ram,
    IReadOnlyList<FanData> Fans, IReadOnlyList<DriveData> Drives, int MoreDrives, NetData Net,
    IReadOnlyList<string> Warnings)
{
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
