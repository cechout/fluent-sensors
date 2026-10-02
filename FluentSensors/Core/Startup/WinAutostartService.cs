using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;

using FluentSensors.Common;


namespace FluentSensors.Core.Startup
{
    // autostart:
    // the scheduled task that starts the app at sign-in; not an HKCU Run entry, which under requireAdministrator fails
    // with ERROR_ELEVATION_REQUIRED (740) instead of prompting
    // HighestAvailable with InteractiveToken starts elevated without a prompt or a stored password, only
    // while the user is signed in
    public static class WinAutostartService
    {
        // === fields ===

        private const string TaskName = "FluentSensors";

        // the delayed start; the sign-in settles before discovery and the driver load compete with it
        private const string StartupDelay = "PT30S";

        // marks a sign-in launch, the only one start minimized applies to
        private const string AutostartArgument = "--autostart";


        // === public api ===

        // not for a portable copy, the task would outlive a deleted folder
        // a packaged build keeps the task too: the StartupTask extension activates with the normal token, which
        // requireAdministrator refuses; (RapidDev.Radiograph ships this shape from the store)
        public static bool IsSupported => !AppDistribution.IsPortableBuild;

        // started by the task, not by the user
        public static bool StartedByTask { get; } = HasAutostartArgument();

        // reads the task, so one removed by hand shows as off
        public static bool IsEnabled()
        {
            if (!IsSupported) return false;

            return ReadTaskXml() != null;
        }

