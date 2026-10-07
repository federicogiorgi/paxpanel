namespace PaxPanel.Sensors;

public sealed class SnapshotBuilder(PanelConfig cfg, HardwareSource? hw, SystemSource sys, DiskMapper disks,
    IReadOnlyList<string> warnings, FanMaxTracker? fanMax = null)
{
    static readonly TimeSpan SaveFanMaxEvery = TimeSpan.FromMinutes(1);
    readonly UiData _ui = new(cfg.Cpu, cfg.Gpu, cfg.Memory, cfg.StorageLogos, cfg.Network.Logo);
    readonly FanMaxTracker _fanMax = fanMax ?? new FanMaxTracker();
    DateTime _fanMaxSavedAt = DateTime.MinValue;

    public static string FanMaxPath => Path.Combine(Paths.DataDir, "fanmax.json");

    /// <summary>Runs one source; if it throws, logs once per key and returns the fallback, so one failing
    /// source only blanks its own fields instead of the whole tick.</summary>
    internal static T Safe<T>(string key, Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception e)
        {
            Log.Once("source:" + key + ":" + e.GetType().Name, $"{key} unavailable: {e.Message}");
            return fallback;
        }
    }

    public Snapshot Build(DateTime now)
    {
        var utc = now.ToUniversalTime();
        var readings = Safe("hardware", () => hw?.Read() ?? [], []);
        var stats = Safe("disk map", () => DiskMapper.ByLetter(disks.LetterToDisk(), HardwareAssembler.StorageByDisk(readings)),
            new Dictionary<char, DiskStats>());
        var volumes = Safe("volumes", sys.ReadVolumes, []);
        var (drives, more) = DriveListBuilder.Build(cfg.Drives, volumes, stats, cfg.MaxExtraDrives);

        var fans = HardwareAssembler.Fans(readings, cfg.Fans)
            .Select((f, i) => f with { MaxRpm = _fanMax.MaxFor(f.Label, f.Rpm, f.DutyPct, cfg.Fans[i].MaxRpm) })
            .ToList();
        if (_fanMax.Dirty && utc - _fanMaxSavedAt > SaveFanMaxEvery)
        {
            _fanMaxSavedAt = utc;
            Safe("fan max save", () => { _fanMax.Save(FanMaxPath); return true; }, false);
        }

        return new Snapshot(
            now.ToString("yyyy-MM-dd'T'HH:mm:ss"),
            _ui,
            HardwareAssembler.Cpu(readings),
            HardwareAssembler.Gpu(readings),
            Safe("memory", sys.ReadRam, new RamData(null, null, null)),
            fans,
            drives,
            more,
            Safe("network", () => sys.ReadNet(cfg.Network.Adapter, utc), new NetData(null, null, null)),
            warnings,
            HardwareAssembler.Board(readings, cfg.Board),
            Safe("processes", () => sys.ReadSys(utc), new SysData(Environment.TickCount64 / 1000.0, [])));
    }
}
