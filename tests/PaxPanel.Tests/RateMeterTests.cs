using PaxPanel.Sensors;

namespace PaxPanel.Tests;

public class RateMeterTests
{
    static readonly DateTime T0 = new(2026, 10, 7, 18, 0, 0);

    [Fact]
    public void First_sample_is_null_then_bytes_per_second()
    {
        var m = new RateMeter();
        Assert.Null(m.Update(1000, T0));
        Assert.Equal(500, m.Update(2000, T0.AddSeconds(2)));
    }

    [Fact]
    public void Counter_reset_yields_null_then_recovers()
    {
        var m = new RateMeter();
        m.Update(5_000_000, T0);
        Assert.Null(m.Update(100, T0.AddSeconds(1)));
        Assert.Equal(900, m.Update(1000, T0.AddSeconds(2)));
    }

    [Fact]
    public void Non_advancing_clock_yields_null_and_reset_forgets()
    {
        var m = new RateMeter();
        m.Update(0, T0);
        Assert.Null(m.Update(10, T0));
        m.Reset();
        Assert.Null(m.Update(20, T0.AddSeconds(1)));
    }
}
