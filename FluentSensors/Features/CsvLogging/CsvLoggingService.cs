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
    // the sensor id every payload is matched against, plus the header text written once into the first line of
    // the file
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

        // written next to every value while that option is on; empty for sensor types that carry no unit
        public string Unit { get; }
    }


    // owns a running csv recording: the column set, the open file, the row counter and the elapsed clock
    //
    // deliberately a service and not window state; CsvLoggerWindow only hides itself when closed (the WinUI
    // retained-instance pattern) and gets fully rebuilt when Windows theme or transparency changes, and a recording
    // has to survive both without dropping a single row
    public class CsvLoggingService
    {
        // === fields ===

        private readonly List<CsvLoggedSensor> _sensors = new();
        private readonly object _writeLock = new();

        // the column set the open file was started with, snapshotted so the monitor thread never reads _sensors
        // while the sensors page is replacing it
        private CsvLoggedSensor[] _activeColumns;

        // the format the open file was started with, snapshotted the same way _activeColumns is:
        // a switch of the separators, the decimal count or the units midway through a recording would leave one
        // half of the file formatted differently from the other, and nothing reading it would notice
        private CsvRowFormat _rowFormat;

        // how much of Elapsed was spent holding, and how often; both reset with every fresh start
        private TimeSpan _pausedTotal;
        private DateTime _pauseStartedAt;
        private int _pauseCount;

        // set by Pause, cleared by Resume; the file and the column set stay open the whole time, only the rows
        // stop arriving
        // volatile because the click that sets it and the monitor thread that reads it never share a lock, and a
        // resume that the writer only notices two polls later would drop measurements for no reason
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

        // true only while a recording is open and holding; IsRunning stays true through a pause, so the elapsed
        // clock keeps counting and the file stays claimed
        public bool IsPaused { get; private set; }
        public string CurrentFilePath { get; private set; }
        public int SensorCount => _sensors.Count;
        public long RowCount => Interlocked.Read(ref _rowCount);

        // set when a start attempt could not open its file, cleared by the next successful start; the logger window
        // is the only place this surfaces
        public string LastError { get; private set; }

        // where recordings are written; the settings value while one has been picked, otherwise a subfolder in
        // Documents so the logs land somewhere the user can actually find them
        public string ResolvedLogFolder
        {
            get
            {
                string configured = SettingsService.Instance.CsvLogFolder;
                if (!string.IsNullOrEmpty(configured)) return configured;

                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FluentSensors");
            }
        }

        // keeps counting while recording, then freezes on the final duration, so the readout still shows how long
        // the finished recording ran
        public TimeSpan Elapsed
        {
            get
            {
                if (_startedAt == default) return TimeSpan.Zero;
                return (IsRunning ? DateTime.UtcNow : _stoppedAt) - _startedAt;
            }
        }

        // the stretch that is actually covered by rows, so the main bar readout can never contradict the row
        // counter sitting next to it: at a 500 ms poll, 1200 rows are ten minutes, and after a pause the wall
        // clock would claim thirteen
        // the running pause is subtracted on the fly, so the number freezes the instant the button is hit
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

        // what explains the gap between Elapsed and RecordedElapsed, and at the same time how many seam rows the
        // file carries
        public int PauseCount => _pauseCount;

        // fires on start, stop and on a taken-over selection; always from the UI thread since every caller is a
        // click handler;
        // The row counter deliberately has no event of its own, CsvLoggerViewModel polls it on a timer instead so
        // the recording never touches the UI thread at all
        public event Action StateChanged;


        // === public api ===

        // takes over the sensor selection pushed from the sensors page
        // Ignored while a recording runs: the column set is written into the file header once at start, changing it
        // afterwards would silently invalidate every row before it
        public void SetSensors(List<SensorRowViewModel> selectedSensors)
        {
            if (IsRunning) return;

            var hardwareNames = BuildHardwareNameMap();

            _sensors.Clear();
            foreach (var sensor in selectedSensors)
            {
                _sensors.Add(new CsvLoggedSensor(sensor.Id, BuildHeader(sensor, hardwareNames),
                    SensorUnitFormatter.GetUnit(sensor.SensorType)));
            }

            StateChanged?.Invoke();
        }

        // opens a fresh csv file in the configured folder and starts recording; returns false when there is nothing
        // to record or the file could not be opened
        //
        // deliberately asks nothing: the folder is picked up front from the logger window, so pressing start is a
        // single click even for a long series of recordings, and the file name is always a fresh timestamp so no
        // earlier recording can be overwritten
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

                // utf-8 with BOM so a double click in Excel picks up the encoding on its own and unit symbols like
                // the degree sign survive
                // AutoFlush is not optional: the tray Exit and the settings restart both end in Process.Kill(),
                // which skips finalizers and every closing handler, so a buffered tail would be lost without a trace
                _writer = new StreamWriter(path, false, new UTF8Encoding(true)) { AutoFlush = true };
                _writer.WriteLine(BuildHeaderLine(columns, rowFormat.Separator));
            }
            catch
            {
                // a folder we cannot write to (protected location, drive gone, file open elsewhere) leaves the
                // recording unstarted;
                // The reason is named rather than swallowed, this is the one failure the user
                // can actually fix
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

        // drops a previous start failure, called after the user picked a different folder so the old message does
        // not keep pointing at a location that is no longer the target
        public void ClearLastError()
        {
            if (LastError == null) return;

            LastError = null;
            StateChanged?.Invoke();
        }

        // clears what a finished recording left behind, so the readout comes back up empty instead of showing the
        // counters of a session that is long over
        //
        // has to be explicit because CsvLoggerWindow is only hidden when it is closed and reused afterwards; the
        // sensor selection is deliberately kept, it comes from the sensors page and is what makes the next start
        // possible at all
        //
        // a running recording is never touched: closing the readout stops the readout, never the logging
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

        // holds the recording without giving up the file: the columns, the header and the row counter all stay,
        // only the incoming payloads are dropped
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

            // the seam is written before the flag is cleared, so a payload arriving in between cannot slip a real
            // measurement in front of the gap row
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

            // a recording stopped while it was holding still has that last interval open; closing it against the
            // stop instant is what keeps the final recorded duration correct
            if (_isPaused)
            {
                _pausedTotal += _stoppedAt - _pauseStartedAt;
                _isPaused = false;
                IsPaused = false;
            }

            // a payload that arrived just before the unsubscribe can still be inside the write below, so closing the
            // file takes the same lock the rows do
            lock (_writeLock)
            {
                try { _writer?.Dispose(); } catch { /* every row is already flushed, a failing dispose loses nothing */ }
                _writer = null;
                _activeColumns = null;
            }

            StateChanged?.Invoke();
        }


        // === recording ===

        // runs on the HardwareMonitorService background thread and deliberately stays there; writing a row is pure
        // file I/O and touches nothing bindable, so there is no reason to pay a dispatcher hop per poll
        private void OnHardwareDataUpdated(List<SensorData> payload)
        {
            var columns = _activeColumns;
            if (columns == null) return;

            // a paused recording keeps its file open and simply lets the payload go by
            if (_isPaused) return;

            var rowFormat = _rowFormat;
            if (rowFormat == null) return;

            string separator = rowFormat.Separator;

            var values = new Dictionary<string, double>(payload.Count);
            foreach (var sensor in payload)
            {
                values[sensor.Id] = sensor.Value;
            }

            // one instant for both time columns, so the wall clock and the elapsed seconds can never disagree by the
            // few microseconds two separate reads would drift apart
            DateTime nowUtc = DateTime.UtcNow;

            var line = new StringBuilder();
            // the timestamp stays invariant in both formats; its separators are the ISO ones, and running them
            // through a culture would swap the date and time separators for no gain
            line.Append(nowUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            line.Append(separator);

            // the elapsed column keeps its own fixed single decimal and ignores the configured value precision:
            // its resolution belongs to the poll interval, not to how precisely a sensor is worth reading, and a
            // tenth of a second is already finer than the 500 ms the monitor runs at by default
            line.Append((nowUtc - _startedAt).TotalSeconds.ToString("0.0", rowFormat.ValueCulture));

            foreach (var column in columns)
            {
                line.Append(separator);

                // a sensor missing from this payload (hidden after the recording started, or hardware gone) leaves
                // the field empty; a zero would read as a real measurement
                if (values.TryGetValue(column.Id, out double value))
                {
                    // the raw value, never the SensorUnitFormatter scaling: that switches MHz to GHz above 1000 and
                    // would change a columns unit halfway through the file
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
                    // best-effort; a disk that stopped accepting writes must not tear down the monitoring loop
                    return;
                }
            }

            Interlocked.Increment(ref _rowCount);
        }

        // one row of empty fields at the point a pause was resumed, so a chart drawn from the file breaks its line
        // there instead of interpolating across a stretch that was never measured
        //
        // deliberately not counted into _rowCount: it carries no measurement, and the readout would otherwise claim
        // rows the recording never took
        private void WriteSeamRow()
        {
            var columns = _activeColumns;
            var rowFormat = _rowFormat;
            if (columns == null || rowFormat == null) return;

            // two fixed columns (timestamp and elapsed) plus one per sensor, so the seam keeps the column count of
            // every other row and reads back as empty cells rather than as a short row
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
                    // same best-effort rule the rows follow
                }
            }
        }


        // === private helpers ===

        // maps every known sensor id to the hardware it sits on, so two identically named sensors on different
        // hardware ("Temperature" on the CPU and on the GPU) never collapse into the same column title
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
            string unit = SensorUnitFormatter.GetUnit(sensor.SensorType);
            string name = hardwareNames.TryGetValue(sensor.Id, out string hardwareName) && !string.IsNullOrEmpty(hardwareName)
                ? $"{hardwareName} {sensor.Name}"
                : sensor.Name;

            return unit.Length > 0 ? $"{name} [{unit}]" : name;
        }

        private static string BuildHeaderLine(CsvLoggedSensor[] columns, string separator)
        {
            // the elapsed column sits next to the wall clock deliberately: it is what makes two recordings comparable
            // on one axis without subtracting timestamps first
            var line = new StringBuilder("Timestamp").Append(separator).Append("Elapsed [s]");

            foreach (var column in columns)
            {
                line.Append(separator).Append(EscapeCsvField(column.Header, separator));
            }

            return line.ToString();
        }

        // header text only; hardware names can carry the separator ("AMD Ryzen 7 5800X, 8-Core"), the numeric
        // fields never can, their decimal separator is never the field one
        private static string EscapeCsvField(string field, string separator)
        {
            if (!field.Contains(separator) && field.IndexOf('"') < 0) return field;

            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
    }
}
