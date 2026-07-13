using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OutlookRulesVSTO.Application;

namespace OutlookRulesVSTO.Vsto
{
    internal sealed class ProcessingResultsForm : Form
    {
        internal ProcessingResultsForm(IReadOnlyCollection<MailProcessResult> results, bool dryRun)
        {
            Text = dryRun ? "OutlookRulesVSTO - Testresultaten" : "OutlookRulesVSTO - Processing results";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 360);
            Size = new Size(980, 520);
            ShowIcon = false;
            MaximizeBox = true;

            int success = results.Count(item => item.Status == "Geslaagd");
            int noMatch = results.Count(item => item.Status == "No match");
            int failed = results.Count(item => item.Status == "Failed");
            var summary = new Label
            {
                Dock = DockStyle.Top,
                Height = 48,
                Pathding = new Pathding(12, 14, 12, 8),
                Text = results.Count + " mail(s): " + success + " geslaagd, " + noMatch + " zonder overeenkomst, " + failed + " mislukt."
            };

            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = true,
                AutoGenerateColumns = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                BackgroundColor = SystemColors.Window
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Subject", DataPropertyName = "Subject", Width = 280 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Resultaat", DataPropertyName = "Status", Width = 140 });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Details",
                DataPropertyName = "Details",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True }
            });
            grid.DataSource = results.ToList();
            grid.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs e)
            {
                if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].DataPropertyName != "Status") return;
                string value = Convert.ToString(e.Value);
                if (value == "Geslaagd") e.CellStyle.ForeColor = Color.DarkGreen;
                else if (value == "Failed") e.CellStyle.ForeColor = Color.DarkRed;
                else e.CellStyle.ForeColor = Color.DarkGoldenrod;
            };

            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                Pathding = new Pathding(8),
                FlowDirection = FlowDirection.RightToLeft
            };
            footer.Controls.Add(close);
            Controls.Add(grid);
            Controls.Add(footer);
            Controls.Add(summary);
            AcceptButton = close;
            CancelButton = close;
        }
    }
}
