using System.Text.Json;
using PaxPanel;
using PaxPanel.Sensors;

namespace PaxPanel.Tests;

public class SnapshotTests
{
    static Snapshot Sample() => new(
        "2026-10-07T18:55:03",
        new UiData(new BrandConfig { Title = "CPU" }, new BrandConfig { Title = "GPU" }, new MemoryConfig(), ["a.png"], null),
        new CpuData(42, 4, 3190, null, 38.4),
        new GpuData(36, 45, 3, 210, 46.3, 3300, 24564),
        new RamData(23300, 131072, 4000),
        [new FanData("CPU", 1259), new FanData("GPU", null)],
        [new DriveData("C", "SYSTEM", true, false, false, 355, 1862, null)],
        0,
        new NetData(12000, 148000, 1000),
        []);

    [Fact]
    public void Json_is_camel_case_and_matches_contract()
    {
        using var doc = JsonDocument.Parse(Sample().ToJson());
        var root = doc.RootElement;
        Assert.Equal("2026-10-07T18:55:03", root.GetProperty("time").GetString());
        Assert.Equal(42, root.GetProperty("cpu").GetProperty("tempC").GetDouble());
        Assert.Equal(24564, root.GetProperty("gpu").GetProperty("vramTotalMB").GetDouble());
        Assert.Equal("CPU", root.GetProperty("ui").GetProperty("cpu").GetProperty("title").GetString());
        Assert.Equal("SYSTEM", root.GetProperty("drives")[0].GetProperty("label").GetString());
        Assert.Equal(0, root.GetProperty("moreDrives").GetInt32());
        Assert.Equal(148000, root.GetProperty("net").GetProperty("downBps").GetDouble());
    }

    [Fact]
    public void Json_writes_nulls()
    {
        using var doc = JsonDocument.Parse(Sample().ToJson());
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("cpu").GetProperty("voltV").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("fans")[1].GetProperty("rpm").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("drives")[0].GetProperty("tempC").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("ui").GetProperty("netLogo").ValueKind);
    }
}
