namespace PaxPanel.Sensors;

/// <summary>CPU % per process name (all instances summed, like Task Manager's app grouping), from two
/// successive samples of total processor time. Percent is of all logical CPUs.</summary>
public sealed class ProcessCpuTracker
{
    Dictionary<int, (string Name, TimeSpan Cpu)> _previous = new();
    DateTime _previousAt;

    public List<ProcessLoad> Update(IEnumerable<(int Pid, string Name, TimeSpan Cpu)> processes, DateTime at,
        int logicalCpus, int top)
    {
        var current = new Dictionary<int, (string Name, TimeSpan Cpu)>();
        foreach (var p in processes) current[p.Pid] = (p.Name, p.Cpu);

        var result = new List<ProcessLoad>();
        var elapsed = (at - _previousAt).TotalSeconds;
        if (_previous.Count > 0 && elapsed > 0)
        {
            var byName = new Dictionary<string, double>();
            foreach (var (pid, now) in current)
            {
                if (pid == 0 || now.Name == "Idle") continue;
                if (!_previous.TryGetValue(pid, out var before) || before.Name != now.Name || now.Cpu < before.Cpu) continue;
                byName[now.Name] = byName.GetValueOrDefault(now.Name) + (now.Cpu - before.Cpu).TotalSeconds;
            }
            result = byName
                .Select(p => new ProcessLoad(p.Key, p.Value / (elapsed * logicalCpus) * 100))
                .Where(p => p.CpuPct > 0)
                .OrderByDescending(p => p.CpuPct)
                .Take(top)
                .ToList();
        }
        _previous = current;
        _previousAt = at;
        return result;
    }
}
