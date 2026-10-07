using PaxPanel;
using PaxPanel.Sensors;

namespace PaxPanel.Tests;

public class HardwareAssemblerTests
{
    static SensorReading R(string hw, string type, string name, double? v, string id = "/x/0", string hwName = "HW") =>
        new(hw, id, hwName, type, name, v);

    [Fact]
    public void Clean_rejects_nan_and_infinity()
    {
        Assert.Null(HardwareAssembler.Clean(float.NaN));
        Assert.Null(HardwareAssembler.Clean(float.PositiveInfinity));
        Assert.Null(HardwareAssembler.Clean(null));
        Assert.Equal(1.5, HardwareAssembler.Clean(1.5f));
    }

    [Fact]
    public void Cpu_picks_named_sensors_and_averages_core_clocks()
    {
        var rs = new List<SensorReading>
        {
            R("Cpu", "Temperature", "Core #1", 50),
            R("Cpu", "Temperature", "CPU Package", 42),
            R("Cpu", "Load", "CPU Total", 4),
            R("Cpu", "Load", "CPU Core #1", 9),
            R("Cpu", "Clock", "Bus Speed", 100),
            R("Cpu", "Clock", "P-Core #1", 3000),
            R("Cpu", "Clock", "E-Core #1", 2000),
            R("Cpu", "Voltage", "CPU Core", 1.116),
            R("Cpu", "Power", "CPU Package", 38.4),
        };
        var c = HardwareAssembler.Cpu(rs);
        Assert.Equal(42, c.TempC);
        Assert.Equal(4, c.LoadPct);
        Assert.Equal(2500, c.ClockMHz);
        Assert.Equal(1.116, c.VoltV);
        Assert.Equal(38.4, c.PowerW);
    }

    [Fact]
    public void Cpu_volt_falls_back_to_superio_vcore_and_temp_to_first_cpu_temperature()
    {
        var rs = new List<SensorReading>
        {
            R("Cpu", "Temperature", "Core Max", 61),
            R("SuperIO", "Voltage", "Vcore", 1.2),
        };
        var c = HardwareAssembler.Cpu(rs);
        Assert.Equal(61, c.TempC);
        Assert.Equal(1.2, c.VoltV);
        Assert.Null(c.PowerW);
    }

    [Fact]
    public void Gpu_reads_nvidia_sensors()
    {
        var rs = new List<SensorReading>
        {
            R("GpuNvidia", "Temperature", "GPU Core", 36),
            R("GpuNvidia", "Temperature", "GPU Hot Spot", 45),
            R("GpuNvidia", "Load", "GPU Core", 3),
            R("GpuNvidia", "Clock", "GPU Core", 210),
            R("GpuNvidia", "Power", "GPU Package", 46.3),
            R("GpuNvidia", "SmallData", "GPU Memory Used", 3300),
            R("GpuNvidia", "SmallData", "GPU Memory Total", 24564),
            R("GpuIntel", "Temperature", "GPU Core", 99), // integrated GPU must be ignored
        };
        var g = HardwareAssembler.Gpu(rs);
        Assert.Equal(new GpuData(36, 45, 3, 210, 46.3, 3300, 24564), g);
    }

    [Fact]
    public void Fans_follow_config_order_exact_match_first()
    {
        var rs = new List<SensorReading>
        {
            R("SuperIO", "Fan", "Fan #10", 500),
            R("SuperIO", "Fan", "Fan #1", 1259),
            R("GpuNvidia", "Fan", "GPU Fan 1", 1000),
        };
        var cfg = new List<FanConfig>
        {
            new() { Label = "CPU", Match = "Fan #1" },
            new() { Label = "GPU", Match = "GPU Fan" },
            new() { Label = "SYS", Match = "Fan #7" },
        };
        var fans = HardwareAssembler.Fans(rs, cfg);
        Assert.Equal(new[] { new FanData("CPU", 1259), new FanData("GPU", 1000), new FanData("SYS", null) }, fans);
    }

    [Theory]
    [InlineData("/nvme/3", 3)]
    [InlineData("/hdd/0", 0)]
    [InlineData("/ssd/12", 12)]
    [InlineData("/scsi/1/2", null)]
    [InlineData("/nvme/x", null)]
    public void ParseDiskNumber(string id, int? expected) =>
        Assert.Equal(expected, HardwareAssembler.ParseDiskNumber(id));

    [Fact]
    public void Storage_temps_prefer_sensor_named_temperature()
    {
        var rs = new List<SensorReading>
        {
            R("Storage", "Temperature", "Warning Temperature", 80, "/nvme/5"),
            R("Storage", "Temperature", "Temperature", 40, "/nvme/5"),
            R("Storage", "Temperature", "Temperature #2", 55, "/nvme/5"),
            R("Storage", "Temperature", "Temperature #1", 43, "/nvme/3"),
            R("Storage", "Temperature", "Temperature", null, "/hdd/0"),
        };
        var t = HardwareAssembler.StorageTempsByDisk(rs);
        Assert.Equal(40, t[5]);
        Assert.Equal(43, t[3]);
        Assert.False(t.ContainsKey(0));
    }
}
