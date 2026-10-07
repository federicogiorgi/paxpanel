using PaxPanel;

namespace PaxPanel.Tests;

public class ConfigTests
{
    [Fact]
    public void Defaults_describe_this_pc()
    {
        var c = new PanelConfig();
        Assert.Equal(1000, c.RefreshMs);
        Assert.Equal(400, c.Monitor.Width);
        Assert.Equal(1280, c.Monitor.Height);
        Assert.Equal(new[] { "C", "D", "E", "F", "G", "O" }, c.Drives.Select(d => d.Letter));
        Assert.Equal("DRIVE", c.Drives[1].Label);
        Assert.Equal(4, c.Fans.Count);
        Assert.Equal(3, c.MaxExtraDrives);
        Assert.Equal("i9-14900KS", c.Cpu.Subtitle);
    }

    [Fact]
    public void Parse_reads_values_case_insensitively_and_allows_comments()
    {
        var (c, warn) = ConfigLoader.Parse("""
            // my panel
            { "RefreshMs": 2000, "drives": [ { "letter": "x", "label": "STICK" } ], }
            """);
        Assert.Null(warn);
        Assert.Equal(2000, c.RefreshMs);
        Assert.Single(c.Drives);
        Assert.Equal("X", c.Drives[0].Letter);
        Assert.Equal(4, c.Fans.Count); // untouched sections keep defaults
    }

    [Fact]
    public void Values_are_clamped_and_cleaned()
    {
        var (c, _) = ConfigLoader.Parse("""
            { "refreshMs": 5, "maxExtraDrives": 99,
              "fans": [ {"label":"A","match":"1"},{"label":"B","match":"2"},{"label":"C","match":"3"},
                        {"label":"D","match":"4"},{"label":"E","match":"5"},{"label":"F","match":""} ],
              "drives": [ {"letter":"c","label":"ONE"}, {"letter":"C","label":"DUP"}, {"letter":"1","label":"BAD"} ],
              "monitor": { "width": 0, "height": -1 } }
            """);
        Assert.Equal(250, c.RefreshMs);
        Assert.Equal(6, c.MaxExtraDrives);
        Assert.Equal(new[] { "A", "B", "C", "D" }, c.Fans.Select(f => f.Label));
        Assert.Equal(new[] { "C" }, c.Drives.Select(d => d.Letter));
        Assert.Equal("ONE", c.Drives[0].Label);
        Assert.Equal(400, c.Monitor.Width);
    }

    [Fact]
    public void Null_sections_get_defaults()
    {
        var (c, warn) = ConfigLoader.Parse("""
            { "monitor": null, "cpu": null, "gpu": null, "memory": null, "fans": null,
              "drives": null, "storageLogos": null, "network": null }
            """);
        Assert.Null(warn);
        Assert.Equal(400, c.Monitor.Width);
        Assert.Equal("CPU", c.Cpu.Title);
        Assert.Equal("GPU", c.Gpu.Title);
        Assert.Equal("DDR5", c.Memory.RamType);
        Assert.Empty(c.Fans);
        Assert.Empty(c.Drives);
        Assert.Empty(c.StorageLogos);
        Assert.NotNull(c.Network);
    }

    [Fact]
    public void Wrong_type_returns_defaults_with_warning()
    {
        var (c, warn) = ConfigLoader.Parse("""{ "refreshMs": "fast" }""");
        Assert.NotNull(warn);
        Assert.Contains("config.json", warn);
        Assert.Equal(1000, c.RefreshMs);
    }

    [Fact]
    public void Missing_file_returns_defaults_with_warning()
    {
        var (c, warn) = ConfigLoader.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        Assert.NotNull(warn);
        Assert.Equal(6, c.Drives.Count);
    }
}
