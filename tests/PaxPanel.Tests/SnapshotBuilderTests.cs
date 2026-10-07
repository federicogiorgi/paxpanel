using System.Text.Json;
using PaxPanel;
using PaxPanel.Sensors;

namespace PaxPanel.Tests;

public class SnapshotBuilderTests
{
    [Fact]
    public void Builds_a_serialisable_snapshot_without_hardware_source()
    {
        var cfg = new PanelConfig();
        var builder = new SnapshotBuilder(cfg, hw: null, new SystemSource(), new DiskMapper(), ["test warning"]);
        var s = builder.Build(new DateTime(2026, 10, 7, 18, 55, 3));

        Assert.Equal("2026-10-07T18:55:03", s.Time);
        Assert.Null(s.Cpu.TempC);
        Assert.True(s.Ram.TotalMB > 0);
        Assert.Equal("C", s.Drives[0].Letter);
        Assert.True(s.Drives[0].Mounted);
        Assert.Equal(cfg.Fans.Count, s.Fans.Count);
        Assert.Contains("test warning", s.Warnings);
        using var _ = JsonDocument.Parse(s.ToJson());
    }
}
