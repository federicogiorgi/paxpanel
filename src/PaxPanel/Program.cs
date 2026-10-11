namespace PaxPanel;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled", e.ExceptionObject as Exception);

        // Get administrator rights for the sensor driver: via the trigger-less task (no prompt) or UAC.
        var plan = Launcher.Decide(args, Env.IsElevated);
        if (plan != LaunchPlan.RunHere && Launcher.Relaunch(plan, args)) return 0;

        if (args.Contains("--remove-startup")) return Launcher.RemoveStartup();
        if (args.Contains("--dump-sensors")) return SensorDump.Run();

        var screenshot = ArgValue(args, "--screenshot");
        var windowed = args.Contains("--windowed");

        Mutex? instance = null;
        if (screenshot is null)
        {
            instance = AcquireSingleInstance();
            if (instance is null) return 0;
        }

        Log.Info($"paxpanel start (elevated={Env.IsElevated}, pawnio={Env.PawnIoInstalled})");
        if (Env.IsElevated)
            Task.Run(() => PaxPanel.Sensors.SnapshotBuilder.Safe("startup registration", () => { Launcher.EnsureRegistered(); return true; }, false));
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Log.Error("UI thread", e.Exception);
        Application.Run(new PanelWindow(AppContext.BaseDirectory, windowed, screenshot));
        instance?.ReleaseMutex();
        return Environment.ExitCode;
    }

    static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    static Mutex? AcquireSingleInstance()
    {
        try
        {
            var m = new Mutex(true, @"Global\paxpanel", out var created);
            if (created) return m;
            m.Dispose();
        }
        catch (UnauthorizedAccessException)
        {
            // Another instance at a different integrity level owns it.
        }
        Log.Info("Another paxpanel is already running; exiting");
        return null;
    }
}
