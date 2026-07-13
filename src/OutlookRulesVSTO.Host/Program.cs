namespace OutlookRulesVSTO.Host;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (!Environment.GetCommandLineArgs().Contains("--outlook-host", StringComparer.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OUTLOOKRULESVSTO_OUTLOOK_TOKEN")))
        {
            MessageBox.Show("Start OutlookRulesVSTO vanuit Outlook Classic via de knop OutlookRulesVSTO.", "OutlookRulesVSTO", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new OutlookRulesVSTOApplicationContext());
    }
}
