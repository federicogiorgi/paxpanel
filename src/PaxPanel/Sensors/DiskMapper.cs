using System.Management;

namespace PaxPanel.Sensors;

/// <summary>Maps drive letters to Windows physical disk numbers (the same numbers LHM uses in
/// storage identifiers), so temperatures follow the disk even when models are identical.</summary>
public sealed class DiskMapper
{
    const long CacheForMs = 30_000;
    IReadOnlyDictionary<char, int> _cache = new Dictionary<char, int>();
    long? _cachedAtMs;

    /// <summary>Cached for 30 s on a monotonic clock, so wall-clock changes (DST, manual) can't freeze it.</summary>
    public IReadOnlyDictionary<char, int> LetterToDisk()
    {
        var nowMs = Environment.TickCount64;
        if (_cachedAtMs is long at && nowMs - at < CacheForMs) return _cache;
        _cachedAtMs = nowMs;
        try
        {
            var map = new Dictionary<char, int>();
            using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage",
                "SELECT DriveLetter, DiskNumber FROM MSFT_Partition");
            foreach (ManagementBaseObject p in searcher.Get())
            {
                using (p)
                {
                    if (p["DriveLetter"] is not { } raw) continue;
                    var letter = char.ToUpperInvariant(Convert.ToChar(raw));
                    if (!char.IsAsciiLetter(letter)) continue;
                    map[letter] = Convert.ToInt32(p["DiskNumber"]);
                }
            }
            _cache = map;
        }
        catch (Exception e)
        {
            Log.Once("diskmapper", $"Drive letter to disk mapping failed: {e.Message}");
        }
        return _cache;
    }

    public static Dictionary<char, T> ByLetter<T>(IReadOnlyDictionary<char, int> letterToDisk,
        IReadOnlyDictionary<int, T> byDisk)
    {
        var result = new Dictionary<char, T>();
        foreach (var (letter, disk) in letterToDisk)
            if (byDisk.TryGetValue(disk, out var v)) result[letter] = v;
        return result;
    }

    public static Dictionary<char, double> TempsByLetter(IReadOnlyDictionary<char, int> letterToDisk,
        IReadOnlyDictionary<int, double> tempsByDisk)
    {
        var result = new Dictionary<char, double>();
        foreach (var (letter, disk) in letterToDisk)
            if (tempsByDisk.TryGetValue(disk, out var t)) result[letter] = t;
        return result;
    }
}
