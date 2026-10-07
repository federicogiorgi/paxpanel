using PaxPanel;

namespace PaxPanel.Tests;

[Collection("Log")]
public class SensorLoopTests
{
    [Fact]
    public async Task Publishes_repeatedly_and_survives_a_throwing_producer()
    {
        Log.Initialize(Path.Combine(Path.GetTempPath(), "paxpanel-test-" + Guid.NewGuid()));
        var calls = 0;
        var published = new List<string>();
        var loop = new SensorLoop(
            _ => ++calls == 2 ? throw new InvalidOperationException("boom") : $"tick{calls}",
            json => { lock (published) published.Add(json); },
            TimeSpan.FromMilliseconds(50));
        await Task.Delay(400);
        loop.Dispose();
        await loop.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains("tick1", published);
        Assert.Contains("tick3", published);
        Assert.DoesNotContain("tick2", published);
    }
}
