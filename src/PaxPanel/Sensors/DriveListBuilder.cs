namespace PaxPanel.Sensors;

public sealed record VolumeInfo(char Letter, string Label, bool Removable, double TotalGB, double FreeGB);

public static class DriveListBuilder
{
    public static (List<DriveData> Drives, int More) Build(IReadOnlyList<DriveConfig> configured,
        IReadOnlyList<VolumeInfo> mounted, IReadOnlyDictionary<char, double> temps, int maxExtra)
    {
        var byLetter = mounted.ToDictionary(v => char.ToUpperInvariant(v.Letter));
        var list = new List<DriveData>();
        foreach (var c in configured)
        {
            var letter = c.Letter[0];
            list.Add(byLetter.TryGetValue(letter, out var v)
                ? Row(v, c.Label, extra: false, temps)
                : new DriveData(c.Letter, c.Label, false, false, false, null, null, null));
        }

        var configuredLetters = configured.Select(c => c.Letter[0]).ToHashSet();
        var extras = byLetter.Values
            .Where(v => !configuredLetters.Contains(char.ToUpperInvariant(v.Letter)))
            .OrderBy(v => char.ToUpperInvariant(v.Letter))
            .ToList();
        foreach (var v in extras.Take(maxExtra))
        {
            var label = string.IsNullOrWhiteSpace(v.Label) ? (v.Removable ? "USB" : "DISK") : v.Label.Trim().ToUpperInvariant();
            list.Add(Row(v, label, extra: true, temps));
        }
        return (list, Math.Max(0, extras.Count - maxExtra));
    }

    static DriveData Row(VolumeInfo v, string label, bool extra, IReadOnlyDictionary<char, double> temps)
    {
        var letter = char.ToUpperInvariant(v.Letter);
        return new DriveData(letter.ToString(), label, true, v.Removable, extra,
            Math.Round(v.TotalGB - v.FreeGB, 1), Math.Round(v.TotalGB, 1),
            temps.TryGetValue(letter, out var t) ? t : null);
    }
}
