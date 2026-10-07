using PaxPanel;
using PaxPanel.Sensors;

namespace PaxPanel.Tests;

/// <summary>v2 values, using sensor names exactly as --dump-sensors printed them on this PC.</summary>
public class HardwareAssemblerV2Tests
{
    static SensorReading R(string hw, string type, string name, double? v, string id = "/intelcpu/0") =>
        new(hw, id, "HW", type, name, v);

    static List<SensorReading> HybridCpu()
    {
        var rs = new List<SensorReading>();
        // 2 P-cores with 2 threads each, 3 E-cores with one thread each
        rs.Add(R("Cpu", "Load", "CPU Core #1 Thread #1", 100));
        rs.Add(R("Cpu", "Load", "CPU Core #1 Thread #2", 50));
        rs.Add(R("Cpu", "Load", "CPU Core #2 Thread #1", 10));
        rs.Add(R("Cpu", "Load", "CPU Core #2 Thread #2", 30));
        rs.Add(R("Cpu", "Load", "CPU Core #3", 7));
        rs.Add(R("Cpu", "Load", "CPU Core #4", 8));
        rs.Add(R("Cpu", "Load", "CPU Core #5", 9));
        rs.Add(R("Cpu", "Load", "CPU Core Max", 100));
        rs.Add(R("Cpu", "Load", "CPU Total", 40));
        rs.Add(R("Cpu", "Temperature", "P-Core #1", 72));
        rs.Add(R("Cpu", "Temperature", "P-Core #2", 75));
        rs.Add(R("Cpu", "Temperature", "E-Core #1", 60));
        rs.Add(R("Cpu", "Temperature", "E-Core #2", 61));
        rs.Add(R("Cpu", "Temperature", "E-Core #3", 62));
        rs.Add(R("Cpu", "Temperature", "P-Core #1 Distance to TjMax", 28));
        rs.Add(R("Cpu", "Clock", "P-Core #1", 5400));
        rs.Add(R("Cpu", "Clock", "P-Core #2", 5600));
        rs.Add(R("Cpu", "Clock", "E-Core #1", 4200));
        rs.Add(R("Cpu", "Clock", "E-Core #2", 4300));
        rs.Add(R("Cpu", "Clock", "E-Core #3", 4400));
        rs.Add(R("Cpu", "Clock", "Bus Speed", 100));
        return rs;
    }

    [Fact]
    public void Cores_average_threads_and_name_p_then_e_cores()
    {
        var cores = HardwareAssembler.Cpu(HybridCpu()).Cores!;
        Assert.Equal(new[] { "P1", "P2", "E1", "E2", "E3" }, cores.Select(c => c.Name));
        Assert.Equal(new CoreData("P1", true, 75, 72), cores[0]);
        Assert.Equal(new CoreData("P2", true, 20, 75), cores[1]);
        Assert.Equal(new CoreData("E3", false, 9, 62), cores[4]);
    }

    [Fact]
    public void P_and_e_clocks_are_averaged_separately()
    {
        var c = HardwareAssembler.Cpu(HybridCpu());
        Assert.Equal(5500, c.PClockMHz);
        Assert.Equal(4300, c.EClockMHz);
    }

    [Fact]
    public void Cpu_without_core_sensors_has_empty_core_list()
    {
        var c = HardwareAssembler.Cpu([R("Cpu", "Load", "CPU Total", 3)]);
        Assert.Empty(c.Cores!);
        Assert.Null(c.PClockMHz);
    }

    [Fact]
    public void Non_hybrid_cpu_without_p_core_names_uses_c_prefix()
    {
        var rs = new List<SensorReading> { R("Cpu", "Load", "CPU Core #1", 10), R("Cpu", "Load", "CPU Core #2", 20) };
        Assert.Equal(new[] { "C1", "C2" }, HardwareAssembler.Cpu(rs).Cores!.Select(c => c.Name));
    }

    [Fact]
    public void Gpu_v2_extras()
    {
        var rs = new List<SensorReading>
        {
            R("GpuNvidia", "Temperature", "GPU Memory Junction", 46, "/gpu-nvidia/0"),
            R("GpuNvidia", "Load", "GPU Power", 10.86, "/gpu-nvidia/0"),
            R("GpuNvidia", "Throughput", "GPU PCIe Rx", 30699520, "/gpu-nvidia/0"),
            R("GpuNvidia", "Throughput", "GPU PCIe Tx", 9549824, "/gpu-nvidia/0"),
        };
        var g = HardwareAssembler.Gpu(rs);
        Assert.Equal(46, g.MemJunctionC);
        Assert.Equal(10.86, g.PowerPct);
        Assert.Equal(30699520, g.PcieRxBps);
        Assert.Equal(9549824, g.PcieTxBps);
    }

    [Fact]
    public void Fan_duty_comes_from_control_sensor_of_same_hardware_and_name()
    {
        var rs = new List<SensorReading>
        {
            R("GpuNvidia", "Fan", "GPU Fan 1", 1003, "/gpu-nvidia/0"),
            R("GpuNvidia", "Control", "GPU Fan 1", 30, "/gpu-nvidia/0"),
            R("SuperIO", "Fan", "Fan #1", 2205, "/lpc/it8689e/0"),
            R("SuperIO", "Control", "Fan #1", null, "/lpc/it8689e/0"),
        };
        var fans = HardwareAssembler.Fans(rs, [new() { Label = "CPU", Match = "Fan #1" }, new() { Label = "GPU", Match = "GPU Fan 1" }]);
        Assert.Equal(new FanData("CPU", 2205, null, null), fans[0]);
        Assert.Equal(new FanData("GPU", 1003, null, 30), fans[1]);
    }

    [Fact]
    public void Storage_stats_per_disk_include_io_and_life()
    {
        var rs = new List<SensorReading>
        {
            R("Storage", "Temperature", "Composite Temperature", 46, "/nvme/5"),
            R("Storage", "Throughput", "Read Rate", 2019438.25, "/nvme/5"),
            R("Storage", "Throughput", "Write Rate", 1017246.5, "/nvme/5"),
            R("Storage", "Level", "Life", 97, "/nvme/5"),
            R("Storage", "Level", "Percentage Used", 3, "/nvme/4"),
            R("Storage", "Temperature", "Temperature", 48, "/hdd/2"),
            R("Storage", "Throughput", "Read Rate", 0, "/hdd/2"),
        };
        var s = HardwareAssembler.StorageByDisk(rs);
        Assert.Equal(new DiskStats(46, 2019438.25, 1017246.5, 97), s[5]);
        Assert.Equal(new DiskStats(null, null, null, 97), s[4]);
        Assert.Equal(new DiskStats(48, 0, null, null), s[2]);
    }

    [Fact]
    public void Board_temps_follow_config_labels_and_order()
    {
        var rs = new List<SensorReading>
        {
            R("SuperIO", "Temperature", "Temperature #3", 78, "/lpc/it8689e/0"),
            R("SuperIO", "Temperature", "Temperature #5", 51, "/lpc/it8689e/0"),
            R("Cpu", "Temperature", "Temperature #3", 1),
        };
        var cfg = new List<BoardTempConfig> { new() { Label = "VRM", Match = "Temperature #5" }, new() { Label = "CPU", Match = "Temperature #3" }, new() { Label = "X", Match = "Temperature #9" } };
        Assert.Equal(new[] { new BoardTemp("VRM", 51), new BoardTemp("CPU", 78), new BoardTemp("X", null) }, HardwareAssembler.Board(rs, cfg));
    }
}
