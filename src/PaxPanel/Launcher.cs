using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PaxPanel;

public enum LaunchPlan { RunHere, ViaTask, ViaUac }

/// <summary>paxpanel starts as a normal program and gets administrator rights (needed by the sensor driver)
/// the gentlest way available: through its trigger-less scheduled task (no prompt), else a UAC prompt.
/// Windows never autostarts programs that demand admin rights, so this is also what lets the
/// Settings › Apps › Startup entry work.</summary>
public static class Launcher
{
    static readonly string[] ArgsTheTaskCannotCarry = ["--windowed", "--dump-sensors", "--screenshot", "--remove-startup"];

    public static LaunchPlan Decide(IReadOnlyList<string> args, bool elevated)
    {
        if (elevated || args.Contains("--no-elevate")) return LaunchPlan.RunHere;
        return args.Any(a => ArgsTheTaskCannotCarry.Contains(a)) ? LaunchPlan.ViaUac : LaunchPlan.ViaTask;
    }

    static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    /// <summary>Starts the elevated copy; true when it is on its way (this process should exit).
    /// False when the user declined the UAC prompt: then this copy runs on without the CPU and fan sensors.</summary>
    public static bool Relaunch(LaunchPlan plan, string[] args)
    {
        if (plan == LaunchPlan.ViaTask && ElevatedTask.Run()) return true;
        try
        {
            Process.Start(new ProcessStartInfo(ExePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
                WorkingDirectory = Path.GetDirectoryName(ExePath)!,
            });
            return true;
        }
        catch (Win32Exception e)
        {
            Log.Warn($"Administrator rights declined ({e.Message}); running without CPU and fan sensors");
            return false;
        }
    }

    /// <summary>Keeps the scheduled task and the Startup entry pointing at this exe. Called on elevated starts.</summary>
    public static void EnsureRegistered()
    {
        ElevatedTask.Ensure(ExePath);
        if (new StartupRegistration().EnsureRunEntry(ExePath))
            Log.Info("Added paxpanel to Settings > Apps > Startup (switched off)");
    }

    public static int RemoveStartup()
    {
        new StartupRegistration().Remove();
        ElevatedTask.Remove();
        Log.Info("Removed the Startup entry and the scheduled task");
        MessageBox.Show("paxpanel was removed from Startup apps, and its scheduled task was deleted.\n\n" +
                        "Launching PaxPanel.exe as administrator adds them back (Startup switched off).",
            "paxpanel", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }
}

/// <summary>A scheduled task with no trigger whose only job is to start paxpanel with administrator rights
/// without a UAC prompt. It never starts anything by itself; paxpanel runs it (schtasks /run).</summary>
public static class ElevatedTask
{
    public const string Name = "paxpanel";

    public static string Xml(string exePath, string userId)
    {
        static string E(string s) => SecurityElement.Escape(s);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts paxpanel with administrator rights (needed by its sensor driver) without a UAC prompt. It has no trigger: paxpanel runs it when you launch it or when its Startup entry is switched on.</Description>
              </RegistrationInfo>
              <Principals>
                <Principal id="Author">
                  <UserId>{E(userId)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>6</Priority>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{E(exePath)}</Command>
                  <Arguments>--from-task</Arguments>
                  <WorkingDirectory>{E(Path.GetDirectoryName(exePath)!)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    public static string? CommandFromXml(string xml)
    {
        var m = Regex.Match(xml, "<Command>(.*?)</Command>", RegexOptions.Singleline);
        return m.Success
            ? m.Groups[1].Value.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&amp;", "&")
            : null;
    }

    public static bool Run() => Schtasks($"/run /tn \"{Name}\"", out _) == 0;

    public static void Ensure(string exePath)
    {
        if (Schtasks($"/query /tn \"{Name}\" /xml", out var current) == 0
            && string.Equals(CommandFromXml(current), exePath, StringComparison.OrdinalIgnoreCase)) return;
        var file = Path.Combine(Path.GetTempPath(), $"paxpanel-task-{Environment.ProcessId}.xml");
        File.WriteAllText(file, Xml(exePath, System.Security.Principal.WindowsIdentity.GetCurrent().Name), Encoding.Unicode);
        try
        {
            if (Schtasks($"/create /tn \"{Name}\" /xml \"{file}\" /f", out var output) != 0)
                throw new InvalidOperationException("schtasks /create failed: " + output.Trim());
            Log.Info("Scheduled task 'paxpanel' (no trigger) registered for prompt-free starts");
        }
        finally
        {
            File.Delete(file);
        }
    }

    public static void Remove() => Schtasks($"/delete /tn \"{Name}\" /f", out _);

    static int Schtasks(string arguments, out string output)
    {
        using var p = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(15_000);
        return p.HasExited ? p.ExitCode : -1;
    }
}

/// <summary>The "paxpanel" entry in Settings › Apps › Startup: a value in the user's Run key, with the on/off
/// switch Windows keeps under Explorer\StartupApproved\Run (first byte 2 = on, 3 = off).</summary>
public sealed class StartupRegistration(
    string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run",
    string approvedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run")
{
    const string ValueName = "paxpanel";

    public string? RunCommand()
    {
        using var k = Registry.CurrentUser.OpenSubKey(runKey);
        return k?.GetValue(ValueName) as string;
    }

    /// <summary>null: Windows has no switch recorded (counts as on); otherwise the switch position.</summary>
    public bool? IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(approvedKey);
        return k?.GetValue(ValueName) is byte[] { Length: > 0 } b ? b[0] % 2 == 0 : null;
    }

    /// <summary>Creates the entry switched off (true), or just updates its path, keeping the user's switch (false).</summary>
    public bool EnsureRunEntry(string exePath)
    {
        var command = $"\"{exePath}\" --autostart";
        var existed = RunCommand() is not null;
        using (var run = Registry.CurrentUser.CreateSubKey(runKey))
            if (!string.Equals(run.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase))
                run.SetValue(ValueName, command, RegistryValueKind.String);
        if (existed) return false;
        using var approved = Registry.CurrentUser.CreateSubKey(approvedKey);
        var off = new byte[12];
        off[0] = 3;
        BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(off, 4);
        approved.SetValue(ValueName, off, RegistryValueKind.Binary);
        return true;
    }

    public void Remove()
    {
        using (var run = Registry.CurrentUser.OpenSubKey(runKey, writable: true)) run?.DeleteValue(ValueName, throwOnMissingValue: false);
        using var approved = Registry.CurrentUser.OpenSubKey(approvedKey, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
