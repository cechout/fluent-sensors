using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

using FluentSensors.Common.Localization;
using FluentSensors.Core;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.CsvLogging
{
    // the csv logger readout:
    // button states and live counters of CsvLoggerWindow; no recording state of its own, so a reopened or rebuilt
    // window picks the recording up where it stands
    public class CsvLoggerViewModel : INotifyPropertyChanged
    {
        // === fields ===

        private readonly DispatcherQueue _dispatcherQueue;
        private DispatcherQueueTimer _tickTimer;

        // elapsed clock and row counter
        private const int TickIntervalMs = 500;

        // the window is on screen; a hidden or minimized logger stops ticking, the recording runs on
        private bool _isReadoutActive = true;

        // set when the sensors page pushes a selection at a running recording, until the next state change
        private string _transientHint;


        // === constructor ===

        public CsvLoggerViewModel()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            CsvLoggingService.Instance.StateChanged += OnLoggingStateChanged;
            HardwareMonitorService.Instance.UpdateIntervalChanged += OnUpdateIntervalChanged;

            RefreshAll();
            UpdateTickTimer();
        }


        // === bindable properties ===

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (_isRunning == value) return;
                _isRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(PauseButtonVisibility));
            }
        }

        private bool _isPaused;
        public bool IsPaused
        {
            get => _isPaused;
            private set
            {
                if (_isPaused == value) return;
                _isPaused = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PauseGlyph));
                OnPropertyChanged(nameof(PauseTooltip));
            }
        }

        public bool CanStart => !CsvLoggingService.Instance.IsRunning && CsvLoggingService.Instance.SensorCount > 0;
        public bool CanStop => CsvLoggingService.Instance.IsRunning;

        // start and stop share one slot
        public Visibility StartButtonVisibility => CsvLoggingService.Instance.IsRunning ? Visibility.Collapsed : Visibility.Visible;
        public Visibility StopButtonVisibility => CsvLoggingService.Instance.IsRunning ? Visibility.Visible : Visibility.Collapsed;

        // only while recording; one button that swaps its glyph, like the details chevron
        public Visibility PauseButtonVisibility => CsvLoggingService.Instance.IsRunning ? Visibility.Visible : Visibility.Collapsed;

        // Pause and Play, escaped to keep the source ascii
        public string PauseGlyph => IsPaused ? "\uE768" : "\uE769";
        public string PauseTooltip => AppStrings.Get(IsPaused ? "Common_Resume" : "Common_Pause");

        private string _logFolderText = "";
        public string LogFolderText
        {
            get => _logFolderText;
            private set
            {
                if (_logFolderText == value) return;
                _logFolderText = value;
                OnPropertyChanged();
            }
        }

        private string _recordedElapsedText = "00:00:00";
        public string RecordedElapsedText
        {
            get => _recordedElapsedText;
            private set
            {
                if (_recordedElapsedText == value) return;
                _recordedElapsedText = value;
                OnPropertyChanged();
            }
        }

        private string _totalElapsedText = "00:00:00";
        public string TotalElapsedText
        {
            get => _totalElapsedText;
            private set
            {
                if (_totalElapsedText == value) return;
                _totalElapsedText = value;
                OnPropertyChanged();
            }
        }

        private string _pauseCountText = "0";
        public string PauseCountText
        {
            get => _pauseCountText;
            private set
            {
                if (_pauseCountText == value) return;
                _pauseCountText = value;
                OnPropertyChanged();
            }
        }

        private string _sensorCountText = "0";
        public string SensorCountText
        {
            get => _sensorCountText;
            private set
            {
                if (_sensorCountText == value) return;
                _sensorCountText = value;
                OnPropertyChanged();
            }
        }

        private string _rowCountText = "0";
        public string RowCountText
        {
            get => _rowCountText;
            private set
            {
                if (_rowCountText == value) return;
                _rowCountText = value;
                OnPropertyChanged();
            }
        }

        // the main bar
        private string _elapsedRowsText = "00:00:00 | 0";
        public string ElapsedRowsText
        {
            get => _elapsedRowsText;
            private set
            {
                if (_elapsedRowsText == value) return;
                _elapsedRowsText = value;
                OnPropertyChanged();
            }
        }

        private string _pollingText = "0 ms";
        public string PollingText
        {
            get => _pollingText;
            private set
            {
                if (_pollingText == value) return;
                _pollingText = value;
                OnPropertyChanged();
            }
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (_statusText == value) return;
                _statusText = value;
                OnPropertyChanged();
            }
        }


        // === public methods ===

        public void Start()
        {
            CsvLoggingService.Instance.Start();
        }

        public void Stop()
        {
            CsvLoggingService.Instance.Stop();
        }

        public void TogglePause()
        {
            var service = CsvLoggingService.Instance;

            if (service.IsPaused)
            {
                service.Resume();
            }
            else
            {
                service.Pause();
            }
        }

        // a picked folder, which drops an earlier start failure
        public void SetLogFolder(string folder)
        {
            SettingsService.Instance.CsvLogFolder = folder;
            CsvLoggingService.Instance.ClearLastError();

            LogFolderText = CsvLoggingService.Instance.ResolvedLogFolder;
        }

        // a selection pushed during a recording is not taken over (the columns are fixed); the window says so
        public void ShowSelectionLockedHint()
        {
            _transientHint = AppStrings.Get("CsvLogger_StatusSelectionLocked");
            StatusText = BuildStatusText();
        }

        // pauses the readout of a hidden or minimized window; the recording is untouched
        public void SetReadoutActive(bool active)
        {
            if (_isReadoutActive == active) return;
            _isReadoutActive = active;

            UpdateTickTimer();

            if (active)
            {
                RefreshAll();
            }
        }

        public void Cleanup()
        {
            CsvLoggingService.Instance.StateChanged -= OnLoggingStateChanged;
            HardwareMonitorService.Instance.UpdateIntervalChanged -= OnUpdateIntervalChanged;

            _isReadoutActive = false;
            UpdateTickTimer();
        }


        // === event handlers ===

        private void OnLoggingStateChanged()
        {
            _transientHint = null;

            RefreshAll();
            UpdateTickTimer();
        }

        // a setting, so taken on change rather than per tick
        private void OnUpdateIntervalChanged(int newIntervalMs)
        {
            PollingText = FormatPolling();
        }

        private void OnTick(DispatcherQueueTimer sender, object args)
        {
            RefreshCounters();
        }


        // === private helpers ===

        // a timer only while a visible window shows a running recording
        private void UpdateTickTimer()
        {
            bool shouldTick = _isReadoutActive && CsvLoggingService.Instance.IsRunning;

            if (shouldTick)
            {
                if (_tickTimer != null) return;

                _tickTimer = _dispatcherQueue.CreateTimer();
                _tickTimer.Interval = TimeSpan.FromMilliseconds(TickIntervalMs);
                _tickTimer.IsRepeating = true;
                _tickTimer.Tick += OnTick;
                _tickTimer.Start();
            }
            else
            {
                if (_tickTimer == null) return;

                _tickTimer.Tick -= OnTick;
                _tickTimer.Stop();
                _tickTimer = null;
            }
        }

        private void RefreshAll()
        {
            var service = CsvLoggingService.Instance;

            IsRunning = service.IsRunning;
            IsPaused = service.IsPaused;
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(StartButtonVisibility));
            OnPropertyChanged(nameof(StopButtonVisibility));
            OnPropertyChanged(nameof(PauseButtonVisibility));

            SensorCountText = service.SensorCount.ToString();
            LogFolderText = service.ResolvedLogFolder;
            PollingText = FormatPolling();
            RefreshCounters();
        }

        private void RefreshCounters()
        {
            var service = CsvLoggingService.Instance;

            RecordedElapsedText = FormatElapsed(service.RecordedElapsed);
            TotalElapsedText = FormatElapsed(service.Elapsed);
            PauseCountText = service.PauseCount.ToString();
            RowCountText = service.RowCount.ToString("N0");
            ElapsedRowsText = $"{RecordedElapsedText} | {RowCountText}";
            StatusText = BuildStatusText();
        }

        private static string FormatPolling()
        {
            return $"{HardwareMonitorService.Instance.UpdateIntervalMs} ms";
        }

        // total hours, since "hh" wraps at 24
        private static string FormatElapsed(TimeSpan elapsed)
        {
            return $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        }

        private string BuildStatusText()
        {
            if (_transientHint != null) return _transientHint;

            var service = CsvLoggingService.Instance;

            if (service.LastError != null) return service.LastError;

            if (service.IsPaused)
            {
                return AppStrings.Format("CsvLogger_StatusPaused", service.RowCount);
            }

            if (service.IsRunning)
            {
                return AppStrings.Format("CsvLogger_StatusRecording", Path.GetFileName(service.CurrentFilePath));
            }

            if (service.SensorCount == 0)
            {
                return AppStrings.Get("CsvLogger_StatusNoSensors");
            }

            if (service.CurrentFilePath != null)
            {
                return AppStrings.Format("CsvLogger_StatusSaved", service.RowCount, Path.GetFileName(service.CurrentFilePath));
            }

            return AppStrings.Get("CsvLogger_StatusReady");
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
