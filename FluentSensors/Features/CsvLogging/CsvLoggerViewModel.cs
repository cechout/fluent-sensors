using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

using FluentSensors.Core;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.CsvLogging
{
    // readout for CsvLoggerWindow: what the two buttons are allowed to do plus the three live counters
    //
    // holds no recording state of its own, everything is read back from CsvLoggingService, so a window that was
    // hidden and later reopened (or fully rebuilt) picks the running recording up exactly where it stands
    public class CsvLoggerViewModel : INotifyPropertyChanged
    {
        // === fields ===

        private readonly DispatcherQueue _dispatcherQueue;
        private DispatcherQueueTimer _tickTimer;

        // how often the elapsed clock and the row counter are re-read while recording; unrelated to the sensor poll
        // interval, this only drives text
        private const int TickIntervalMs = 500;

        // whether the window is actually on screen; a hidden or minimized logger stops ticking, the recording in
        // CsvLoggingService keeps running either way
        private bool _isReadoutActive = true;

        // one-off message set when the sensors page pushes a selection at a running recording; cleared again by the
        // next real state change
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

        // start and stop share the same slot in the main bar, only one of them is ever up
        public Visibility StartButtonVisibility => CsvLoggingService.Instance.IsRunning ? Visibility.Collapsed : Visibility.Visible;
        public Visibility StopButtonVisibility => CsvLoggingService.Instance.IsRunning ? Visibility.Visible : Visibility.Collapsed;

        // the pause button only exists while there is a recording to hold; unlike start and stop it is a single
        // button that swaps its glyph, the way the details chevron next to it does
        public Visibility PauseButtonVisibility => CsvLoggingService.Instance.IsRunning ? Visibility.Visible : Visibility.Collapsed;

        // segoe fluent icons Pause and Play, escaped rather than pasted so this file stays plain ascii like the
        // rest of the sources
        public string PauseGlyph => IsPaused ? "\uE768" : "\uE769";
        public string PauseTooltip => IsPaused ? "Resume" : "Pause";

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

        // the stretch covered by rows, which is what the main bar shows next to the row counter
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

        // wall clock since start, pauses included; only shown in the details region, and the value that matches
        // the elapsed column in the file
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

        // the reason the two durations above differ
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

        // elapsed clock and row counter in one line, for the main bar where a single value has to carry both
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

        // takes over a folder the user just picked and drops any earlier start failure with it
        public void SetLogFolder(string folder)
        {
            SettingsService.Instance.CsvLogFolder = folder;
            CsvLoggingService.Instance.ClearLastError();

            LogFolderText = CsvLoggingService.Instance.ResolvedLogFolder;
        }

        // shown when the sensors page pushes a new selection while a recording runs; the columns are already fixed
        // in the open file, so the selection was not taken over and the window has to say so
        public void ShowSelectionLockedHint()
        {
            _transientHint = "stop the recording first to change the sensor selection";
            StatusText = BuildStatusText();
        }

        // pauses the readout while the window is hidden or minimized, so a logger nobody is looking at costs
        // nothing; the recording itself is untouched, only the text stops updating
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

        // the polling rate is a setting, so it is picked up when it changes instead of polled on the tick below
        private void OnUpdateIntervalChanged(int newIntervalMs)
        {
            PollingText = FormatPolling();
        }

        private void OnTick(DispatcherQueueTimer sender, object args)
        {
            RefreshCounters();
        }


        // === private helpers ===

        // the timer only exists while a visible window is showing a running recording
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

        // the rate the user set, not the measured one; a number that only moves when they move it
        private static string FormatPolling()
        {
            return $"{HardwareMonitorService.Instance.UpdateIntervalMs} ms";
        }

        // hours are counted as a running total rather than through a TimeSpan format string; the "hh" specifier wraps
        // at 24 and would silently show a two day recording as a two hour one
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
                return $"paused at {service.RowCount:N0} rows";
            }

            if (service.IsRunning)
            {
                return $"recording to {Path.GetFileName(service.CurrentFilePath)}";
            }

            if (service.SensorCount == 0)
            {
                return "no sensors picked yet, select some in the sensor list";
            }

            if (service.CurrentFilePath != null)
            {
                return $"saved {service.RowCount:N0} rows to {Path.GetFileName(service.CurrentFilePath)}";
            }

            return "ready to record";
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
