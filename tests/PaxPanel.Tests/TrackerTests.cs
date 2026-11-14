using PaxPanel.Sensors;

namespace PaxPanel.Tests;

[Collection("Log")]
public class FanMaxTrackerTests
{
    public FanMaxTrackerTests() => PaxPanel.Log.Initialize(Path.Combine(Path.GetTempPath(), "paxpanel-test-" + Guid.NewGuid()));

    [Fact]
    public void Config_max_wins()
    {
        var t = new FanMaxTracker();
        Assert.Equal(1800, t.MaxFor("CPU", rpm: 2200, dutyPct: null, configMax: 1800));
    }

    [Fact]
    public void Without_duty_the_highest_rpm_seen_is_the_max()
    {
        var t = new FanMaxTracker();
        Assert.Equal(900, t.MaxFor("CPU", 900, null, null));
        Assert.Equal(2213, t.MaxFor("CPU", 2213, null, null));
        Assert.Equal(2213, t.MaxFor("CPU", 1200, null, null));
    }

    [Fact]
    public void Duty_estimate_stands_in_only_until_the_fan_has_been_seen_working()
    {
        var t = new FanMaxTracker();
        // RTX 4090 on this PC: 30 % duty at ~1003 rpm, so far only idling: estimate 1003 / 0.30
        Assert.Equal(1003 / 0.30, t.MaxFor("GPU", 1003, 30, null)!.Value, 3);
        // under heavy load its own fan curve stops at ~49 % duty, 1713 rpm: that is the max from now on
        Assert.Equal(1713, t.MaxFor("GPU", 1713, 49, null));
        // back to idle: the max stays
        Assert.Equal(1713, t.MaxFor("GPU", 1003, 30, null));
    }

    [Fact]
    public void Learned_file_from_the_old_rule_switches_to_the_highest_rpm_seen()
    {
        var path = Path.Combine(Path.GetTempPath(), "paxpanel-fanmax-" + Guid.NewGuid() + ".json");
        File.WriteAllText(path, """{"GPU":{"MaxObserved":1713,"BestDuty":49,"DutyEstimate":3485.7}}""");
        Assert.Equal(1713, FanMaxTracker.Load(path).MaxFor("GPU", 1100, 32, null));
        File.Delete(path);
    }

    [Fact]
    public void Idle_speed_is_the_lowest_steady_rpm_seen()
    {
        var t = new FanMaxTracker();
        Assert.Null(t.MinFor("CPU"));
        t.MaxFor("CPU", 1400, null, null);           // first reading: not yet known to be steady
        Assert.Null(t.MinFor("CPU"));
        t.MaxFor("CPU", 1390, null, null);           // within 5 % of the last one: steady
        Assert.Equal(1390, t.MinFor("CPU"));
        t.MaxFor("CPU", 2200, null, null);
        t.MaxFor("CPU", 2210, null, null);
        Assert.Equal(1390, t.MinFor("CPU"));         // working hard does not raise it
    }

    [Fact]
    public void Spin_up_transients_and_a_stopped_fan_do_not_lower_the_idle_speed()
    {
        var t = new FanMaxTracker();
        t.MaxFor("GPU", 1003, 30, null);
        t.MaxFor("GPU", 1000, 30, null);
        t.MaxFor("GPU", 0, 0, null);                 // zero-rpm mode
        t.MaxFor("GPU", 400, 30, null);              // spinning up
        t.MaxFor("GPU", 950, 30, null);
        Assert.Equal(1000, t.MinFor("GPU"));
    }

    [Fact]
    public void Low_duty_is_ignored_and_stopped_fan_has_no_max_until_seen()
    {
        var t = new FanMaxTracker();
        Assert.Null(t.MaxFor("GPU", 0, 0, null));
        Assert.Equal(500, t.MaxFor("GPU", 500, 10, null));
    }

    [Fact]
    public void Learned_values_round_trip_through_a_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "paxpanel-fanmax-" + Guid.NewGuid() + ".json");
        var t = new FanMaxTracker();
        t.MaxFor("CPU", 2213, null, null);
        t.MaxFor("GPU", 1003, 30, null);
        t.MaxFor("CPU", 2200, null, null);
        t.Save(path);
        var back = FanMaxTracker.Load(path);
        Assert.Equal(2200, back.MinFor("CPU"));
        Assert.Equal(2213, back.MaxFor("CPU", 100, null, null));
        Assert.Equal(1003 / 0.30, back.MaxFor("GPU", 100, 10, null)!.Value, 3);
        File.Delete(path);
    }

    [Fact]
    public void Missing_or_corrupt_file_starts_empty()
    {
        var path = Path.Combine(Path.GetTempPath(), "paxpanel-fanmax-" + Guid.NewGuid() + ".json");
        Assert.Null(FanMaxTracker.Load(path).MaxFor("CPU", null, null, null));
        File.WriteAllText(path, "{ not json");
        Assert.Null(FanMaxTracker.Load(path).MaxFor("CPU", null, null, null));
        File.Delete(path);
    }
}

public class ProcessCpuTrackerTests
{
    static readonly DateTime T0 = new(2026, 10, 7, 20, 0, 0);

    [Fact]
    public void First_sample_has_no_rates_then_percent_of_all_logical_cpus_grouped_by_name()
    {
        var t = new ProcessCpuTracker();
        Assert.Empty(t.Update([(10, "blender", TimeSpan.FromSeconds(100)), (20, "chrome", TimeSpan.FromSeconds(5)), (21, "chrome", TimeSpan.FromSeconds(5))], T0, 32, 2));
        // 1 s later on 32 logical CPUs: blender used 16 cpu-seconds = 50 %, chrome 0.32 + 0.32 cpu-s = 2 %
        var top = t.Update([(10, "blender", TimeSpan.FromSeconds(116)), (20, "chrome", TimeSpan.FromSeconds(5.32)), (21, "chrome", TimeSpan.FromSeconds(5.32))], T0.AddSeconds(1), 32, 2);
        Assert.Equal(2, top.Count);
        Assert.Equal("blender", top[0].Name);
        Assert.Equal(50, top[0].CpuPct, 3);
        Assert.Equal("chrome", top[1].Name);
        Assert.Equal(2, top[1].CpuPct, 3);
    }

    [Fact]
    public void New_and_idle_processes_are_skipped_and_pid_reuse_is_safe()
    {
        var t = new ProcessCpuTracker();
        t.Update([(0, "Idle", TimeSpan.FromSeconds(500)), (10, "a", TimeSpan.FromSeconds(10))], T0, 4, 3);
        var top = t.Update([(0, "Idle", TimeSpan.FromSeconds(503)), (10, "b", TimeSpan.FromSeconds(1)), (11, "new", TimeSpan.FromSeconds(1))], T0.AddSeconds(1), 4, 3);
        Assert.Empty(top); // Idle excluded, pid 10 reused by another name, pid 11 has no baseline
    }
}
