using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace OutlookRulesVSTO.Host;

internal sealed class HostMainForm : Form
{
    private const string AppUrl = "http://127.0.0.1:5187/";
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Panel _loading = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(6, 31, 49) };
    private readonly Label _status = new()
    {
        AutoSize = false, Dock = DockStyle.Fill, Text = "OutlookRulesVSTO starten…", TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 13, FontStyle.Bold), ForeColor = Color.FromArgb(241, 247, 250)
    };
    private readonly FlowLayoutPanel _errorActions = new()
    {
        Dock = DockStyle.Bottom, Height = 62, Visible = false, FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false, Pathding = new Pathding(0, 8, 0, 0), BackColor = Color.FromArgb(6, 31, 49)
    };
    private Process? _ownedServer;
    private readonly ChildProcessJob _childProcesses = new();
    private bool _webViewConfigured;

    public HostMainForm()
    {
        Text = "OutlookRulesVSTO";
        Width = 1280;
        Height = 820;
        MinimumSize = new Size(860, 600);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(6, 31, 49);
        var retryButton = ErrorButton("Opnieuw proberen", primary: true);
        var closeButton = ErrorButton("Close");
        retryButton.Click += async (_, _) => await RetryFromMainAsync();
        closeButton.Click += (_, _) => Close();
        _errorActions.Controls.Add(retryButton);
        _errorActions.Controls.Add(closeButton);
        _errorActions.Resize += (_, _) => _errorActions.Pathding = new Pathding(Math.Max(0, (_errorActions.ClientSize.Width - 220) / 2), 8, 0, 0);
        _loading.Controls.Add(_status);
        _loading.Controls.Add(_errorActions);
        Controls.Add(_webView);
        Controls.Add(_loading);
        FormClosed += (_, _) => { StopOwnedServer(); _childProcesses.Dispose(); };
    }

    private void ShowStartupDialog()
    {
        _errorActions.Visible = false;
        _status.ForeColor = Color.FromArgb(241, 247, 250);
        _status.Text = "OutlookRulesVSTO starten...";
        using var dialog = new StartupDialog();
        dialog.Shown += async (_, _) =>
        {
            await Task.Yield(); // Paint the modal before starting the child process.
            try
            {
                await InitializeForStartupAsync(dialog.SetStatus);
                dialog.DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                ShowMainError(ex.Message);
                dialog.DialogResult = DialogResult.Abort;
            }
        };
        dialog.ShowDialog(this);
    }

    private void ShowMainError(string message)
    {
        _loading.Visible = true;
        _status.ForeColor = Color.FromArgb(255, 112, 112);
        _status.Text = $"OutlookRulesVSTO could not start.\r\n\r\n{message}";
        _errorActions.Visible = true;
    }

    internal void ShowStartupError(string message) => ShowMainError(message);

    private async Task RetryFromMainAsync()
    {
        _errorActions.Visible = false;
        _status.ForeColor = Color.FromArgb(241, 247, 250);
        try { await InitializeForStartupAsync(text => _status.Text = text); }
        catch (Exception ex) { ShowMainError(ex.Message); }
    }

    private static Button ErrorButton(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text, Width = 104, Height = 36, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            BackColor = primary ? Color.FromArgb(35, 211, 198) : Color.FromArgb(18, 52, 77),
            ForeColor = primary ? Color.FromArgb(6, 31, 49) : Color.FromArgb(241, 247, 250),
            Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Pathding(3)
        };
        button.FlatAppearance.BorderColor = primary ? Color.FromArgb(35, 211, 198) : Color.FromArgb(43, 83, 108);
        return button;
    }

    internal async Task InitializeForStartupAsync(Action<string> setStatus)
    {
        setStatus("Controleren of OutlookRulesVSTO al actief is...");
        if (!await ServerRespondsAsync())
        {
            setStatus("OutlookRulesVSTO-subproces starten...");
            _ownedServer = StartServer();
            setStatus("Wachten tot het subproces klaar is...");
            await WaitForServerAsync(_ownedServer);
        }

        setStatus("Preparing interface...");
        var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OutlookRulesVSTO", "WebView2");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
        await _webView.EnsureCoreWebView2Async(environment);
        if (!_webViewConfigured)
        {
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            _webView.CoreWebView2.NewWindowRequested += (_, eventArgs) =>
            {
                eventArgs.Handled = true;
                if (Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            };
            _webViewConfigured = true;
        }
        setStatus("OutlookRulesVSTO laden...");
        var navigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
        {
            if (eventArgs.IsSuccess) navigation.TrySetResult();
            else navigation.TrySetException(new InvalidOperationException($"The home page could not be loaded ({eventArgs.WebErrorStatus})."));
        }
        _webView.NavigationCompleted += NavigationCompleted;
        try
        {
            _webView.Source = new Uri(AppUrl);
            await navigation.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally { _webView.NavigationCompleted -= NavigationCompleted; }
        _webView.Visible = true;
        _loading.Visible = false;
    }

    private async Task InitializeAsync()
    {
        try
        {
            _status.Text = "OutlookRulesVSTO-onderdeel starten…";
            if (!await ServerRespondsAsync())
            {
                _ownedServer = StartServer();
                await WaitForServerAsync(_ownedServer);
            }

            _status.Text = "Interface laden…";
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OutlookRulesVSTO", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await _webView.EnsureCoreWebView2Async(environment);
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            _webView.CoreWebView2.NewWindowRequested += (_, eventArgs) =>
            {
                eventArgs.Handled = true;
                if (Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            };
            _webView.Source = new Uri(AppUrl);
            _webView.Visible = true;
            _loading.Visible = false;
        }
        catch (Exception ex)
        {
            _status.Text = $"OutlookRulesVSTO could not start.\r\n\r\n{ex.Message}\r\n\r\nClose the window and try again.";
        }
    }

    private static async Task<bool> ServerRespondsAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(600) };
        try { return (await client.GetAsync(AppUrl)).IsSuccessStatusCode; }
        catch { return false; }
    }

    private Process StartServer()
    {
        var executable = FindWebExecutable();
        if (executable is null)
            throw new FileNotFoundException("The local OutlookRulesVSTO web service was not found. Build the Release project first.");
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = FindWebWorkingDirectory(executable),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        // Launching through Windows Shell can lose the environment inherited
        // from Outlook. Pass the private bridge token to the child explicitly.
        var outlookToken = Environment.GetEnvironmentVariable("OUTLOOKRULESVSTO_OUTLOOK_TOKEN");
        if (!string.IsNullOrWhiteSpace(outlookToken))
            startInfo.Environment["OUTLOOKRULESVSTO_OUTLOOK_TOKEN"] = outlookToken;
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The local OutlookRulesVSTO subprocess could not be started.");
        try { _childProcesses.Add(process); return process; }
        catch { process.Kill(entireProcessTree: true); process.Dispose(); throw; }
    }

    private static string FindWebWorkingDirectory(string executable)
    {
        var executableDirectory = Path.GetDirectoryName(executable)!;
        if (Directory.Exists(Path.Combine(executableDirectory, "wwwroot"))) return executableDirectory;

        // Build output does not contain the static files physically. ASP.NET must
        // use the web project as content root when the desktop app is started by F5.
        var directory = new DirectoryInfo(executableDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OutlookRulesVSTO.Web.csproj")) &&
                Directory.Exists(Path.Combine(directory.FullName, "wwwroot")))
                return directory.FullName;
            directory = directory.Parent;
        }

        return executableDirectory;
    }

    private static string? FindWebExecutable()
    {
        var overridePath = Environment.GetEnvironmentVariable("OUTLOOKRULESVSTO_WEB_EXE");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath)) return overridePath;

        // A published installation always carries its private backend in Web.
        var publishedWeb = Path.Combine(AppContext.BaseDirectory, "Web", "OutlookRulesVSTO.Web.exe");
        if (File.Exists(publishedWeb)) return publishedWeb;
        var sibling = Path.Combine(AppContext.BaseDirectory, "OutlookRulesVSTO.Web.exe");
        if (File.Exists(sibling) && File.Exists(Path.ChangeExtension(sibling, ".dll"))) return sibling;

        // During an F5 run, use the same build configuration as the desktop app.
        // Fall back to the other common configuration for command-line builds.
        var configurations = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Debug", "Release" }
            : new[] { "Release", "Debug" };
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            foreach (var configuration in configurations)
            {
                var candidate = Path.Combine(directory.FullName, "src", "OutlookRulesVSTO.Web", "bin", configuration, "net10.0", "OutlookRulesVSTO.Web.exe");
                if (File.Exists(candidate)) return candidate;
            }
            directory = directory.Parent;
        }
        return null;
    }

    private static async Task WaitForServerAsync(Process server)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (await ServerRespondsAsync()) return;
            server.Refresh();
            if (server.HasExited) throw new InvalidOperationException($"The local subprocess stopped during startup (code {server.ExitCode}).");
            await Task.Delay(200);
        }
        throw new TimeoutException("The local subprocess did not respond within the expected time.");
    }

    private void StopOwnedServer()
    {
        try { if (_ownedServer is { HasExited: false }) _ownedServer.Kill(entireProcessTree: true); }
        catch { /* The process may already have stopped. */ }
        _ownedServer?.Dispose();
    }
}
