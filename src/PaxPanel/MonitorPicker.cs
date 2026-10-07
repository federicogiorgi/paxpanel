using System.Drawing;

namespace PaxPanel;

public sealed record ScreenInfo(string DeviceName, Rectangle Bounds);

public static class MonitorPicker
{
    public static ScreenInfo? Pick(IReadOnlyList<ScreenInfo> screens, MonitorConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.DeviceName))
            return screens.FirstOrDefault(s => string.Equals(s.DeviceName, cfg.DeviceName, StringComparison.OrdinalIgnoreCase));
        return screens.FirstOrDefault(s => s.Bounds.Width == cfg.Width && s.Bounds.Height == cfg.Height);
    }
}
