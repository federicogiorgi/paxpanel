using System.Drawing;
using PaxPanel;

namespace PaxPanel.Tests;

public class MonitorPickerTests
{
    static readonly List<ScreenInfo> ThisPc =
    [
        new(@"\\.\DISPLAY1", new Rectangle(-3840, 0, 3840, 2160)),
        new(@"\\.\DISPLAY2", new Rectangle(3840, 0, 3840, 2160)),
        new(@"\\.\DISPLAY3", new Rectangle(0, 0, 3840, 2160)),
        new(@"\\.\DISPLAY4", new Rectangle(-4240, 0, 400, 1280)),
    ];

    [Fact]
    public void Picks_the_400x1280_screen_by_size() =>
        Assert.Equal(@"\\.\DISPLAY4", MonitorPicker.Pick(ThisPc, new MonitorConfig())!.DeviceName);

    [Fact]
    public void Device_name_overrides_size() =>
        Assert.Equal(@"\\.\DISPLAY2",
            MonitorPicker.Pick(ThisPc, new MonitorConfig { DeviceName = @"\\.\display2" })!.DeviceName);

    [Fact]
    public void Landscape_1280x400_is_not_accepted() =>
        Assert.Null(MonitorPicker.Pick([new("X", new Rectangle(0, 0, 1280, 400))], new MonitorConfig()));

    [Fact]
    public void Unknown_device_name_returns_null() =>
        Assert.Null(MonitorPicker.Pick(ThisPc, new MonitorConfig { DeviceName = @"\\.\DISPLAY9" }));
}
