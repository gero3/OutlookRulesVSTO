using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OutlookRulesVSTO.Application;
using Office = Microsoft.Office.Core;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace OutlookRulesVSTO.Vsto
{
    // VSTO composition root and event adapter. All behavior is injected into OutlookRulesController.
    public partial class ThisAddIn
    {
        private Outlook.MAPIFolder inboxFolder;
        private Outlook.Items inboxItems;
        private Outlook.ApplicationEvents_11_Event applicationEvents;
        private OutlookRulesController controller;

        private void ThisAddIn_Startup(object sender, EventArgs e)
        {
            try
            {
                string settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OutlookRulesVSTO", "settings.xml");
                controller = new OutlookRulesController(new MailProcessingService(settings, new SystemFileSystem(), new ComRuleEngine()), new DesktopGateway(), new AddInApplicationLog());
                inboxFolder = Application.Session.GetDefaultFolder(Outlook.OlDefaultFolders.olFolderInbox);
                inboxItems = inboxFolder.Items;
                inboxItems.ItemAdd += InboxItems_ItemAdd;
                applicationEvents = (Outlook.ApplicationEvents_11_Event)Application;
                applicationEvents.Quit += Application_Quit;
                AddInLog.Write("AddInStartup", "Success", "OutlookRulesVSTO VSTO add-in gestart", null);
            }
            catch (Exception ex) { AddInLog.Write("AddInStartup", "Error", "OutlookRulesVSTO VSTO add-in could not start", ex); }
        }

        private void InboxItems_ItemAdd(object item)
        {
            Outlook.MailItem mail = item as Outlook.MailItem;
            if (mail == null) return;
            try { controller.ProcessIncoming(new OutlookMailMessage(mail)); }
            catch (Exception ex) { AddInLog.WriteMail("ItemAddFailed", mail, "Error", "Automatic processing failed", ex); }
        }

        internal IReadOnlyList<MailProcessResult> ProcessSelected(bool dryRun) => controller.ProcessSelected(SelectedMessages(), dryRun);
        internal Task OpenOutlookRulesVSTOAsync() => controller.OpenAsync();
        internal Task CreateAiRuleFromSelectionAsync() => controller.CreateAiRuleAsync(SelectedMessages().FirstOrDefault());

        private IEnumerable<IMailMessage> SelectedMessages()
        {
            Outlook.Explorer explorer = Application.ActiveExplorer();
            if (explorer == null) yield break;
            Outlook.Selection selection = explorer.Selection;
            for (int i = 1; i <= selection.Count; i++)
            {
                Outlook.MailItem mail = selection[i] as Outlook.MailItem;
                if (mail != null) yield return new OutlookMailMessage(mail);
            }
        }

        private void Application_Quit() => controller?.Dispose();

        protected override Office.IRibbonExtensibility CreateRibbonExtensibilityObject() => new OutlookRulesVSTORibbon(this);

        private void ThisAddIn_Shutdown(object sender, EventArgs e)
        {
            if (inboxItems != null) inboxItems.ItemAdd -= InboxItems_ItemAdd;
            if (applicationEvents != null) applicationEvents.Quit -= Application_Quit;
            applicationEvents = null;
            if (inboxItems != null) Marshal.FinalReleaseComObject(inboxItems);
            if (inboxFolder != null) Marshal.FinalReleaseComObject(inboxFolder);
            inboxItems = null; inboxFolder = null;
            controller?.Dispose(); controller = null;
        }

        #region VSTO generated code
        private void InternalStartup()
        {
            Startup += new EventHandler(ThisAddIn_Startup);
            Shutdown += new EventHandler(ThisAddIn_Shutdown);
        }
        #endregion
    }
}
