namespace PaxPanel.Sensors;

/// <summary>One flattened LibreHardwareMonitor sensor value. Types are LHM enum names.</summary>
public sealed record SensorReading(string HardwareType, string HardwareId, string HardwareName,
    string SensorType, string Name, double? Value);
