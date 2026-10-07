namespace PaxPanel.Sensors;

public sealed record NicInfo(string Id, string Description, bool Up, bool VirtualOrLoopback);

public static class NicPicker
{
    static readonly string[] VirtualHints =
        ["virtual", "hyper-v", "miniport", "vpn", "wireguard", "tap-", "tunnel", "loopback", "bluetooth", "surfshark"];

    public static bool LooksVirtual(string description) =>
        VirtualHints.Any(h => description.Contains(h, StringComparison.OrdinalIgnoreCase));

    public static NicInfo? Pick(IEnumerable<NicInfo> nics, string? match)
    {
        var up = nics.Where(n => n.Up).ToList();
        if (!string.IsNullOrWhiteSpace(match))
        {
            var hit = up.FirstOrDefault(n => n.Description.Contains(match, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        return up.FirstOrDefault(n => !n.VirtualOrLoopback);
    }
}
