namespace PaxPanel;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--dump-sensors")) return SensorDump.Run();
        Log.Info("paxpanel: no window yet (Task 9)");
        return 0;
    }
}
