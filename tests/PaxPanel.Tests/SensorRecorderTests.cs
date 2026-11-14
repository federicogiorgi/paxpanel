using PaxPanel.Sensors;

namespace PaxPanel.Tests;

public class SensorRecorderTests
{
    static Snapshot Snap(double? cpuT) => new(
        "2026-11-14T19:00:05",
        new UiData(new BrandConfig(), new BrandConfig(), new MemoryConfig(), [], null),
        new CpuData(cpuT, 100, 5600, 1.3, 253.5, Cores: [new("P1", true, 100, 97), new("E1", false, 100, 88)]),
        new GpuData(64, 75.2, 99, 2700, 431, 1000, 24000),
        new RamData(1, 2, 3),
        [new FanData("CPU", 2201.4, 2235), new FanData("GPU", 1713, 1713, 49)],
        [], 0, new NetData(null, null, null), []);

    [Fact]
    public void Header_then_one_line_per_snapshot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "paxpanel-rec-" + Guid.NewGuid());
        var rec = new SensorRecorder(dir);
        rec.Append(Snap(98.4));
        rec.Append(Snap(null));
        var lines = File.ReadAllLines(Path.Combine(dir, "sensors.csv"));
        Assert.Equal("time,cpuTempC,cpuLoadPct,cpuPowerW,hottestCoreC,gpuTempC,gpuHotspotC,gpuLoadPct,gpuPowerW,CPU_rpm,CPU_duty,GPU_rpm,GPU_duty", lines[0]);
        Assert.Equal("2026-11-14T19:00:05,98.4,100,253.5,97,64,75.2,99,431,2201,,1713,49", lines[1]);
        Assert.StartsWith("2026-11-14T19:00:05,,100,", lines[2]);
        Assert.Equal(3, lines.Length);
        Directory.Delete(dir, true);
    }
}
