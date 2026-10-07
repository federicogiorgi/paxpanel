namespace PaxPanel.Sensors;

/// <summary>GPU % per process name from the Windows "GPU Engine" counters (Utilization Percentage, a 100 ns
/// timer per engine instance "pid_N_luid_..._engtype_X"). Like Task Manager, a process counts as busy as its
/// busiest engine; processes with the same name add up (capped at 100).</summary>
public sealed class GpuProcessTracker
{
    Dictionary<string, (long Raw, long Time)> _previous = new();

    public static int? ParsePid(string instance)
    {
        if (!instance.StartsWith("pid_", StringComparison.Ordinal)) return null;
        var end = instance.IndexOf('_', 4);
        return int.TryParse(end < 0 ? instance[4..] : instance[4..end], out var pid) ? pid : null;
    }

    public List<ProcessLoad> Update(IEnumerable<(string Instance, long Raw, long Time100ns)> samples,
        IReadOnlyDictionary<int, string> names, int top)
    {
        var current = new Dictionary<string, (long Raw, long Time)>();
        var busiest = new Dictionary<int, double>();
        foreach (var (instance, raw, time) in samples)
        {
            current[instance] = (raw, time);
            if (!_previous.TryGetValue(instance, out var before) || time <= before.Time || raw < before.Raw) continue;
            if (ParsePid(instance) is not int pid) continue;
            var pct = (double)(raw - before.Raw) / (time - before.Time) * 100;
            busiest[pid] = Math.Max(busiest.GetValueOrDefault(pid), pct);
        }
        _previous = current;

        return busiest
            .Where(p => p.Value > 0 && names.ContainsKey(p.Key))
            .GroupBy(p => names[p.Key])
            .Select(g => new ProcessLoad(g.Key, Math.Min(100, g.Sum(p => p.Value))))
            .OrderByDescending(p => p.CpuPct)
            .Take(top)
            .ToList();
    }
}
