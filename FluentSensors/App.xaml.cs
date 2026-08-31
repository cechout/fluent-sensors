using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;

using FluentSensors.Persistence.Services;


namespace FluentSensors
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();

            // settings are written through a 1s debounce, and MainWindow only flushes on its own two exit routes;
            // an unhandled exception would drop whatever is still pending, so flush here as well
            // a debugger stop or an external kill still cannot be covered, nothing managed runs on TerminateProcess
            this.UnhandledException += (s, e) => PersistenceService.Instance.FlushAll();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // brute-force safety net:
            // force-close any other instance of this app that might still be lingering (e.g. a previous instance stuck
            // mid-shutdown)
            // this replaces cooperatively waiting for one specific PID to exit, since that PID's own self-termination has
            // turned out to be unreliable
            KillOtherInstances();

            // load all persisted state from disk before any window or service falls back to defaults
            SettingsService.Instance.LoadFromData(PersistenceService.Instance.LoadSettings());
            SensorStateService.Instance.LoadFromDisk(PersistenceService.Instance.LoadSensorStates());
            WindowStateService.Instance.LoadFromDisk(PersistenceService.Instance.LoadWindowStates());
            SensorSwitchStateService.Instance.LoadFromDisk(PersistenceService.Instance.LoadSensorSwitchStates());
            SensorSelectionService.Instance.LoadFromDisk(PersistenceService.Instance.LoadSensorSelections());

            // one-time migration for users updating from a version before selection profiles existed, WindowStateService
            // is already loaded by this point so the legacy widget pin list is available
            SensorSelectionService.Instance.MigrateFromLegacyWidgetPins(WindowStateService.Instance.GetState("Widget")?.PinnedSensorIds);

            _window = new MainWindow();
            _window.Activate();
        }

        // force-terminates every other process sharing this apps process name, so a stuck previous instance never lingers
        // alongside a freshly started one
        private void KillOtherInstances()
        {
            int currentPid = Environment.ProcessId;
            string currentName = Process.GetCurrentProcess().ProcessName;

            foreach (var process in Process.GetProcessesByName(currentName))
            {
                if (process.Id == currentPid) continue;

                try
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
                catch
                {
                    // already gone, access denied, or didnt finish terminating in time; nothing more to safely do here
                    // without blocking this instances own startup indefinitely
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }
}