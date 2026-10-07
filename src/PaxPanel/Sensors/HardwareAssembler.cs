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

    static double? Average(IEnumerable<SensorReading> rs)
    {
        var values = rs.Where(r => r.Value is not null).Select(r => r.Value!.Value).ToList();
        return values.Count > 0 ? values.Average() : null;
    }

    static readonly System.Text.RegularExpressions.Regex CoreLoadName =
        new(@"^CPU Core #(\d+)( Thread #\d+)?$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static CpuData Cpu(IReadOnlyList<SensorReading> rs)
    {
        var cpu = rs.Where(r => IsCpu(r.HardwareType)).ToList();
        var clocks = cpu.Where(r => r.SensorType == "Clock" && r.Value is > 0).ToList();
        return new CpuData(
            TempC: Find(rs, IsCpu, "Temperature", true, "CPU Package", "Core Max", "Core Average"),
            LoadPct: Find(rs, IsCpu, "Load", false, "CPU Total"),
            ClockMHz: Average(clocks.Where(r => r.Name.Contains("Core #", StringComparison.OrdinalIgnoreCase))),
            VoltV: Find(rs, IsCpu, "Voltage", false, "CPU Core", "Core (SVID)")
                   ?? Find(rs, t => t == "SuperIO", "Voltage", false, "Vcore", "CPU Core"),
            PowerW: Find(rs, IsCpu, "Power", false, "CPU Package"),
            PClockMHz: Average(clocks.Where(r => r.Name.StartsWith("P-Core #", StringComparison.Ordinal))),
            EClockMHz: Average(clocks.Where(r => r.Name.StartsWith("E-Core #", StringComparison.Ordinal))),
            Cores: Cores(cpu));
    }

    /// <summary>One entry per physical core: thread loads averaged, P-cores first (named P1..), then E-cores (E1..).
    /// LHM numbers cores "CPU Core #n" with P-cores first; per-core temperatures are "P-Core #k" / "E-Core #k".</summary>
    static List<CoreData> Cores(List<SensorReading> cpu)
    {
        var loads = cpu
            .Where(r => r.SensorType == "Load")
            .Select(r => (r, m: CoreLoadName.Match(r.Name)))
            .Where(x => x.m.Success)
            .GroupBy(x => int.Parse(x.m.Groups[1].Value))
            .OrderBy(g => g.Key)
            .Select(g => (Number: g.Key, Load: Average(g.Select(x => x.r)), Threads: g.Count()))
            .ToList();
        var temps = cpu.Where(r => r.SensorType == "Temperature" && !r.Name.Contains("Distance")).ToList();
        double? Temp(string name) => temps.FirstOrDefault(r => r.Name == name)?.Value;

        var pCount = temps.Count(r => r.Name.StartsWith("P-Core #", StringComparison.Ordinal));
        var hybrid = pCount > 0;
        return loads.Select((c, i) =>
        {
            if (!hybrid) return new CoreData($"C{c.Number}", true, c.Load, Temp($"Core #{c.Number}"));
            var isP = i < pCount;
            var n = isP ? i + 1 : i - pCount + 1;
            var prefix = isP ? "P" : "E";
            return new CoreData($"{prefix}{n}", isP, c.Load, Temp($"{prefix}-Core #{n}"));
        }).ToList();
    }

    public static GpuData Gpu(IReadOnlyList<SensorReading> rs) => new(
        TempC: Find(rs, IsNvidia, "Temperature", true, "GPU Core"),
        HotspotC: Find(rs, IsNvidia, "Temperature", false, "GPU Hot Spot"),
        LoadPct: Find(rs, IsNvidia, "Load", false, "GPU Core"),
        ClockMHz: Find(rs, IsNvidia, "Clock", false, "GPU Core"),
        PowerW: Find(rs, IsNvidia, "Power", false, "GPU Package", "GPU Power"),
        VramUsedMB: Find(rs, IsNvidia, "SmallData", false, "GPU Memory Used"),
        VramTotalMB: Find(rs, IsNvidia, "SmallData", false, "GPU Memory Total"),
        MemJunctionC: Find(rs, IsNvidia, "Temperature", false, "GPU Memory Junction"),
        PowerPct: Find(rs, IsNvidia, "Load", false, "GPU Power"),
        PcieRxBps: Find(rs, IsNvidia, "Throughput", false, "GPU PCIe Rx"),
        PcieTxBps: Find(rs, IsNvidia, "Throughput", false, "GPU PCIe Tx"));

    public static List<FanData> Fans(IReadOnlyList<SensorReading> rs, IReadOnlyList<FanConfig> config)
    {
        var fans = rs.Where(r => r.SensorType == "Fan" && r.Value is not null).ToList();
        return config.Select(f =>
        {
            var hit = fans.FirstOrDefault(r => string.Equals(r.Name, f.Match, StringComparison.OrdinalIgnoreCase))
                      ?? fans.FirstOrDefault(r => r.Name.Contains(f.Match, StringComparison.OrdinalIgnoreCase));
            var duty = hit is null ? null
                : rs.FirstOrDefault(r => r.SensorType == "Control" && r.HardwareId == hit.HardwareId && r.Name == hit.Name)?.Value;
            return new FanData(f.Label, hit?.Value, null, duty);
        }).ToList();
    }

    public static List<BoardTemp> Board(IReadOnlyList<SensorReading> rs, IReadOnlyList<BoardTempConfig> config) =>
        config.Select(b => new BoardTemp(b.Label,
            rs.FirstOrDefault(r => r.HardwareType == "SuperIO" && r.SensorType == "Temperature" && r.Name == b.Match)?.Value)).ToList();

    public static Dictionary<int, DiskStats> StorageByDisk(IReadOnlyList<SensorReading> rs)
    {
        var result = new Dictionary<int, DiskStats>();
        foreach (var disk in rs.Where(r => r.HardwareType == "Storage").GroupBy(r => r.HardwareId))
        {
            if (ParseDiskNumber(disk.Key) is not int number) continue;
            double? Get(string type, string name) =>
                disk.FirstOrDefault(r => r.SensorType == type && r.Name == name && r.Value is not null)?.Value;
            var temps = disk.Where(r => r.SensorType == "Temperature" && r.Value is not null).ToList();
            var temp = (temps.FirstOrDefault(r => r.Name == "Temperature")
                        ?? temps.FirstOrDefault(r => r.Name == "Composite Temperature")
                        ?? temps.FirstOrDefault(r => r.Name.StartsWith("Temperature #", StringComparison.Ordinal)))?.Value;
            var life = Get("Level", "Life") ?? (Get("Level", "Percentage Used") is double used ? 100 - used : null);
            var stats = new DiskStats(temp, Get("Throughput", "Read Rate"), Get("Throughput", "Write Rate"), life);
            if (stats != new DiskStats(null, null, null, null)) result[number] = stats;
        }
        return result;
    }

    /// <summary>LHM storage identifiers look like "/nvme/3" or "/hdd/0", where the number is the
    /// Windows physical disk number. Returns null for other shapes (e.g. SCSI paths).</summary>
    public static int? ParseDiskNumber(string hardwareId)
    {
        var parts = hardwareId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && int.TryParse(parts[1], out var n) ? n : null;
    }

    public static Dictionary<int, double> StorageTempsByDisk(IReadOnlyList<SensorReading> rs) =>
        StorageByDisk(rs).Where(p => p.Value.TempC is not null).ToDictionary(p => p.Key, p => p.Value.TempC!.Value);
}
