using PaxPanel;

namespace PaxPanel.Tests;

[Collection("Log")]
public class LogTests
{
    [Fact]
    public void Once_writes_a_key_only_once_and_rolls_at_1MB()
    {
        var dir = Path.Combine(Path.GetTempPath(), "paxpanel-test-" + Guid.NewGuid());
        Log.Initialize(dir);
        Log.Once("k", "first");
        Log.Once("k", "second");
        var text = File.ReadAllText(Log.FilePath);
        Assert.Contains("first", text);
        Assert.DoesNotContain("second", text);

        File.WriteAllText(Log.FilePath, new string('x', 1_000_001));
        Log.Info("after roll");
        Assert.True(File.Exists(Log.FilePath + ".1"));
        Assert.Contains("after roll", File.ReadAllText(Log.FilePath));
        Directory.Delete(dir, true);
    }
}
