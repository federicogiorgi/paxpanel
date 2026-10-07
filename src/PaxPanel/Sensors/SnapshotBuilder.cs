namespace PaxPanel.Sensors;

public sealed class SnapshotBuilder(PanelConfig cfg, HardwareSource? hw, SystemSource sys, DiskMapper disks,
    IReadOnlyList<string> warnings)
{
    readonly UiData _ui = new(cfg.Cpu, cfg.Gpu, cfg.Memory, cfg.StorageLogos, cfg.Network.Logo);

    public Snapshot Build(DateTime now)
    {
        var readings = hw?.Read() ?? [];
        var temps = DiskMapper.TempsByLetter(disks.LetterToDisk(now), HardwareAssembler.StorageTempsByDisk(readings));
        var (drives, more) = DriveListBuilder.Build(cfg.Drives, sys.ReadVolumes(), temps, cfg.MaxExtraDrives);
        return new Snapshot(
            now.ToString("yyyy-MM-dd'T'HH:mm:ss"),
            _ui,
            HardwareAssembler.Cpu(readings),
            HardwareAssembler.Gpu(readings),
            sys.ReadRam(),
            HardwareAssembler.Fans(readings, cfg.Fans),
            drives,
            more,
            sys.ReadNet(cfg.Network.Adapter, now),
            warnings);
    }
}
