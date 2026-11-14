using System.Globalization;

namespace PaxPanel.Sensors;

/// <summary>Appends the readings that matter for tuning the panel (temperatures, loads, power, fans) to
/// sensors.csv, one line per tick. Off unless config.json has "recordSensors": true. Starts a new file
/// (keeping one old one) past 20 MB.</summary>
public sealed class SensorRecorder(string dir)
{
    const long MaxBytes = 20 * 1024 * 1024;
    public string FilePath { get; } = Path.Combine(dir, "sensors.csv");

    public void Append(Snapshot s)
    {
        Directory.CreateDirectory(dir);
        var file = new FileInfo(FilePath);
        if (file.Exists && file.Length > MaxBytes)
            File.Move(FilePath, Path.Combine(dir, "sensors.old.csv"), overwrite: true);
        var lines = new List<string>(2);
        if (!File.Exists(FilePath))
            lines.Add(string.Join(",", ["time", "cpuTempC", "cpuLoadPct", "cpuPowerW", "hottestCoreC", "gpuTempC",
                "gpuHotspotC", "gpuLoadPct", "gpuPowerW", .. s.Fans.SelectMany(f => new[] { f.Label + "_rpm", f.Label + "_duty" })]));
        var hottest = s.Cpu.Cores?.Where(c => c.TempC is not null).Max(c => c.TempC);
        lines.Add(string.Join(",", [s.Time, N(s.Cpu.TempC), N(s.Cpu.LoadPct), N(s.Cpu.PowerW), N(hottest), N(s.Gpu.TempC),
            N(s.Gpu.HotspotC), N(s.Gpu.LoadPct), N(s.Gpu.PowerW), .. s.Fans.SelectMany(f => new[] { N(f.Rpm, 0), N(f.DutyPct) })]));
        File.AppendAllLines(FilePath, lines);
    }

    static string N(double? v, int digits = 1) =>
        v is { } d ? Math.Round(d, digits).ToString(CultureInfo.InvariantCulture) : "";
}
