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
        Assert.Equal(cfg.Board.Count, s.Board!.Count);
        Assert.True(s.Sys!.UptimeSec > 0);
        using var _ = JsonDocument.Parse(s.ToJson());
    }

    [Fact]
    public void Second_build_reports_top_processes()
    {
        var builder = new SnapshotBuilder(new PanelConfig(), hw: null, new SystemSource(), new DiskMapper(), []);
        builder.Build(DateTime.Now);
        Thread.Sleep(600);
        var s = builder.Build(DateTime.Now);
        Assert.Equal(3, s.Sys!.Top.Count); // a busy desktop always has 3+ processes using some CPU
        Assert.All(s.Sys.Top, p => Assert.InRange(p.CpuPct, 0.0001, 100.5));
        Assert.NotNull(s.Sys.TopGpu);
        Assert.InRange(s.Sys.TopGpu!.Count, 0, 3);
    }

    [Fact]
    public void Safe_returns_fallback_when_a_source_throws()
    {
        Assert.Equal(-1, SnapshotBuilder.Safe<int>("test", () => throw new InvalidOperationException("boom"), -1));
        Assert.Equal(5, SnapshotBuilder.Safe("test", () => 5, -1));
    }
}
