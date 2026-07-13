namespace OutlookRulesVSTO.Host;

internal sealed class StartupDialog : Form
{
    private readonly Label _status = new()
    {
        Dock = DockStyle.Top, Height = 52, Text = "Preparing OutlookRulesVSTO...", TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 10.5f), ForeColor = Color.FromArgb(175, 195, 207)
    };
    private readonly ProgressBar _progress = new()
    {
        Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 28, Width = 310, Height = 5
    };
    private readonly Button _close = new()
    {
        Text = "Close", Width = 96, Height = 34, Visible = false, FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(18, 52, 77), ForeColor = Color.FromArgb(241, 247, 250)
    };

    public StartupDialog()
    {
        Text = "OutlookRulesVSTO starten";
        ClientSize = new Size(440, 240);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        ControlBox = false;
        BackColor = Color.FromArgb(6, 31, 49);

        var brand = new Label
        {
            Dock = DockStyle.Top, Height = 75, Text = "OutlookRulesVSTO", TextAlign = ContentAlignment.BottomCenter,
            Font = new Font("Segoe UI", 23, FontStyle.Bold | FontStyle.Italic), ForeColor = Color.FromArgb(241, 247, 250)
        };
        var accent = new Label
        {
            Dock = DockStyle.Top, Height = 26, Text = "OUTLOOK AUTOMATION", TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = Color.FromArgb(35, 211, 198)
        };
        var progressHost = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 24, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Pathding = new Pathding(61, 8, 0, 0), BackColor = BackColor
        };
        progressHost.Controls.Add(_progress);
        var closeHost = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Pathding = new Pathding(168, 7, 0, 0), BackColor = BackColor
        };
        _close.FlatAppearance.BorderColor = Color.FromArgb(43, 83, 108);
        _close.Click += (_, _) => DialogResult = DialogResult.Cancel;
        closeHost.Controls.Add(_close);
        Controls.Add(closeHost);
        Controls.Add(progressHost);
        Controls.Add(_status);
        Controls.Add(accent);
        Controls.Add(brand);
    }

    public void SetStatus(string text)
    {
        _status.Text = text;
        _status.ForeColor = Color.FromArgb(175, 195, 207);
    }

    public void ShowError(string message)
    {
        _progress.Visible = false;
        _status.Height = 76;
        _status.Text = $"OutlookRulesVSTO could not start.\r\n{message}";
        _status.ForeColor = Color.FromArgb(255, 112, 112);
        _close.Visible = true;
        ControlBox = true;
    }
}
