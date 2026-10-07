using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using PaxPanel.Sensors;

namespace PaxPanel;

/// <summary>--dump-sensors: lists every sensor, drive mapping and adapter for calibrating config.json.</summary>
public static class SensorDump
{
    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);

    public static int Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"paxpanel sensor dump {DateTime.Now:s}  elevated={Env.IsElevated}  pawnio={Env.PawnIoInstalled}");
        sb.AppendLine("-- LibreHardwareMonitor sensors (hardwareType | hardwareId | hardware | sensorType | name | value)");
        using (var hw = new HardwareSource())
        {
            hw.Read();
            Thread.Sleep(1500); // some sensors (load, power) need two samples
            foreach (var r in hw.Read())
                sb.AppendLine($"{r.HardwareType,-12} | {r.HardwareId,-26} | {r.HardwareName,-36} | {r.SensorType,-11} | {r.Name,-30} | {r.Value?.ToString("0.###") ?? "null"}");
        }
        sb.AppendLine("-- drive letter -> physical disk number");
        foreach (var (letter, disk) in new DiskMapper().LetterToDisk().OrderBy(p => p.Key))
            sb.AppendLine($"{letter}: -> disk {disk}");
        sb.AppendLine("-- network adapters (up | virtual | description | speed Mbps)");
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            sb.AppendLine($"{n.OperationalStatus == OperationalStatus.Up,-5} | {NicPicker.LooksVirtual(n.Description),-5} | {n.Description} | {n.Speed / 1_000_000}");

        Directory.CreateDirectory(Paths.DataDir);
        var path = Path.Combine(Paths.DataDir, "sensors.txt");
        File.WriteAllText(path, sb.ToString());
        if (AttachConsole(-1)) Console.WriteLine(Environment.NewLine + sb + $"Saved to {path}");
        return 0;
    }
}
