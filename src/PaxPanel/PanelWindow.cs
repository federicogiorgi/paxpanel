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
    readonly bool _windowed;
    readonly string? _screenshotPath;
    readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Background };
    readonly System.Windows.Forms.Timer _monitorTimer = new() { Interval = 10_000 };
    readonly ContextMenuStrip _menu = new();
    readonly SystemSource _system = new();
    readonly DiskMapper _disks = new();
    HardwareSource? _hardware;
    PanelConfig _cfg = new();
    volatile SnapshotBuilder? _builder;
    SensorLoop? _loop;
    bool _pageReady;

    public PanelWindow(string baseDir, bool windowed, string? screenshotPath)
    {
        _baseDir = baseDir;
        _windowed = windowed;
        _screenshotPath = screenshotPath;

        Text = "paxpanel";
        BackColor = Background;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = windowed ? FormBorderStyle.FixedSingle : FormBorderStyle.None;
        ShowInTaskbar = windowed;
        TopMost = !windowed;
        ClientSize = new Size(400, 1280);
        Controls.Add(_web);

        _menu.Items.Add("Reload", null, (_, _) => Reload());
        _menu.Items.Add("Save screenshot…", null, async (_, _) => await SaveScreenshotDialogAsync());
        _menu.Items.Add("Exit", null, (_, _) => Close());

        Load += async (_, _) => await InitAsync();
        Shown += (_, _) => Place();
        DpiChanged += (_, e) => { e.Cancel = true; Place(); };
        _monitorTimer.Tick += (_, _) => Place();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (!_windowed) cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW: no Alt-Tab entry
            return cp;
        }
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e) => BeginInvoke(Place);

    void Place()
    {
        if (_windowed || IsDisposed) return;
        var screens = Screen.AllScreens.Select(s => new ScreenInfo(s.DeviceName, s.Bounds)).ToList();
        var target = MonitorPicker.Pick(screens, _cfg.Monitor);
        if (target is null)
        {
            if (_screenshotPath is not null) { FallBackToWindowed(); return; }
            if (Visible) Hide();
            return;
        }
        if (Bounds != target.Bounds) Bounds = target.Bounds;
        if (!Visible) Show();
    }

    void FallBackToWindowed()
    {
        FormBorderStyle = FormBorderStyle.FixedSingle;
        ClientSize = new Size(400, 1280);
        if (!Visible) Show();
    }

    async Task InitAsync()
    {
        LoadConfig();
        _monitorTimer.Start();
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Paths.DataDir, "webview2"));
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show("The Microsoft Edge WebView2 Runtime is missing.\n\nInstall it from https://developer.microsoft.com/microsoft-edge/webview2/",
                "paxpanel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        var core = _web.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDevToolsEnabled = _windowed;
        core.SetVirtualHostNameToFolderMapping(Host, Path.Combine(_baseDir, "web"), CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += OnWebMessage;
        core.NavigationStarting += (_, _) => _pageReady = false;
        core.NavigationCompleted += (_, e) => _pageReady = e.IsSuccess;
        core.Navigate($"https://{Host}/index.html");

        try
        {
            _hardware = new HardwareSource();
        }
        catch (Exception e)
        {
            Log.Error("LibreHardwareMonitor failed to open", e);
        }
        RebuildBuilder();
        _loop = new SensorLoop(now => _builder!.Build(now).ToJson(), Publish, TimeSpan.FromMilliseconds(_cfg.RefreshMs));
    }

    void LoadConfig()
    {
        var (cfg, warning) = ConfigLoader.Load(Path.Combine(_baseDir, "config.json"));
        if (warning is not null) Log.Warn(warning);
        _cfg = cfg;
    }

    void RebuildBuilder()
    {
        var (_, warning) = ConfigLoader.Load(Path.Combine(_baseDir, "config.json"));
        var warnings = Env.StartupWarnings();
        if (warning is not null) warnings.Add(warning);
        if (_hardware is null) warnings.Add("Hardware sensors failed to start (see log)");
        _builder = new SnapshotBuilder(_cfg, _hardware, _system, _disks, warnings);
    }

    void Publish(string json)
    {
        if (IsDisposed) return;
        try
        {
            BeginInvoke(() =>
            {
                if (_pageReady && !IsDisposed) _web.CoreWebView2?.PostWebMessageAsJson(json);
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
            await Task.Delay(500);
            await SaveScreenshotAsync(_screenshotPath);
            Close();
        }
    }

    void Reload()
    {
        LoadConfig();
        RebuildBuilder();
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
        if (_loop is not null)
        {
            _loop.Dispose();
            _loop.Completion.Wait(TimeSpan.FromSeconds(3));
        }
        _hardware?.Dispose();
        base.OnFormClosing(e);
    }
}
