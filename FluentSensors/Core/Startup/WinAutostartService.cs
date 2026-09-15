using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;

using FluentSensors.Common;


namespace FluentSensors.Core.Startup
{
    // registers and removes the scheduled task that starts the app when the user signs in
    //
    // a scheduled task rather than the usual HKCU\...\Run entry, because the app manifest asks for
    // requireAdministrator: a Run entry starts it with the non-elevated logon token, and CreateProcess then fails
    // with ERROR_ELEVATION_REQUIRED (740) instead of prompting
    // a task with RunLevel HighestAvailable and LogonType InteractiveToken starts elevated with no prompt, stores no
    // password, and only runs while that user is actually signed in, which is what a window needs
    public static class WinAutostartService
    {
        // === fields ===

        private const string TaskName = "FluentSensors";

        // the trigger delay for the delayed start option; long enough to let the sign-in settle before the sensor
        // discovery and the kernel driver load start competing with it
        private const string StartupDelay = "PT30S";


        // === public api ===

        // a task is a real system entry that outlives the app folder, so a portable copy deliberately does not offer
        // this; deleting the folder would leave a task pointing at nothing that nobody connects to this app anymore
        // a packaged build wants the StartupTask manifest extension instead, which the store controls
        public static bool IsSupported => !AppDistribution.IsPortableBuild && !AppDistribution.IsPackaged;

        // reads the task rather than a setting, so a task removed by hand in the task scheduler shows up as off
        public static bool IsEnabled()
        {
            if (!IsSupported) return false;

            return ReadTaskXml() != null;
        }

        // true when a task exists but points at a different exe than the one running, which is what a moved or
        // reinstalled copy leaves behind
        public static bool IsStale()
        {
            string existing = ReadTaskCommand();
            if (string.IsNullOrEmpty(existing)) return false;

            return !string.Equals(existing, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
        }

        // creates, rewrites or removes the task; returns false when the task scheduler refused, so the caller can put
        // its toggle back rather than showing a state that does not exist
        public static bool Apply(bool enabled, bool delayed)
        {
            if (!IsSupported) return false;

            return enabled ? CreateTask(delayed) : DeleteTask();
        }


        // === private helpers ===

        // schtasks /Create with /SC ONLOGON cannot express the three settings below, which is why the task is handed
        // over as a full definition instead:
        // DisallowStartIfOnBatteries and StopIfGoingOnBatteries both default to true, so on a laptop the app would
        // not start on battery and would be killed the moment the charger is pulled
        // ExecutionTimeLimit defaults to three days, which eventually terminates something meant to run all the time
        private static bool CreateTask(bool delayed)
        {
            string? exePath = Environment.ProcessPath;
            string? workingDirectory = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(exePath) || string.IsNullOrEmpty(workingDirectory)) return false;

            string xmlPath = Path.Combine(Path.GetTempPath(), $"{TaskName}_task.xml");

            try
            {
                // UTF-16 on purpose; schtasks /XML rejects a UTF-8 file
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
                catch { /* a leftover definition in the temp folder is harmless, windows clears it eventually */ }
            }
        }

        private static bool DeleteTask()
        {
            // nothing to remove counts as success, the end state is what the caller asked for
            if (ReadTaskXml() == null) return true;

            return RunSchTasks("/Delete", "/TN", TaskName, "/F") == 0;
        }

        // the current user, which is who the task runs as and whose sign-in triggers it
        //
        // KNOWN UNRELIABLE:
        // this reads whoever the process runs as, which is the elevated identity; when a standard user answers the
        // UAC prompt with someone elses administrator credentials, that administrator is what lands here and the task
        // ends up tied to their sign-in instead
        // correct for the ordinary case where the user elevates their own account
        private static string CurrentUserId() => $"{Environment.UserDomainName}\\{Environment.UserName}";

        private static string BuildTaskXml(string exePath, string workingDirectory, bool delayed)
        {
            string userId = SecurityElement.Escape(CurrentUserId());
            string command = SecurityElement.Escape(exePath);
            string directory = SecurityElement.Escape(workingDirectory);
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
                   $"      <WorkingDirectory>{directory}</WorkingDirectory>\n" +
                   "    </Exec>\n" +
                   "  </Actions>\n" +
                   "</Task>\n";
        }

        // the exe the registered task actually launches, or empty when there is no task
        private static string ReadTaskCommand()
        {
            string? xml = ReadTaskXml();
            if (string.IsNullOrEmpty(xml)) return "";

            const string open = "<Command>";
            const string close = "</Command>";

            int start = xml.IndexOf(open, StringComparison.Ordinal);
            if (start < 0) return "";
            start += open.Length;

            int end = xml.IndexOf(close, start, StringComparison.Ordinal);
            if (end < 0) return "";

            return xml.Substring(start, end - start).Trim();
        }

        // returns null when the task does not exist; schtasks answers a missing task with a non-zero exit code
        private static string? ReadTaskXml()
        {
            if (!IsSupported) return null;

            string output = "";
            int exitCode = RunSchTasks(line => output += line, "/Query", "/TN", TaskName, "/XML", "ONE");

            return exitCode == 0 ? output : null;
        }

        private static int RunSchTasks(params string[] arguments) => RunSchTasks(null, arguments);

        // the app is already elevated, so creating and deleting tasks needs no further consent
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

                // deliberately no StandardOutputEncoding: schtasks declares UTF-16 inside the xml it prints but
                // writes it to stdout in the console codepage, and forcing Unicode here turns the whole thing into
                // mojibake (measured)
                // the cost is that a path with non-ascii characters can come back mangled, which at worst makes the
                // staleness check rewrite a task that was already fine
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
