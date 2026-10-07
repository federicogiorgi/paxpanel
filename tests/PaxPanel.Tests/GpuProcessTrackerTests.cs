using PaxPanel.Sensors;

namespace PaxPanel.Tests;

public class GpuProcessTrackerTests
{
    const string Eng3D = "_luid_0x00000000_0x000164B6_phys_0_eng_0_engtype_3D";
    const string EngCopy = "_luid_0x00000000_0x000164B6_phys_0_eng_5_engtype_Copy";
    static readonly Dictionary<int, string> Names = new() { [2740] = "blender", [2568] = "dwm", [100] = "chrome", [101] = "chrome" };

    [Theory]
    [InlineData("pid_2740_luid_0x00000000_0x000164B6_phys_0_eng_0_engtype_3D", 2740)]
    [InlineData("pid_4_luid_0x0_0x1_phys_0_eng_14_engtype_Security_1", 4)]
    [InlineData("_Total", null)]
    [InlineData("pid_x_luid", null)]
    public void Parses_pid_from_instance_name(string instance, int? pid) =>
        Assert.Equal(pid, GpuProcessTracker.ParsePid(instance));

    [Fact]
    public void Busiest_engine_per_process_from_two_timer_samples()
    {
        var t = new GpuProcessTracker();
        // first sample: no rates yet
        Assert.Empty(t.Update([("pid_2740" + Eng3D, 0, 0), ("pid_2740" + EngCopy, 0, 0), ("pid_2568" + Eng3D, 0, 0)], Names, 1));
        // 1 s = 10,000,000 x 100 ns later: blender 3D busy 0.64 s (64 %), copy 10 %; dwm 2 %
        var top = t.Update([("pid_2740" + Eng3D, 6_400_000, 10_000_000), ("pid_2740" + EngCopy, 1_000_000, 10_000_000),
                            ("pid_2568" + Eng3D, 200_000, 10_000_000)], Names, 1);
        Assert.Equal([new ProcessLoad("blender", 64)], top);
    }

    [Fact]
    public void Same_name_processes_add_up_capped_at_100()
    {
        var t = new GpuProcessTracker();
        t.Update([("pid_100" + Eng3D, 0, 0), ("pid_101" + Eng3D, 0, 0)], Names, 1);
        var top = t.Update([("pid_100" + Eng3D, 7_000_000, 10_000_000), ("pid_101" + Eng3D, 6_000_000, 10_000_000)], Names, 1);
        Assert.Equal([new ProcessLoad("chrome", 100)], top);
    }

    [Fact]
    public void Unknown_pids_counter_resets_and_idle_engines_are_skipped()
    {
        var t = new GpuProcessTracker();
        t.Update([("pid_9999" + Eng3D, 0, 0), ("pid_2740" + Eng3D, 5_000_000, 0), ("pid_2568" + Eng3D, 0, 0)], Names, 3);
        var top = t.Update([("pid_9999" + Eng3D, 5_000_000, 10_000_000), ("pid_2740" + Eng3D, 100, 10_000_000),
                            ("pid_2568" + Eng3D, 0, 10_000_000)], Names, 3);
        Assert.Empty(top);
    }
}
