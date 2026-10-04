using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;

using FluentSensors.Common.Localization;
using FluentSensors.Persistence.Services;


namespace FluentSensors
{
    public partial class App : Application
    {
        private Window? _window;

        public App()
        {
            // before InitializeComponent, the first resource lookup fixes the language of the process; read
            // straight from the file, SettingsService loads in OnLaunched
            var settings = PersistenceService.Instance.LoadSettings();
            AppLanguage.Apply(settings.AppLanguage);
            AppTerms.Configure(settings.TechnicalTermsInEnglish);

            InitializeComponent();

            // settings write through a 1 s debounce and MainWindow flushes on its two exit routes
            // only, so a crash flushes here
            // (a debugger stop or an external kill runs nothing managed)
            this.UnhandledException += (s, e) => PersistenceService.Instance.FlushAll();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // safety net: kills any lingering instance (one stuck mid-shutdown); waiting on its PID proved unreliable
            KillOtherInstances();

            // before any window or service falls back to defaults
            SettingsService.Instance.LoadFromData(PersistenceService.Instance.LoadSettings());
            SensorStateService.Instance.LoadFromDisk(PersistenceService.Instance.LoadSensorStates());
            WindowStateService.Instance.LoadFromDisk(PersistenceService.Instance.LoadWindowStates());
            SensorSwitchStateService.Instance.LoadFromDisk(PersistenceService.Instance.LoadSensorSwitchStates());
            SensorSelectionService.Instance.LoadFromDisk(PersistenceService.Instance.LoadSensorSelections());

            // one-time migration from before selection profiles, from the widget pin list
            // WindowStateService loaded above
            SensorSelectionService.Instance.MigrateFromLegacyWidgetPins(WindowStateService.Instance.GetState("Widget")?.PinnedSensorIds);

            _window = new MainWindow();
            _window.Activate();
        }

        // kills every other process of this name, so no stuck instance runs beside a new one
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
                    // gone, denied or not done in time; anything more would block this startup
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }
}
