using System.Xml.Linq;
using Microsoft.Win32;
using PaxPanel;

namespace PaxPanel.Tests;

public class LauncherTests
{
    [Theory]
    [InlineData(true, new string[0], LaunchPlan.RunHere)]
    [InlineData(false, new[] { "--no-elevate" }, LaunchPlan.RunHere)]
    [InlineData(false, new string[0], LaunchPlan.ViaTask)]
    [InlineData(false, new[] { "--autostart" }, LaunchPlan.ViaTask)]
    [InlineData(false, new[] { "--windowed" }, LaunchPlan.ViaUac)]          // the task cannot pass arguments
    [InlineData(false, new[] { "--dump-sensors" }, LaunchPlan.ViaUac)]
    [InlineData(false, new[] { "--screenshot", "x.png" }, LaunchPlan.ViaUac)]
    [InlineData(false, new[] { "--remove-startup" }, LaunchPlan.ViaUac)]
    public void Decide_how_to_get_administrator_rights(bool elevated, string[] args, LaunchPlan expected) =>
        Assert.Equal(expected, Launcher.Decide(args, elevated));

    [Fact]
    public void Task_xml_has_no_trigger_runs_elevated_and_never_times_out()
    {
        var xml = ElevatedTask.Xml(@"D:\Drive\programs\paxpanel & co\PaxPanel.exe", @"AORUS\feder");
        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.Empty(doc.Descendants(ns + "Triggers"));
        Assert.Equal(@"D:\Drive\programs\paxpanel & co\PaxPanel.exe", doc.Descendants(ns + "Command").Single().Value);
        Assert.Equal("--from-task", doc.Descendants(ns + "Arguments").Single().Value);
        Assert.Equal(@"D:\Drive\programs\paxpanel & co", doc.Descendants(ns + "WorkingDirectory").Single().Value);
        Assert.Equal("HighestAvailable", doc.Descendants(ns + "RunLevel").Single().Value);
        Assert.Equal(@"AORUS\feder", doc.Descendants(ns + "UserId").Single().Value);
        Assert.Equal("PT0S", doc.Descendants(ns + "ExecutionTimeLimit").Single().Value);
        Assert.Equal("IgnoreNew", doc.Descendants(ns + "MultipleInstancesPolicy").Single().Value);
    }

    [Fact]
    public void Registered_command_is_read_back_from_schtasks_xml()
    {
        var xml = ElevatedTask.Xml(@"D:\a & b\PaxPanel.exe", "u");
        Assert.Equal(@"D:\a & b\PaxPanel.exe", ElevatedTask.CommandFromXml(xml));
        Assert.Null(ElevatedTask.CommandFromXml("ERROR: The system cannot find the file specified."));
    }
}

public class StartupRegistrationTests : IDisposable
{
    readonly string _root = @"Software\paxpanel-tests\" + Guid.NewGuid().ToString("N");
    readonly StartupRegistration _reg;

    public StartupRegistrationTests() => _reg = new StartupRegistration(_root + @"\Run", _root + @"\Approved");

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);

    [Fact]
    public void First_registration_adds_the_entry_switched_off()
    {
        Assert.True(_reg.EnsureRunEntry(@"D:\p\PaxPanel.exe"));
        Assert.Equal("\"D:\\p\\PaxPanel.exe\" --autostart", _reg.RunCommand());
        Assert.False(_reg.IsEnabled());
    }

    [Fact]
    public void Later_registrations_keep_the_users_switch_and_follow_a_moved_exe()
    {
        _reg.EnsureRunEntry(@"D:\p\PaxPanel.exe");
        using (var approved = Registry.CurrentUser.CreateSubKey(_root + @"\Approved"))
            approved.SetValue("paxpanel", new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary); // user switched it on
        Assert.False(_reg.EnsureRunEntry(@"E:\moved\PaxPanel.exe"));
        Assert.True(_reg.IsEnabled());
        Assert.Equal("\"E:\\moved\\PaxPanel.exe\" --autostart", _reg.RunCommand());
    }

    [Fact]
    public void Remove_deletes_entry_and_switch()
    {
        _reg.EnsureRunEntry(@"D:\p\PaxPanel.exe");
        _reg.Remove();
        Assert.Null(_reg.RunCommand());
        Assert.Null(_reg.IsEnabled());
    }
}
