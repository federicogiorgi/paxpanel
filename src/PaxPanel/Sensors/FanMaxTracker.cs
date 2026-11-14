using System.Text.Json;

namespace PaxPanel.Sensors;

/// <summary>Works out each fan's maximum RPM so the panel can show speed as a fraction of it.
/// A configured value wins; otherwise the highest RPM actually seen, so the spinner is at full speed when the
/// fan runs as fast as it ever does (a GPU's own fan curve rarely goes past ~50 % duty, so RPM ÷ duty would
/// promise a speed the fan never reaches). Until a fan has been seen working (duty ≥ 40 %), RPM ÷ duty stands
/// in, so a fan seen only idling doesn't look flat out. It also learns each fan's idle speed (the lowest steady
/// RPM seen) so the panel can spin slowly at idle. Learned values persist across restarts.</summary>
public sealed class FanMaxTracker
{
    const double MinDutyForEstimate = 20;
    const double WorkedDuty = 40;
    const double SteadyWithin = 0.05;

    public sealed class Learned
    {
        public double MaxObserved { get; set; }
        public double BestDuty { get; set; }
        public double DutyEstimate { get; set; }
        /// <summary>Lowest RPM seen on two readings in a row within 5 % (spin-up transients don't count); 0 = not yet.</summary>
        public double MinSteady { get; set; }
        internal double Last;
    }

    readonly Dictionary<string, Learned> _fans;

    public FanMaxTracker() : this(new Dictionary<string, Learned>()) { }

    FanMaxTracker(Dictionary<string, Learned> fans) => _fans = fans;

    /// <summary>True when a learned value changed since the last Save.</summary>
    public bool Dirty { get; private set; }

    public double? MaxFor(string label, double? rpm, double? dutyPct, double? configMax)
    {
        if (!_fans.TryGetValue(label, out var f)) _fans[label] = f = new Learned();
        if (rpm is > 0 && f.Last > 0 && Math.Abs(rpm.Value - f.Last) <= f.Last * SteadyWithin
            && (f.MinSteady == 0 || rpm < f.MinSteady)) { f.MinSteady = rpm.Value; Dirty = true; }
        f.Last = rpm ?? 0;
        if (configMax is > 0) return configMax;
        if (rpm is > 0 && rpm > f.MaxObserved) { f.MaxObserved = rpm.Value; Dirty = true; }
        if (rpm is > 0 && dutyPct is >= MinDutyForEstimate && dutyPct >= f.BestDuty)
        {
            var estimate = rpm.Value * 100 / dutyPct.Value;
            if (dutyPct > f.BestDuty || Math.Abs(estimate - f.DutyEstimate) > 1) Dirty = true;
            f.BestDuty = dutyPct.Value;
            f.DutyEstimate = estimate;
        }
        var max = f.BestDuty >= WorkedDuty ? f.MaxObserved : Math.Max(f.MaxObserved, f.DutyEstimate);
        return max > 0 ? max : null;
    }

    /// <summary>The fan's idle speed, once learned (see <see cref="Learned.MinSteady"/>).</summary>
    public double? MinFor(string label) => _fans.TryGetValue(label, out var f) && f.MinSteady > 0 ? f.MinSteady : null;

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
