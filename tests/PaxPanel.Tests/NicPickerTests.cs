using PaxPanel.Sensors;

namespace PaxPanel.Tests;

public class NicPickerTests
{
    static readonly List<NicInfo> Nics =
    [
        new("1", "Hyper-V Virtual Ethernet Adapter", true, true),
        new("2", "OpenVPN Data Channel Offload", true, true),
        new("3", "Intel(R) Wi-Fi 6E AX211 160MHz", false, false),
        new("4", "Realtek Gaming 2.5GbE Family Controller", true, false),
    ];

    [Fact]
    public void Match_is_case_insensitive_substring() =>
        Assert.Equal("4", NicPicker.Pick(Nics, "realtek gaming 2.5gbe")!.Id);

    [Fact]
    public void Fallback_skips_virtual_and_vpn()
    {
        Assert.Equal("4", NicPicker.Pick(Nics, "Does Not Exist")!.Id);
        Assert.Equal("4", NicPicker.Pick(Nics, null)!.Id);
    }

    [Fact]
    public void Down_adapters_are_never_picked() =>
        Assert.Null(NicPicker.Pick([new("3", "Intel Wi-Fi", false, false)], "Intel"));

    [Theory]
    [InlineData("Hyper-V Virtual Ethernet Adapter", true)]
    [InlineData("OpenVPN Data Channel Offload", true)]
    [InlineData("WAN Miniport (IP)", true)]
    [InlineData("Microsoft Wi-Fi Direct Virtual Adapter", true)]
    [InlineData("Surfshark WireGuard", true)]
    [InlineData("Realtek Gaming 2.5GbE Family Controller", false)]
    public void LooksVirtual(string description, bool expected) =>
        Assert.Equal(expected, NicPicker.LooksVirtual(description));
}
