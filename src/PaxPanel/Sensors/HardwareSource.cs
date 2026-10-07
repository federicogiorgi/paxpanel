using LibreHardwareMonitor.Hardware;

namespace PaxPanel.Sensors;

/// <summary>Owns the LibreHardwareMonitor Computer. Not thread-safe: call Read from one thread.</summary>
public sealed class HardwareSource : IDisposable
{
    readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMotherboardEnabled = true,
        IsStorageEnabled = true,
    };

    public HardwareSource() => _computer.Open();

    public List<SensorReading> Read()
    {
        var list = new List<SensorReading>();
        foreach (var hw in _computer.Hardware) Collect(hw, list);
        return list;
    }

    static void Collect(IHardware hw, List<SensorReading> list)
    {
        try
        {
            hw.Update();
        }
        catch (Exception e)
        {
            Log.Once("update:" + hw.Identifier, $"Update failed for {hw.Name}: {e.Message}");
        }
        foreach (var s in hw.Sensors)
            list.Add(new SensorReading(hw.HardwareType.ToString(), hw.Identifier.ToString(), hw.Name,
                s.SensorType.ToString(), s.Name, HardwareAssembler.Clean(s.Value)));
        foreach (var sub in hw.SubHardware) Collect(sub, list);
    }

    public void Dispose() => _computer.Close();
}
