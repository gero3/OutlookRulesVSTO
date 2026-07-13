using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using WixToolset.Dtf.WindowsInstaller;
using Microsoft.Win32;

namespace OutlookRulesVSTO.InstallerActions
{
    public static class CustomActions
    {
        private const string AddInKey = @"Software\Microsoft\Office\Outlook\Addins\OutlookRulesVSTO.Vsto";
        [CustomAction]
        public static ActionResult Install(Session session)
        {
            try
            {
                EnsureOutlookClosed();
                string root = RequiredRoot(session);
                string runtime = GetOutlookRuntime() ?? throw new InvalidOperationException("Outlook Classic was not found. OutlookRulesVSTO can only be started through Outlook Classic.");
                InstallCertificate(Path.Combine(root, "Vsto", "Publisher.cer"));
                RegisterEngine(root, runtime, false);
                RegisterAddIn(root, runtime);
                session.Log("OutlookRulesVSTO Outlook integration installed.");
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log("OutlookRulesVSTO install custom action failed: " + ex);
                return ActionResult.Failure;
            }
        }

        [CustomAction]
        public static ActionResult RollbackInstall(Session session)
        {
            try
            {
                string root = RequiredRoot(session);
                bool preserveExisting;
                try { preserveExisting = !string.IsNullOrWhiteSpace(session.CustomActionData["PRESERVEEXISTING"]); }
                catch { preserveExisting = false; }
                if (preserveExisting)
                {
                    string runtime = GetOutlookRuntime() ?? throw new InvalidOperationException("Outlook Classic was not found during rollback.");
                    InstallCertificate(Path.Combine(root, "Vsto", "Publisher.cer"));
                    RegisterEngine(root, runtime, false);
                    RegisterAddIn(root, runtime);
                }
                else CleanupIntegration(root, session, false);
            }
            catch (Exception ex) { session.Log("OutlookRulesVSTO rollback warning: " + ex); }
            return ActionResult.Success;
        }

        [CustomAction]
        public static ActionResult Uninstall(Session session)
        {
            try
            {
                EnsureOutlookClosed();
                string root = RequiredRoot(session);
                CleanupIntegration(root, session, true);
                session.Log("OutlookRulesVSTO Outlook integration removed.");
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log("OutlookRulesVSTO uninstall custom action warning: " + ex);
                return ActionResult.Failure;
            }
        }

        private static string RequiredRoot(Session session)
        {
            string root = session.CustomActionData["INSTALLFOLDER"];
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("INSTALLFOLDER is missing from CustomActionData.");
            return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void EnsureOutlookClosed()
        {
            if (Process.GetProcessesByName("OUTLOOK").Length > 0)
                throw new InvalidOperationException("Sluit Outlook Classic volledig en probeer de installatie opnieuw.");
        }

        private static string? GetOutlookRuntime()
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey? configuration = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Office\ClickToRun\Configuration"))
            {
                string platform = Convert.ToString(configuration?.GetValue("Platform")) ?? string.Empty;
                if (platform.IndexOf("x86", StringComparison.OrdinalIgnoreCase) >= 0) return "win-x86";
                if (platform.IndexOf("x64", StringComparison.OrdinalIgnoreCase) >= 0) return "win-x64";
            }
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                {
                    foreach (string version in new[] { "16.0", "15.0", "14.0" })
                    using (RegistryKey? outlook = baseKey.OpenSubKey($@"SOFTWARE\Microsoft\Office\{version}\Outlook"))
                    {
                        string bitness = Convert.ToString(outlook?.GetValue("Bitness")) ?? string.Empty;
                        if (bitness.IndexOf("x86", StringComparison.OrdinalIgnoreCase) >= 0) return "win-x86";
                        if (bitness.IndexOf("x64", StringComparison.OrdinalIgnoreCase) >= 0) return "win-x64";
                    }
                }
            }
            return null;
        }

