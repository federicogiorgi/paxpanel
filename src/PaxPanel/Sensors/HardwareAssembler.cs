namespace PaxPanel.Sensors;

/// <summary>Turns a flat list of LHM readings into panel values. Pure: no hardware access.</summary>
public static class HardwareAssembler
{
    public static double? Clean(float? v) => v is float f && float.IsFinite(f) ? f : null;

    static bool IsCpu(string hardwareType) => hardwareType == "Cpu";
    static bool IsNvidia(string hardwareType) => hardwareType == "GpuNvidia";

    /// <summary>First non-null value whose name equals one of <paramref name="names"/>, tried in order.</summary>
    static double? Find(IReadOnlyList<SensorReading> rs, Func<string, bool> hardware, string sensorType,
        bool fallbackToFirst, params string[] names)
    {
        var candidates = rs.Where(r => hardware(r.HardwareType) && r.SensorType == sensorType && r.Value is not null).ToList();
        foreach (var name in names)
        {
            var hit = candidates.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit.Value;
        }
        return fallbackToFirst ? candidates.FirstOrDefault()?.Value : null;
    }

    public static CpuData Cpu(IReadOnlyList<SensorReading> rs)
    {
        var coreClocks = rs
            .Where(r => IsCpu(r.HardwareType) && r.SensorType == "Clock" && r.Value is > 0
                        && r.Name.Contains("Core #", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Value!.Value)
            .ToList();
        return new CpuData(
            TempC: Find(rs, IsCpu, "Temperature", true, "CPU Package", "Core Max", "Core Average"),
            LoadPct: Find(rs, IsCpu, "Load", false, "CPU Total"),
            ClockMHz: coreClocks.Count > 0 ? coreClocks.Average() : null,
            VoltV: Find(rs, IsCpu, "Voltage", false, "CPU Core", "Core (SVID)")
                   ?? Find(rs, t => t == "SuperIO", "Voltage", false, "Vcore", "CPU Core"),
            PowerW: Find(rs, IsCpu, "Power", false, "CPU Package"));
    }

    public static GpuData Gpu(IReadOnlyList<SensorReading> rs) => new(
        TempC: Find(rs, IsNvidia, "Temperature", true, "GPU Core"),
        HotspotC: Find(rs, IsNvidia, "Temperature", false, "GPU Hot Spot"),
        LoadPct: Find(rs, IsNvidia, "Load", false, "GPU Core"),
        ClockMHz: Find(rs, IsNvidia, "Clock", false, "GPU Core"),
        PowerW: Find(rs, IsNvidia, "Power", false, "GPU Package", "GPU Power"),
        VramUsedMB: Find(rs, IsNvidia, "SmallData", false, "GPU Memory Used"),
        VramTotalMB: Find(rs, IsNvidia, "SmallData", false, "GPU Memory Total"));

    public static List<FanData> Fans(IReadOnlyList<SensorReading> rs, IReadOnlyList<FanConfig> config)
    {
        var fans = rs.Where(r => r.SensorType == "Fan" && r.Value is not null).ToList();
        return config.Select(f =>
        {
            var hit = fans.FirstOrDefault(r => string.Equals(r.Name, f.Match, StringComparison.OrdinalIgnoreCase))
                      ?? fans.FirstOrDefault(r => r.Name.Contains(f.Match, StringComparison.OrdinalIgnoreCase));
            return new FanData(f.Label, hit?.Value);
        }).ToList();
    }

    /// <summary>LHM storage identifiers look like "/nvme/3" or "/hdd/0", where the number is the
    /// Windows physical disk number. Returns null for other shapes (e.g. SCSI paths).</summary>
    public static int? ParseDiskNumber(string hardwareId)
    {
        var parts = hardwareId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && int.TryParse(parts[1], out var n) ? n : null;
    }

    public static Dictionary<int, double> StorageTempsByDisk(IReadOnlyList<SensorReading> rs)
    {
        var result = new Dictionary<int, double>();
        foreach (var disk in rs.Where(r => r.HardwareType == "Storage" && r.SensorType == "Temperature" && r.Value is not null)
                               .GroupBy(r => r.HardwareId))
        {
            if (ParseDiskNumber(disk.Key) is not int number) continue;
            var pick = disk.FirstOrDefault(r => r.Name == "Temperature")
                       ?? disk.FirstOrDefault(r => r.Name.StartsWith("Temperature #", StringComparison.Ordinal));
            if (pick?.Value is double t) result[number] = t;
        }
        return result;
    }
}