        // the task differs from what this build writes: another exe (a moved or reinstalled copy), or no
        // autostart argument (an older task)
        public static bool IsStale()
        {
            string? xml = ReadTaskXml();
            if (string.IsNullOrEmpty(xml)) return false;

            if (!string.Equals(ReadElement(xml, "Command"), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !ReadElement(xml, "Arguments").Contains(AutostartArgument, StringComparison.OrdinalIgnoreCase);
        }

        // rewrites a stale task, nothing otherwise; (two schtasks processes, so off the UI thread)
        public static void RepairIfStale(bool delayed)
        {
            if (!IsSupported) return;
            if (!IsStale()) return;

            CreateTask(delayed);
        }

        // creates, rewrites or removes the task; false when the task scheduler refused, so the toggle goes back
        public static bool Apply(bool enabled, bool delayed)
        {
            if (!IsSupported) return false;

            return enabled ? CreateTask(delayed) : DeleteTask();
        }


        // === private helpers ===

        // a full definition, since /SC ONLOGON cannot express these: DisallowStartIfOnBatteries and
        // StopIfGoingOnBatteries default to true (no start on battery, killed on unplugging),
        // ExecutionTimeLimit to three days
        private static bool CreateTask(bool delayed)
        {
            string? exePath = Environment.ProcessPath;
            string? workingDirectory = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(exePath) || string.IsNullOrEmpty(workingDirectory)) return false;

            string xmlPath = Path.Combine(Path.GetTempPath(), $"{TaskName}_task.xml");

            try
            {
                // UTF-16; schtasks /XML rejects UTF-8
                File.WriteAllText(xmlPath, BuildTaskXml(exePath, workingDirectory, delayed), Encoding.Unicode);

                return RunSchTasks("/Create", "/TN", TaskName, "/XML", xmlPath, "/F") == 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WinAutostartService] create failed: {ex.Message}");
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(xmlPath)) File.Delete(xmlPath);
                }
                catch { /* a leftover in the temp folder is harmless */ }
            }
        }

        private static bool DeleteTask()
        {
            // nothing to remove is success
            if (ReadTaskXml() == null) return true;

            return RunSchTasks("/Delete", "/TN", TaskName, "/F") == 0;
        }

        // the user the task runs as and whose sign-in triggers it
        //
        // KNOWN UNRELIABLE:
        // this is the elevated identity; a standard user who elevates with another administrator account ties the task
        // to that sign-in instead (right for the usual own-account case)
        private static string CurrentUserId() => $"{Environment.UserDomainName}\\{Environment.UserName}";

        private static string BuildTaskXml(string exePath, string workingDirectory, bool delayed)
        {
            string userId = SecurityElement.Escape(CurrentUserId());
            string command = SecurityElement.Escape(exePath);
            string directory = SecurityElement.Escape(workingDirectory);
            string arguments = SecurityElement.Escape(AutostartArgument);
            string delay = delayed ? $"      <Delay>{StartupDelay}</Delay>\n" : "";

            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
                   "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
                   "  <RegistrationInfo>\n" +
                   "    <Author>Fluent Sensors</Author>\n" +
                   "    <Description>Starts Fluent Sensors when you sign in</Description>\n" +
                   "  </RegistrationInfo>\n" +
                   "  <Triggers>\n" +
                   "    <LogonTrigger>\n" +
                   "      <Enabled>true</Enabled>\n" +
                   $"      <UserId>{userId}</UserId>\n" +
                   delay +
                   "    </LogonTrigger>\n" +
                   "  </Triggers>\n" +
                   "  <Principals>\n" +
                   "    <Principal id=\"Author\">\n" +
                   $"      <UserId>{userId}</UserId>\n" +
                   "      <LogonType>InteractiveToken</LogonType>\n" +
                   "      <RunLevel>HighestAvailable</RunLevel>\n" +
                   "    </Principal>\n" +
                   "  </Principals>\n" +
                   "  <Settings>\n" +
                   "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\n" +
                   "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n" +
                   "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n" +
                   "    <AllowHardTerminate>true</AllowHardTerminate>\n" +
                   "    <StartWhenAvailable>false</StartWhenAvailable>\n" +
                   "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\n" +
                   "    <IdleSettings>\n" +
                   "      <StopOnIdleEnd>false</StopOnIdleEnd>\n" +
                   "      <RestartOnIdle>false</RestartOnIdle>\n" +
                   "    </IdleSettings>\n" +
                   "    <AllowStartOnDemand>true</AllowStartOnDemand>\n" +
                   "    <Enabled>true</Enabled>\n" +
                   "    <Hidden>false</Hidden>\n" +
                   "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\n" +
                   "    <WakeToRun>false</WakeToRun>\n" +
                   "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\n" +
                   "    <Priority>7</Priority>\n" +
                   "  </Settings>\n" +
                   "  <Actions Context=\"Author\">\n" +
                   "    <Exec>\n" +
                   $"      <Command>{command}</Command>\n" +
                   $"      <Arguments>{arguments}</Arguments>\n" +
                   $"      <WorkingDirectory>{directory}</WorkingDirectory>\n" +
                   "    </Exec>\n" +
                   "  </Actions>\n" +
                   "</Task>\n";
        }

        // one element of the definition, or empty; flat enough to need no xml parser
        private static string ReadElement(string xml, string element)
        {
            string open = $"<{element}>";
            string close = $"</{element}>";

            int start = xml.IndexOf(open, StringComparison.Ordinal);
            if (start < 0) return "";
            start += open.Length;

            int end = xml.IndexOf(close, start, StringComparison.Ordinal);
            if (end < 0) return "";

            return xml.Substring(start, end - start).Trim();
        }

        private static bool HasAutostartArgument()
        {
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (string.Equals(argument, AutostartArgument, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        // null without a task (schtasks exits non-zero)
        private static string? ReadTaskXml()
        {
            if (!IsSupported) return null;

            string output = "";
            int exitCode = RunSchTasks(line => output += line, "/Query", "/TN", TaskName, "/XML", "ONE");

            return exitCode == 0 ? output : null;
        }

        private static int RunSchTasks(params string[] arguments) => RunSchTasks(null, arguments);

        // already elevated, so no further consent
        private static int RunSchTasks(Action<string>? collectOutput, params string[] arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                foreach (string argument in arguments)
                {
                    psi.ArgumentList.Add(argument);
                }

                using var process = Process.Start(psi);
                if (process == null) return -1;

                // no StandardOutputEncoding: schtasks declares UTF-16 but writes the console codepage, forcing Unicode
                // gives mojibake (measured); a non-ascii path may come back mangled, at worst a fine task is rewritten
                string output = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                if (!process.WaitForExit(10000)) return -1;

                collectOutput?.Invoke(output);

                return process.ExitCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WinAutostartService] schtasks failed: {ex.Message}");
                return -1;
            }
        }
    }
}
