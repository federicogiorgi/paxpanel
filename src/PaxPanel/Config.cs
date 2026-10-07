using System.Text.Json;

namespace PaxPanel;

public sealed class MonitorConfig
{
    public int Width { get; set; } = 400;
    public int Height { get; set; } = 1280;
    public string? DeviceName { get; set; }
}

public sealed class BrandConfig
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string? Logo { get; set; }
}

public sealed class MemoryConfig
{
    public string? Logo { get; set; } = "assets/CORSAIRLOGO.png";
    public string RamType { get; set; } = "DDR5";
    public string VramType { get; set; } = "GDDR6X";
}

public sealed class FanConfig
{
    public string Label { get; set; } = "";
    public string Match { get; set; } = "";
}

public sealed class DriveConfig
{
    public string Letter { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class NetworkConfig
{
    public string? Adapter { get; set; } = "Realtek Gaming 2.5GbE";
    public string? Logo { get; set; } = "assets/FASTWEBLOGO.png";
}

public sealed class PanelConfig
{
    public int RefreshMs { get; set; } = 1000;
    public MonitorConfig Monitor { get; set; } = new();
    public BrandConfig Cpu { get; set; } = DefaultCpu();
    public BrandConfig Gpu { get; set; } = DefaultGpu();
    public MemoryConfig Memory { get; set; } = new();
    public List<FanConfig> Fans { get; set; } =
    [
        new() { Label = "CPU", Match = "Fan #1" },
        new() { Label = "PUMP", Match = "Fan #2" },
        new() { Label = "SYS", Match = "Fan #3" },
        new() { Label = "GPU", Match = "GPU Fan" },
    ];
    public List<DriveConfig> Drives { get; set; } =
    [
        new() { Letter = "C", Label = "SYSTEM" },
        new() { Letter = "D", Label = "DRIVE" },
        new() { Letter = "E", Label = "FAST" },
        new() { Letter = "F", Label = "DATA" },
        new() { Letter = "G", Label = "QBIT" },
        new() { Letter = "O", Label = "OLD" },
    ];
    public int MaxExtraDrives { get; set; } = 3;
    public List<string> StorageLogos { get; set; } = ["assets/SAMSUNGLOGO.png", "assets/WDLOGO.png"];
    public NetworkConfig Network { get; set; } = new();

    internal static BrandConfig DefaultCpu() => new() { Title = "CPU", Subtitle = "i9-14900KS", Logo = "assets/INTELLOGO.png" };
    internal static BrandConfig DefaultGpu() => new() { Title = "GPU", Subtitle = "RTX 4090", Logo = "assets/PNYLOGO.png" };
}

public static class ConfigLoader
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static (PanelConfig Config, string? Warning) Load(string path)
    {
        if (!File.Exists(path))
            return (new PanelConfig(), $"config.json not found at {path}, using defaults");
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (IOException e)
        {
            return (new PanelConfig(), $"config.json unreadable ({e.Message}), using defaults");
        }
    }

    public static (PanelConfig Config, string? Warning) Parse(string json)
    {
        PanelConfig? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<PanelConfig>(json, Options);
        }
        catch (JsonException e)
        {
            return (new PanelConfig(), $"config.json invalid ({e.Message}), using defaults");
        }
        return cfg is null
            ? (new PanelConfig(), "config.json is empty, using defaults")
            : (Normalize(cfg), null);
    }

    static PanelConfig Normalize(PanelConfig c)
    {
        c.Monitor ??= new MonitorConfig();
        if (c.Monitor.Width <= 0 || c.Monitor.Height <= 0)
            c.Monitor = new MonitorConfig { DeviceName = c.Monitor.DeviceName };
        c.Cpu ??= PanelConfig.DefaultCpu();
        c.Gpu ??= PanelConfig.DefaultGpu();
        c.Memory ??= new MemoryConfig();
        c.Network ??= new NetworkConfig();
        c.StorageLogos ??= [];
        c.RefreshMs = Math.Clamp(c.RefreshMs, 250, 10_000);
        c.MaxExtraDrives = Math.Clamp(c.MaxExtraDrives, 0, 6);
        c.Fans = (c.Fans ?? [])
            .Where(f => f is not null && !string.IsNullOrWhiteSpace(f.Match))
            .Take(4)
            .ToList();
        c.Drives = (c.Drives ?? [])
            .Where(d => d is not null && !string.IsNullOrEmpty(d.Letter) && char.IsAsciiLetter(d.Letter[0]))
            .Select(d => new DriveConfig { Letter = d.Letter[..1].ToUpperInvariant(), Label = d.Label ?? "" })
            .DistinctBy(d => d.Letter)
            .ToList();
        return c;
    }
}