        private static void RegisterEngine(string root, string runtime, bool uninstall)
        {
            string comHost = Path.Combine(root, "Bridge", runtime, "OutlookRulesVSTO.Bridge.comhost.dll");
            if (!File.Exists(comHost)) throw new FileNotFoundException("OutlookRulesVSTO COM engine is missing.", comHost);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string systemDirectory = runtime == "win-x86"
                ? Path.Combine(windows, "SysWOW64")
                : Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                    ? Path.Combine(windows, "Sysnative")
                    : Path.Combine(windows, "System32");
            string regsvr = Path.Combine(systemDirectory, "regsvr32.exe");
            string arguments = "/s " + (uninstall ? "/u " : string.Empty) + "\"" + comHost + "\"";
            using (Process process = Process.Start(new ProcessStartInfo(regsvr, arguments) { UseShellExecute = false, CreateNoWindow = true })
                ?? throw new InvalidOperationException("regsvr32 could not be started."))
            {
                if (!process.WaitForExit(30_000))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("COM registration did not respond within 30 seconds.");
                }
                if (process.ExitCode != 0) throw new InvalidOperationException("COM-registratie is mislukt met code " + process.ExitCode + ".");
            }
        }

        private static void RegisterAddIn(string root, string runtime)
        {
            string manifest = new Uri(Path.Combine(root, "Vsto", "OutlookRulesVSTO.Vsto.vsto")).AbsoluteUri + "|vstolocal";
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, runtime == "win-x86" ? RegistryView.Registry32 : RegistryView.Registry64))
            using (RegistryKey key = baseKey.CreateSubKey(AddInKey)!)
            {
                key.SetValue("FriendlyName", "OutlookRulesVSTO Outlook-integratie", RegistryValueKind.String);
                key.SetValue("Description", "Processes Outlook attachments automatically with OutlookRulesVSTO.", RegistryValueKind.String);
                key.SetValue("Manifest", manifest, RegistryValueKind.String);
                key.SetValue("LoadBehavior", 3, RegistryValueKind.DWord);
            }
        }

        private static void InstallCertificate(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("OutlookRulesVSTO publisher certificate is missing.", path);
            X509Certificate2 certificate = new X509Certificate2(path);
            AddToStore(certificate, StoreName.TrustedPublisher);
            AddToStore(certificate, StoreName.Root);
        }

        private static void AddToStore(X509Certificate2 certificate, StoreName name)
        {
            using (X509Store store = new X509Store(name, StoreLocation.LocalMachine))
            {
                store.Open(OpenFlags.ReadWrite);
                store.Add(certificate);
            }
        }

        private static void RemoveCertificate(string path)
        {
            if (!File.Exists(path)) return;
            string thumbprint = new X509Certificate2(path).Thumbprint;
            RemoveFromStore(thumbprint, StoreName.TrustedPublisher);
            RemoveFromStore(thumbprint, StoreName.Root);
        }

        private static void RemoveFromStore(string thumbprint, StoreName name)
        {
            using (X509Store store = new X509Store(name, StoreLocation.LocalMachine))
            {
                store.Open(OpenFlags.ReadWrite);
                foreach (X509Certificate2 certificate in store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false)) store.Remove(certificate);
            }
        }

        private static void CleanupIntegration(string root, Session session, bool throwOnFailure)
        {
            var errors = new List<Exception>();
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    {
                        baseKey.DeleteSubKeyTree(AddInKey, false);
                    }
                }
                catch (Exception ex) { errors.Add(ex); }
            }
            foreach (string runtime in new[] { "win-x64", "win-x86" })
            {
                try
                {
                    string host = Path.Combine(root, "Bridge", runtime, "OutlookRulesVSTO.Bridge.comhost.dll");
                    if (File.Exists(host)) RegisterEngine(root, runtime, true);
                }
                catch (Exception ex) { errors.Add(ex); }
            }
            try { RemoveCertificate(Path.Combine(root, "Vsto", "Publisher.cer")); }
            catch (Exception ex) { errors.Add(ex); }
            foreach (Exception error in errors) session.Log("OutlookRulesVSTO cleanup warning: " + error);
            if (throwOnFailure && errors.Count > 0)
                throw new AggregateException("OutlookRulesVSTO integration could not be fully removed.", errors);
        }
    }
}
