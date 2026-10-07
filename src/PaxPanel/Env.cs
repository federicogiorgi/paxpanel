using System.Security.Principal;
using Microsoft.Win32;

namespace PaxPanel;

public static class Env
{
    public static bool IsElevated { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public static bool PawnIoInstalled { get; } = HasPawnIo();

    static bool HasPawnIo()
    {
        const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var k = hive.OpenSubKey(key);
            if (k?.GetValue("DisplayVersion") is not null) return true;
        }
        return false;
    }

    public static List<string> StartupWarnings()
    {
        var w = new List<string>();
        if (!PawnIoInstalled) w.Add("PawnIO driver not found: CPU and fan sensors unavailable");
        else if (!IsElevated) w.Add("Not running as administrator: CPU and fan sensors unavailable");
        return w;
    }
}
