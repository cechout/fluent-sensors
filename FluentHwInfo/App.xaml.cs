using FluentHwInfo.Services;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.Linq;

namespace FluentHwInfo
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