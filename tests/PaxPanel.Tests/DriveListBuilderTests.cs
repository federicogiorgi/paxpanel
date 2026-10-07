using PaxPanel;
using PaxPanel.Sensors;

namespace PaxPanel.Tests;

public class DriveListBuilderTests
{
    static readonly List<DriveConfig> Cfg =
    [
        new() { Letter = "C", Label = "SYSTEM" },
        new() { Letter = "D", Label = "DRIVE" },
    ];

    [Fact]
    public void Configured_drives_keep_config_order_and_labels()
    {
        var mounted = new List<VolumeInfo> { new('D', "DRIVE", false, 7452, 7181), new('C', "SYSTEM", false, 1862, 1507) };
        var (drives, more) = DriveListBuilder.Build(Cfg, mounted, new Dictionary<char, double> { ['C'] = 40 }, 3);
        Assert.Equal(new[] { "C", "D" }, drives.Select(d => d.Letter));
        Assert.Equal(new DriveData("C", "SYSTEM", true, false, false, 355, 1862, 40), drives[0]);
        Assert.Equal(0, more);
    }

    [Fact]
    public void Unmounted_configured_drive_is_kept_as_not_mounted()
    {
        var (drives, _) = DriveListBuilder.Build(Cfg, [new('C', "SYSTEM", false, 1862, 1507)], new Dictionary<char, double>(), 3);
        Assert.Equal(new DriveData("D", "DRIVE", false, false, false, null, null, null), drives[1]);
    }

    [Fact]
    public void Missing_temperature_is_null()
    {
        var (drives, _) = DriveListBuilder.Build(Cfg, [new('C', "SYSTEM", false, 1862, 1507)], new Dictionary<char, double>(), 3);
        Assert.Null(drives[0].TempC);
        Assert.True(drives[0].Mounted);
    }

    [Fact]
    public void Extra_drives_sorted_capped_and_counted()
    {
        var mounted = new List<VolumeInfo>
        {
            new('C', "SYSTEM", false, 1862, 1507), new('D', "DRIVE", false, 7452, 7181),
            new('K', "", true, 32, 10), new('H', "stick", true, 64, 30),
            new('J', "BACKUP", false, 2000, 1000), new('I', "CAM", true, 128, 100), new('L', "X", true, 8, 1),
        };
        var (drives, more) = DriveListBuilder.Build(Cfg, mounted, new Dictionary<char, double> { ['J'] = 31 }, 3);
        Assert.Equal(new[] { "C", "D", "H", "I", "J" }, drives.Select(d => d.Letter));
        Assert.Equal("STICK", drives[2].Label);
        Assert.True(drives[2].Extra);
        Assert.True(drives[2].Removable);
        Assert.Equal(31, drives[4].TempC);
        Assert.Equal(2, more);
    }

    [Fact]
    public void Extra_drive_without_label_gets_a_generic_one()
    {
        var (drives, _) = DriveListBuilder.Build([], [new('K', " ", true, 32, 10), new('M', "", false, 100, 50)], new Dictionary<char, double>(), 3);
        Assert.Equal("USB", drives[0].Label);
        Assert.Equal("DISK", drives[1].Label);
    }

    [Fact]
    public void Zero_extra_slots_counts_everything_as_more()
    {
        var (drives, more) = DriveListBuilder.Build([], [new('K', "A", true, 32, 10)], new Dictionary<char, double>(), 0);
        Assert.Empty(drives);
        Assert.Equal(1, more);
    }

    [Fact]
    public void Disk_stats_fill_temperature_io_and_life()
    {
        var stats = new Dictionary<char, DiskStats> { ['C'] = new(47, 2_000_000, 1_000_000, 97) };
        var (drives, _) = DriveListBuilder.Build(Cfg, [new('C', "SYSTEM", false, 1862, 1507)], stats, 3);
        Assert.Equal(new DriveData("C", "SYSTEM", true, false, false, 355, 1862, 47, 2_000_000, 1_000_000, 97), drives[0]);
        Assert.Equal(new DriveData("D", "DRIVE", false, false, false, null, null, null), drives[1]);
    }
}
