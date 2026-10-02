using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

using FluentSensors.Common.Csv;
using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorRow;
using FluentSensors.Core;
using FluentSensors.Features.Sensors;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.CsvLogging
{
    // one column of a recording:
    // the sensor id the payloads are matched against, and the header text of the first line
    public class CsvLoggedSensor
    {
        public CsvLoggedSensor(string id, string header, string unit)
        {
            Id = id;
            Header = header;
            Unit = unit;
        }

        public string Id { get; }
        public string Header { get; }

        // next to every value while that option is on; empty for unitless types
        public string Unit { get; }
    }


    // the csv logging service:
    // owns a running recording, its column set, open file, row counter and elapsed clock; a service and not window
    // state, since a recording has to survive the window hiding and being rebuilt without dropping a row
    public class CsvLoggingService
    {
        // === fields ===

        private readonly List<CsvLoggedSensor> _sensors = new();
        private readonly object _writeLock = new();

        // the column set of the open file, a snapshot, so the monitor thread never reads _sensors mid replacement
        private CsvLoggedSensor[] _activeColumns;

        // the format of the open file, a snapshot too, so a settings change mid recording cannot
        // split the file in two formats
        private CsvRowFormat _rowFormat;

        // time and count of the pauses; reset with every start
        private TimeSpan _pausedTotal;
        private DateTime _pauseStartedAt;
        private int _pauseCount;

        // set by Pause, cleared by Resume; the file stays open, only the rows stop
        // (volatile, the click and the monitor thread share no lock)
        private volatile bool _isPaused;

        private StreamWriter _writer;
        private DateTime _startedAt;
        private DateTime _stoppedAt;
        private long _rowCount;


        // === singleton instance ===

        public static CsvLoggingService Instance { get; } = new CsvLoggingService();


        // === constructor ===

        private CsvLoggingService() { }


        // === bindable state ===

        public bool IsRunning { get; private set; }

        // holding; IsRunning stays true through a pause, the clock keeps counting and the file stays claimed
        public bool IsPaused { get; private set; }
        public string CurrentFilePath { get; private set; }
        public int SensorCount => _sensors.Count;
        public long RowCount => Interlocked.Read(ref _rowCount);

        // a start that could not open its file, until the next successful start; shown in the logger window
        public string LastError { get; private set; }

        // the picked folder, otherwise a subfolder in Documents
        public string ResolvedLogFolder
        {
            get
            {
                string configured = SettingsService.Instance.CsvLogFolder;
                if (!string.IsNullOrEmpty(configured)) return configured;

                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FluentSensors");
            }
        }

        // counts while recording, then holds the final duration
        public TimeSpan Elapsed
        {
            get
            {
                if (_startedAt == default) return TimeSpan.Zero;
                return (IsRunning ? DateTime.UtcNow : _stoppedAt) - _startedAt;
            }
        }

        // the time covered by rows, so the readout never contradicts the row counter next to it; a running pause is
        // subtracted on the fly, so it freezes the instant the button is hit
        public TimeSpan RecordedElapsed
        {
            get
            {
                var paused = _pausedTotal;
                if (_isPaused) paused += DateTime.UtcNow - _pauseStartedAt;

                var recorded = Elapsed - paused;
                return recorded > TimeSpan.Zero ? recorded : TimeSpan.Zero;
            }
        }

        // explains the gap between Elapsed and RecordedElapsed; also the seam row count
        public int PauseCount => _pauseCount;

        // start, stop and a taken-over selection, always on the UI thread; (the row counter has no event,
        // CsvLoggerViewModel polls it, so the recording never touches the UI thread)
        public event Action StateChanged;


        // === public api ===

        // takes over the selection from the sensors page; ignored while recording, the header is already written
        public void SetSensors(List<SensorRowViewModel> selectedSensors)
        {
            if (IsRunning) return;

            var hardwareNames = BuildHardwareNameMap();

            // the raw unit, never the displayed one; (the rows carry raw values, a bits setting would mislabel them)
            _sensors.Clear();
            foreach (var sensor in selectedSensors)
            {
                _sensors.Add(new CsvLoggedSensor(sensor.Id, BuildHeader(sensor, hardwareNames),
                    SensorUnitFormatter.GetRawUnit(sensor.SensorType)));
            }

            StateChanged?.Invoke();
        }

        // opens a fresh file and starts recording; false with nothing to record or an unopenable file
        // asks nothing, the folder is picked up front and the timestamp name never overwrites an earlier recording
        public bool Start()
        {
            if (IsRunning || _sensors.Count == 0) return false;

            var rowFormat = CsvRowFormat.Resolve();
            string folder = ResolvedLogFolder;
            string path = Path.Combine(folder, $"FluentSensors-Log-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv");
            var columns = _sensors.ToArray();

            try
            {
                Directory.CreateDirectory(folder);

                // utf-8 with BOM, so Excel picks up the encoding and the degree sign survives
                // AutoFlush, since tray Exit and the settings restart end in Process.Kill(),
                // which would lose a buffered tail
                _writer = new StreamWriter(path, false, new UTF8Encoding(true)) { AutoFlush = true };
                _writer.WriteLine(BuildHeaderLine(columns, rowFormat.Separator));
            }
            catch
            {
                // an unwritable folder (protected, drive gone, file open elsewhere) leaves the recording unstarted;
                // named, since the user can fix it
                try { _writer?.Dispose(); } catch { }
                _writer = null;

                LastError = $"could not write to {folder}";
                StateChanged?.Invoke();
                return false;
            }

            LastError = null;
            _rowFormat = rowFormat;
            _pausedTotal = TimeSpan.Zero;
            _pauseCount = 0;
            _isPaused = false;
            IsPaused = false;
            _activeColumns = columns;
            CurrentFilePath = path;
            Interlocked.Exchange(ref _rowCount, 0);
            _startedAt = DateTime.UtcNow;
            _stoppedAt = _startedAt;
            IsRunning = true;

            HardwareMonitorService.Instance.HardwareDataUpdated += OnHardwareDataUpdated;
            StateChanged?.Invoke();
            return true;
        }

        // after a new folder is picked, the old failure no longer applies
        public void ClearLastError()
        {
            if (LastError == null) return;

            LastError = null;
            StateChanged?.Invoke();
        }

        // clears a finished recording, so the reused window comes back empty; keeps the sensor selection, never
        // touches a running recording
        public void ResetCompletedRecording()
        {
            if (IsRunning) return;

            _startedAt = default;
            _stoppedAt = default;
            _pausedTotal = TimeSpan.Zero;
            _pauseCount = 0;
            Interlocked.Exchange(ref _rowCount, 0);

            CurrentFilePath = null;
            LastError = null;

            StateChanged?.Invoke();
        }

        // holds the recording; file, columns and counter stay, only the payloads are dropped
        public void Pause()
        {
            if (!IsRunning || _isPaused) return;

            _pauseStartedAt = DateTime.UtcNow;
            _pauseCount++;
            _isPaused = true;
            IsPaused = true;

            StateChanged?.Invoke();
        }

        public void Resume()
        {
            if (!IsRunning || !_isPaused) return;

            // the seam before the flag clears, so no payload slips in front of it
            if (SettingsService.Instance.CsvPauseSeam == CsvPauseSeam.Gap)
            {
                WriteSeamRow();
            }

            _pausedTotal += DateTime.UtcNow - _pauseStartedAt;
            _isPaused = false;
            IsPaused = false;

            StateChanged?.Invoke();
        }

        public void Stop()
        {
            if (!IsRunning) return;

            HardwareMonitorService.Instance.HardwareDataUpdated -= OnHardwareDataUpdated;
            _stoppedAt = DateTime.UtcNow;
            IsRunning = false;

            // a stop while holding closes the last pause at the stop instant
            if (_isPaused)
            {
                _pausedTotal += _stoppedAt - _pauseStartedAt;
                _isPaused = false;
                IsPaused = false;
            }

            // a payload from just before the unsubscribe can still be writing, so the close takes the row lock
            lock (_writeLock)
            {
                try { _writer?.Dispose(); } catch { /* every row is flushed already */ }
                _writer = null;
                _activeColumns = null;
            }

            StateChanged?.Invoke();
        }


        // === recording ===

        // stays on the monitor thread; a row is file I/O and touches nothing bindable
        private void OnHardwareDataUpdated(List<SensorData> payload)
        {
            var columns = _activeColumns;
            if (columns == null) return;

            // paused; the payload goes by
            if (_isPaused) return;

            var rowFormat = _rowFormat;
            if (rowFormat == null) return;

            string separator = rowFormat.Separator;

            var values = new Dictionary<string, double>(payload.Count);
            foreach (var sensor in payload)
            {
                values[sensor.Id] = sensor.Value;
            }

            // one instant for both time columns, so they never disagree
            DateTime nowUtc = DateTime.UtcNow;

            var line = new StringBuilder();
            // the timestamp stays invariant in both formats, with the ISO separators
            line.Append(nowUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            line.Append(separator);

            // elapsed keeps one decimal whatever the value precision; its resolution belongs to the poll interval
            line.Append((nowUtc - _startedAt).TotalSeconds.ToString("0.0", rowFormat.ValueCulture));

            foreach (var column in columns)
            {
                line.Append(separator);

                // a sensor missing from the payload leaves the field empty; a zero would read as a measurement
                if (values.TryGetValue(column.Id, out double value))
                {
                    // the raw value, never the SensorUnitFormatter scaling (MHz to GHz above 1000
                    // would change the column unit)
                    line.Append(rowFormat.FormatValue(value, column.Unit));
                }
            }

            lock (_writeLock)
            {
                if (_writer == null) return;

                try
                {
                    _writer.WriteLine(line.ToString());
                }
                catch
                {
                    // best-effort; a full disk must not tear down the monitoring loop
                    return;
                }
            }

            Interlocked.Increment(ref _rowCount);
        }

        // a row of empty fields where a pause ended, so a chart from the file breaks its line instead of interpolating;
        // not counted, it carries no measurement
        private void WriteSeamRow()
        {
            var columns = _activeColumns;
            var rowFormat = _rowFormat;
            if (columns == null || rowFormat == null) return;

            // timestamp, elapsed and one per sensor, so the seam keeps the column count
            string line = new StringBuilder()
                .Insert(0, rowFormat.Separator, columns.Length + 1)
                .ToString();

            lock (_writeLock)
            {
                if (_writer == null) return;

                try
                {
                    _writer.WriteLine(line);
                }
                catch
                {
                    // best-effort, like the rows
                }
            }
        }


        // === private helpers ===

        // sensor id to hardware name, so "Temperature" on CPU and GPU never share a column title
        private static Dictionary<string, string> BuildHardwareNameMap()
        {
            var map = new Dictionary<string, string>();

            foreach (var group in SensorsViewModel.Instance.HardwareGroups)
            {
                foreach (var sensor in group.Sensors.Concat(group.HiddenSensors))
                {
                    map[sensor.Id] = group.HardwareName;
                }
            }

            return map;
        }

        private static string BuildHeader(SensorRowViewModel sensor, Dictionary<string, string> hardwareNames)
        {
            string unit = SensorUnitFormatter.GetRawUnit(sensor.SensorType);
            string name = hardwareNames.TryGetValue(sensor.Id, out string hardwareName) && !string.IsNullOrEmpty(hardwareName)
                ? $"{hardwareName} {sensor.Name}"
                : sensor.Name;

            return unit.Length > 0 ? $"{name} [{unit}]" : name;
        }

        private static string BuildHeaderLine(CsvLoggedSensor[] columns, string separator)
        {
            // elapsed next to the wall clock makes two recordings comparable on one axis
            var line = new StringBuilder("Timestamp").Append(separator).Append("Elapsed [s]");

            foreach (var column in columns)
            {
                line.Append(separator).Append(EscapeCsvField(column.Header, separator));
            }

            return line.ToString();
        }

        // header text only; a hardware name can carry the separator ("AMD Ryzen 7 5800X, 8-Core"), a number never does
        private static string EscapeCsvField(string field, string separator)
        {
            if (!field.Contains(separator) && field.IndexOf('"') < 0) return field;

            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
    }
}
