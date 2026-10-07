using System.Text.Json;

namespace PaxPanel.Sensors;

/// <summary>Works out each fan's maximum RPM so the panel can show speed as a fraction of it.
/// A configured value wins; otherwise the highest RPM seen, or (better) an estimate RPM ÷ duty taken at the
/// highest duty seen, whichever is larger. Learned values persist across restarts.</summary>
public sealed class FanMaxTracker
{
    const double MinDutyForEstimate = 20;

    public sealed class Learned
    {
        public double MaxObserved { get; set; }
        public double BestDuty { get; set; }
        public double DutyEstimate { get; set; }
    }

    readonly Dictionary<string, Learned> _fans;

    public FanMaxTracker() : this(new Dictionary<string, Learned>()) { }

    FanMaxTracker(Dictionary<string, Learned> fans) => _fans = fans;

    /// <summary>True when a learned value changed since the last Save.</summary>
    public bool Dirty { get; private set; }

    public double? MaxFor(string label, double? rpm, double? dutyPct, double? configMax)
    {
        if (configMax is > 0) return configMax;
        if (!_fans.TryGetValue(label, out var f)) _fans[label] = f = new Learned();
        if (rpm is > 0 && rpm > f.MaxObserved) { f.MaxObserved = rpm.Value; Dirty = true; }
        if (rpm is > 0 && dutyPct is >= MinDutyForEstimate && dutyPct >= f.BestDuty)
        {
            var estimate = rpm.Value * 100 / dutyPct.Value;
            if (dutyPct > f.BestDuty || Math.Abs(estimate - f.DutyEstimate) > 1) Dirty = true;
            f.BestDuty = dutyPct.Value;
            f.DutyEstimate = estimate;
        }
        var max = Math.Max(f.MaxObserved, f.DutyEstimate);
        return max > 0 ? max : null;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(_fans));
        Dirty = false;
    }

    public static FanMaxTracker Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, Learned>>(File.ReadAllText(path)) is { } fans)
                return new FanMaxTracker(fans);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Once("fanmax", $"Learned fan maxima unreadable, starting fresh: {e.Message}");
        }
        return new FanMaxTracker();
    }
}
