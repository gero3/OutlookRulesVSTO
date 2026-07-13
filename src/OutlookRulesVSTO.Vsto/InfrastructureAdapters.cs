using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using OutlookRulesVSTO.Application;

namespace OutlookRulesVSTO.Vsto
{
    internal sealed class SystemFileSystem : IFileSystem
    {
        public bool FileExists(string path) => File.Exists(path);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public string ReadAllText(string path) => File.ReadAllText(path);
    }

    internal sealed class ComRuleEngine : IRuleEngine
    {
        private readonly IVstoBridgeRuleEngine engine;
        private readonly Dictionary<MailSnapshot, IVstoBridgeMessage> messages = new Dictionary<MailSnapshot, IVstoBridgeMessage>();

        internal ComRuleEngine()
        {
            Type type = Type.GetTypeFromProgID("OutlookRulesVSTO.RuleEngine", true);
            engine = (IVstoBridgeRuleEngine)Activator.CreateInstance(type);
        }

        public RuleEvaluation Evaluate(string rulesPath, string settingsPath, MailSnapshot message, bool dryRun)
        {
            IVstoBridgeMessage request = engine.CreateMessage();
            request.EntryId = message.EntryId; request.SenderEmail = message.SenderEmail; request.SenderName = message.SenderName;
            request.Subject = message.Subject; request.BodyText = message.BodyText; request.BodyHtml = message.BodyHtml; request.ReceivedTime = message.ReceivedTime;
            foreach (MailAttachment attachment in message.Attachments)
                request.AddAttachment(attachment.Index, attachment.Name, attachment.SizeKb, attachment.IsInline);
            messages[message] = request;
            IVstoBridgeEvaluation result = engine.EvaluateFromFiles(rulesPath, settingsPath, request, dryRun);
            var plans = new List<SavePlan>();
            for (int i = 1; i <= result.PlanCount; i++)
            {
                IVstoBridgeSavePlan plan = result.GetPlan(i);
                plans.Add(new SavePlan { RuleName = plan.RuleName, AttachmentIndex = plan.AttachmentIndex, SaveMode = plan.SaveMode,
                    DestinationPath = plan.DestinationPath, Status = plan.Status, Message = plan.Message, NativePlan = plan });
            }
            return new RuleEvaluation { Summary = result.Summary, Plans = plans };
        }

        public void RecordSaveResult(string settingsPath, MailSnapshot message, SavePlan plan, string status, string text, string errorNumber, string errorDescription) =>
            engine.RecordSaveResultFromFile(settingsPath, messages[message], (IVstoBridgeSavePlan)plan.NativePlan, status, text, errorNumber, errorDescription);

        public void RecordSummary(string settingsPath, MailSnapshot message, bool dryRun, int planCount, int succeededCount, int failedCount)
        {
            engine.RecordActionSummaryFromFile(settingsPath, messages[message], dryRun, planCount, succeededCount, failedCount);
            messages.Remove(message);
        }
    }

    internal sealed class DesktopGateway : IDesktopGateway
    {
        private readonly string token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        private Process process;

        public async Task OpenAsync()
        {
            await EnsureStartedAsync();
            if (process != null && !process.HasExited) try { process.WaitForInputIdle(3000); } catch { }
        }

        public async Task CreateAiRuleAsync(MailSnapshot mail)
        {
            await EnsureStartedAsync();
            var attachments = new object[mail.Attachments.Count];
            for (int i = 0; i < attachments.Length; i++) attachments[i] = new { name = mail.Attachments[i].Name, sizeKb = Math.Max(1, mail.Attachments[i].SizeKb) };
            var payload = new { messageId = mail.EntryId, senderEmail = mail.SenderEmail, senderName = mail.SenderName, subject = mail.Subject,
                bodyText = mail.BodyText, bodyHtml = mail.BodyHtml, receivedTime = mail.ReceivedTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), attachments };
            string json = new JavaScriptSerializer().Serialize(payload);
            using (var client = new HttpClient())
            using (var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:5187/api/outlook/ai-rule"))
            {
                request.Headers.Add("X-OutlookRulesVSTO-Outlook-Token", token);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.SendAsync(request);
                string detail = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException("OutlookRulesVSTO kon de geselecteerde mail niet overnemen: " + ErrorDetail(detail, (int)response.StatusCode));
            }
        }

        private async Task EnsureStartedAsync()
        {
            if (process != null && !process.HasExited) return;
            string root = FindInstallRoot();
            var start = new ProcessStartInfo(Path.Combine(root, "OutlookRulesVSTO.Host.exe"), "--outlook-host") { WorkingDirectory = root, UseShellExecute = false };
            start.EnvironmentVariables["OUTLOOKRULESVSTO_OUTLOOK_TOKEN"] = token;
            process = Process.Start(start) ?? throw new InvalidOperationException("OutlookRulesVSTO could not be started.");
            using (var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(700) })
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    if (process.HasExited) throw new InvalidOperationException("OutlookRulesVSTO stopped during startup.");
                    try { if ((await client.GetAsync("http://127.0.0.1:5187/")).IsSuccessStatusCode) return; } catch { }
                    await Task.Delay(200);
                }
            throw new TimeoutException("OutlookRulesVSTO did not respond within the expected time.");
        }

        private static string ErrorDetail(string detail, int statusCode)
        {
            try { var error = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(detail); if (error != null && error.ContainsKey("message")) detail = Convert.ToString(error["message"]); } catch { }
            return string.IsNullOrWhiteSpace(detail) ? "The local OutlookRulesVSTO service returned error code " + statusCode + "." : detail;
        }

        private static string FindInstallRoot()
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                try
                {
                    using (RegistryKey key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(@"Software\Microsoft\Office\Outlook\Addins\OutlookRulesVSTO.Vsto"))
                    {
                        string root = InstallRootFromManifest(key == null ? null : key.GetValue("Manifest") as string);
                        if (root != null) return root;
                    }
                }
                catch { }
            foreach (string start in new[] { Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), AppDomain.CurrentDomain.BaseDirectory, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OutlookRulesVSTO", "Vsto") })
                for (DirectoryInfo directory = string.IsNullOrWhiteSpace(start) ? null : new DirectoryInfo(start); directory != null; directory = directory.Parent)
                    if (File.Exists(Path.Combine(directory.FullName, "OutlookRulesVSTO.Host.exe"))) return directory.FullName;
            throw new FileNotFoundException("The internal OutlookRulesVSTO interface was not found next to the Outlook add-in.");
        }

        private static string InstallRootFromManifest(string manifest)
        {
            if (string.IsNullOrWhiteSpace(manifest)) return null;
            string location = manifest.Split('|')[0]; Uri uri;
            if (!Uri.TryCreate(location, UriKind.Absolute, out uri) || !uri.IsFile) return null;
            DirectoryInfo directory = Directory.GetParent(Path.GetDirectoryName(uri.LocalPath));
            return directory != null && File.Exists(Path.Combine(directory.FullName, "OutlookRulesVSTO.Host.exe")) ? directory.FullName : null;
        }

        public void Dispose()
        {
            try { if (process != null && !process.HasExited) { process.CloseMainWindow(); if (!process.WaitForExit(3000)) process.Kill(); } } catch { }
            if (process != null) process.Dispose(); process = null;
        }
    }
}
