namespace OutlookRulesVSTO.Host;

internal sealed class OutlookRulesVSTOApplicationContext : ApplicationContext
{
    private readonly StartupDialog _splash = new();
    private HostMainForm? _mainForm;

    public OutlookRulesVSTOApplicationContext()
    {
        _splash.Shown += StartApplicationAsync;
        _splash.Show();
    }

    private async void StartApplicationAsync(object? sender, EventArgs eventArgs)
    {
        await Task.Yield(); // Paint the splash before any startup work begins.
        _mainForm = new HostMainForm();
        try
        {
            await _mainForm.InitializeForStartupAsync(_splash.SetStatus);
        }
        catch (Exception ex)
        {
            _mainForm.ShowStartupError(ex.Message);
        }

        MainForm = _mainForm;
        _mainForm.FormClosed += (_, _) => ExitThread();
        _splash.Close();
        _mainForm.Show();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _splash.Dispose();
        base.Dispose(disposing);
    }
}
