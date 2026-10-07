using System.Drawing;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;
using PaxPanel.Sensors;

namespace PaxPanel;

public sealed class PanelWindow : Form
{
    const string Host = "paxpanel.local";
    static readonly Color Background = Color.FromArgb(0x1f, 0x1f, 0x1f);

    readonly string _baseDir;
    readonly bool _forceWindowed;
    readonly string? _screenshotPath;
    readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Background };
    readonly System.Windows.Forms.Timer _monitorTimer = new() { Interval = 10_000 };
    readonly ContextMenuStrip _menu = new();
    readonly SystemSource _system = new();
    readonly DiskMapper _disks = new();
    HardwareSource? _hardware;
    readonly FanMaxTracker _fanMax = FanMaxTracker.Load(SnapshotBuilder.FanMaxPath);
    PanelConfig _cfg = new();
    string? _configWarning;
    volatile SnapshotBuilder? _builder;
    SensorLoop? _loop;
    bool _pageReady;

    public PanelWindow(string baseDir, bool forceWindowed, string? screenshotPath)
    {
        _baseDir = baseDir;
        _forceWindowed = forceWindowed;
        _screenshotPath = screenshotPath;

        Text = "paxpanel";
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);
        BackColor = Background;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        Controls.Add(_web);

        _menu.Items.Add("Reload", null, (_, _) => Reload());
        _menu.Items.Add("Save screenshot…", null, async (_, _) => await SaveScreenshotDialogAsync());
        _menu.Items.Add("Exit", null, (_, _) => Close());

        LoadConfig();
        Place(); // decide full screen vs window before the first paint
        Load += async (_, _) => await InitAsync();
        DpiChanged += (_, e) => { e.Cancel = true; Place(); };
        _monitorTimer.Tick += (_, _) => Place();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e) => BeginInvoke(Place);

    /// <summary>Mini-monitor present: borderless full screen on it. Otherwise (or with --windowed): a normal
    /// window centred on the main monitor that can be moved, resized and closed. Re-checked every 10 s and on
    /// display changes, so the panel follows the mini-monitor when it is plugged in or unplugged.</summary>
    void Place()
    {
        if (IsDisposed) return;
        var screens = Screen.AllScreens.Select(s => new ScreenInfo(s.DeviceName, s.Bounds)).ToList();
        var target = _forceWindowed ? null : MonitorPicker.Pick(screens, _cfg.Monitor);
        if (target is not null)
        {
            if (_mode != Mode.FullScreen)
            {
                _mode = Mode.FullScreen;
                FormBorderStyle = FormBorderStyle.None;
                TopMost = true;
                Log.Info($"Full screen on {target.DeviceName}");
            }
            if (Bounds != target.Bounds) Bounds = target.Bounds;
        }
        else if (_mode != Mode.Windowed)
        {
            _mode = Mode.Windowed;
            TopMost = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            ClientSize = WindowPlacement.WindowedClientSize(screen.WorkingArea.Size, ScaleOf(screen));
            Location = WindowPlacement.Centre(screen.WorkingArea, Size);
            Log.Info(_forceWindowed ? "Windowed (--windowed)" : "Mini-monitor not found: windowed on the main monitor");
        }
    }

    enum Mode { None, FullScreen, Windowed }
    Mode _mode = Mode.None;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(Point pt, uint flags);

    [System.Runtime.InteropServices.DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>Windows display scaling of a monitor (1.5 for 150 %).</summary>
    static double ScaleOf(Screen screen)
    {
        var monitor = MonitorFromPoint(new Point(screen.Bounds.X + 1, screen.Bounds.Y + 1), 2 /* nearest */);
        return GetDpiForMonitor(monitor, 0 /* effective */, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;
    }

    async Task InitAsync()
    {
        try
        {
            _monitorTimer.Start();
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Paths.DataDir, "webview2"));
            await _web.EnsureCoreWebView2Async(env);

            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = _forceWindowed;
            core.SetVirtualHostNameToFolderMapping(Host, Path.Combine(_baseDir, "web"), CoreWebView2HostResourceAccessKind.Allow);
            core.WebMessageReceived += OnWebMessage;
            core.NavigationStarting += (_, _) => _pageReady = false;
            core.NavigationCompleted += (_, e) => _pageReady = e.IsSuccess;
            core.ProcessFailed += OnProcessFailed;
            core.Navigate($"https://{Host}/index.html");

            try
            {
                _hardware = new HardwareSource();
            }
            catch (Exception e)
            {
                Log.Error("LibreHardwareMonitor failed to open", e);
            }
            StartLoop();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Fail("The Microsoft Edge WebView2 Runtime is missing.\n\nInstall it from https://developer.microsoft.com/microsoft-edge/webview2/", null);
        }
        catch (Exception e)
        {
            Fail(@"paxpanel could not start; see %LOCALAPPDATA%\paxpanel\paxpanel.log.", e);
        }
    }

    /// <summary>Start-up failed: log, tell the user when they are watching, and exit non-zero so the
    /// logon task's repetition can relaunch us instead of leaving a blank panel holding the mutex.</summary>
    void Fail(string message, Exception? e)
    {
        Log.Error("Start-up failed: " + message, e);
        Environment.ExitCode = 1;
        if (_mode == Mode.Windowed || _screenshotPath is not null)
            MessageBox.Show(message, "paxpanel", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Close();
    }

    void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        Log.Warn($"WebView2 process failed: {e.ProcessFailedKind} ({e.Reason})");
        if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
        {
            // The whole browser is gone; exit and let the scheduled task start a fresh panel.
            Environment.ExitCode = 2;
            BeginInvoke(Close);
        }
        else
        {
            BeginInvoke(() => _web.CoreWebView2?.Reload());
        }
    }

    void LoadConfig()
    {
        var (cfg, warning) = ConfigLoader.Load(Path.Combine(_baseDir, "config.json"));
        if (warning is not null) Log.Warn(warning);
        _cfg = cfg;
        _configWarning = warning;
    }

    /// <summary>(Re)starts the sensor loop with the current config, so Reload also applies refreshMs.</summary>
    void StartLoop()
    {
        if (_loop is not null && !StopLoop()) return;
        var warnings = Env.StartupWarnings();
        if (_configWarning is not null) warnings.Add(_configWarning);
        if (_hardware is null) warnings.Add("Hardware sensors failed to start (see log)");
        _builder = new SnapshotBuilder(_cfg, _hardware, _system, _disks, warnings, _fanMax);
        _loop = new SensorLoop(now => _builder!.Build(now).ToJson(), Publish, TimeSpan.FromMilliseconds(_cfg.RefreshMs));
    }

    /// <summary>Stops the loop; false when a tick is still running after 3 s (then LHM must not be touched).</summary>
    bool StopLoop()
    {
        if (_loop is null) return true;
        _loop.Dispose();
        var stopped = _loop.Completion.Wait(TimeSpan.FromSeconds(3));
        if (!stopped) Log.Warn("Sensor loop did not stop within 3 s");
        _loop = null;
        return stopped;
    }

    void Publish(string json)
    {
        if (IsDisposed) return;
        try
        {
            BeginInvoke(() =>
            {
                if (!_pageReady || IsDisposed) return;
                try
                {
                    _web.CoreWebView2?.PostWebMessageAsJson(json);
                }
                catch (Exception e)
                {
                    Log.Once("publish:" + e.GetType().Name, $"Posting to the page failed: {e.Message}");
                }
            });
        }
        catch (InvalidOperationException)
        {
            // Window handle gone during shutdown.
        }
    }

    async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message;
        try { message = e.TryGetWebMessageAsString(); }
        catch (ArgumentException) { return; }

        if (message == "contextmenu")
        {
            _menu.Show(Cursor.Position);
        }
        else if (message == "rendered" && _screenshotPath is not null)
        {
            try
            {
                await Task.Delay(500);
                await SaveScreenshotAsync(_screenshotPath);
            }
            catch (Exception ex)
            {
                Log.Error("Screenshot failed", ex);
                Environment.ExitCode = 1;
            }
            Close();
        }
    }

    void Reload()
    {
        LoadConfig();
        StartLoop();
        Place();
        _web.CoreWebView2?.Reload();
    }

    async Task SaveScreenshotAsync(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var file = File.Create(full);
        await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
        Log.Info($"Screenshot saved to {full}");
    }

    async Task SaveScreenshotDialogAsync()
    {
        using var dialog = new SaveFileDialog { Filter = "PNG image|*.png", FileName = $"paxpanel-{DateTime.Now:yyyyMMdd-HHmmss}.png" };
        if (dialog.ShowDialog(this) == DialogResult.OK) await SaveScreenshotAsync(dialog.FileName);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _monitorTimer.Stop();
        var stopped = StopLoop();
        if (_fanMax.Dirty) SnapshotBuilder.Safe("fan max save", () => { _fanMax.Save(SnapshotBuilder.FanMaxPath); return true; }, false);
        if (stopped) _hardware?.Dispose(); // never close LHM under a still-running tick; the process is exiting anyway
        base.OnFormClosing(e);
    }
}
