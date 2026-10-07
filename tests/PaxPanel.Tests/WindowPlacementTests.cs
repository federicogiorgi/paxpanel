using System.Drawing;
using PaxPanel;

namespace PaxPanel.Tests;

public class WindowPlacementTests
{
    [Fact]
    public void Windowed_size_follows_the_monitor_scaling()
    {
        // this PC's 4K main monitor at 150 %: working area 3840 x 2088 (taskbar excluded)
        Assert.Equal(new Size(600, 1920), WindowPlacement.WindowedClientSize(new Size(3840, 2088), 1.5));
    }

    [Fact]
    public void Windowed_size_shrinks_to_fit_a_short_screen_keeping_the_400x1280_shape()
    {
        // 1080p at 100 %: 1280 px would not fit in 1040 px; leave 80 px for title bar and borders
        var s = WindowPlacement.WindowedClientSize(new Size(1920, 1040), 1.0);
        Assert.Equal(960, s.Height);
        Assert.Equal(300, s.Width);
    }

    [Fact]
    public void Windowed_window_is_centred_in_the_working_area()
    {
        var area = new Rectangle(0, 0, 3840, 2088);
        Assert.Equal(new Point(1610, 64), WindowPlacement.Centre(area, new Size(620, 1960)));
    }

    [Fact]
    public void Centre_never_goes_above_or_left_of_the_working_area()
    {
        var area = new Rectangle(-3840, 0, 1000, 800);
        Assert.Equal(new Point(-3840, 0), WindowPlacement.Centre(area, new Size(1200, 900)));
    }
}
