using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Office = Microsoft.Office.Core;

namespace OutlookRulesVSTO.Vsto
{
    [ComVisible(true)]
    public sealed class OutlookRulesVSTORibbon : Office.IRibbonExtensibility
    {
        private readonly ThisAddIn addIn;

        internal OutlookRulesVSTORibbon(ThisAddIn addIn) { this.addIn = addIn; }

        public string GetCustomUI(string ribbonId)
        {
            return @"<customUI xmlns='http://schemas.microsoft.com/office/2009/07/customui'>
  <ribbon><tabs><tab idMso='TabMail'>
    <group id='OutlookRulesVSTOGroup' label='OutlookRulesVSTO'>
      <button id='OutlookRulesVSTOOpen' label='OutlookRulesVSTO openen' size='large' imageMso='FileOpen' onAction='OnOpen'/>
      <button id='OutlookRulesVSTOAi' label='AI rule from selection' size='large' imageMso='CreateMailRule' onAction='OnAiRule'/>
      <button id='OutlookRulesVSTODryRun' label='Test selectie' size='large' imageMso='ReviewShowMarkup' onAction='OnDryRun'/>
      <button id='OutlookRulesVSTOProcess' label='Verwerk selectie' size='large' imageMso='FileSaveAs' onAction='OnProcess'/>
    </group>
  </tab></tabs></ribbon>
</customUI>";
        }

        public void OnDryRun(Office.IRibbonControl control) { Run(true); }
        public void OnProcess(Office.IRibbonControl control) { Run(false); }
        public async void OnOpen(Office.IRibbonControl control)
        {
            try { await addIn.OpenOutlookRulesVSTOAsync(); }
            catch (Exception ex) { ShowError(ex); }
        }
        public async void OnAiRule(Office.IRibbonControl control)
        {
            try { await addIn.CreateAiRuleFromSelectionAsync(); }
            catch (Exception ex) { ShowError(ex); }
        }

        private void Run(bool dryRun)
        {
            try
            {
                var results = addIn.ProcessSelected(dryRun);
                using (var dialog = new ProcessingResultsForm(results, dryRun)) dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                AddInLog.Write("RibbonActionFailed", "Error", "Handmatige actie mislukt", ex);
                MessageBox.Show(ex.Message, "OutlookRulesVSTO", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void ShowError(Exception ex)
        {
            AddInLog.Write("RibbonActionFailed", "Error", "Outlook-integratie mislukt", ex);
            MessageBox.Show(ex.Message, "OutlookRulesVSTO", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
