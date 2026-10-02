using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.IO.Compression;

using Windows.Storage;

using FluentSensors.Common;
using FluentSensors.Persistence.Models;


namespace FluentSensors.Persistence.Services
{
    // the persistence service:
    // all disk I/O of the app state in five files (settings, window positions, sensor state, sensor switches, selection
    // profiles); pure I/O, plain data in and out
    public class PersistenceService
    {
        // === fields ===

        // portable mode: the marker file of the portable zip moves the state from %LocalAppData% into the app folder,
        // so nothing stays on the host (the check is in AppDistribution, the updater asks it too)
        private const string PortableFolderName = "Persistence";

        // under %LocalAppData%, for an installed build
        private const string LocalFolderName = "FluentSensors";

        // its name from the FluentHwInfo days; moved once, see MigrateLegacyFolder
        private const string LegacyLocalFolderName = "FluentHwInfo";

        // files that failed to parse, kept apart, so the root folder holds live state only
        private const string QuarantineFolderName = "quarantine";
        private const string CorruptSuffix = ".corrupt-";

        // still there when a problem is reported days later, gone before the folder fills up over years
        private static readonly TimeSpan QuarantineRetention = TimeSpan.FromDays(30);

        private readonly string _rootFolder = ResolveRootFolder();
        private string SettingsPath => Path.Combine(_rootFolder, "settings.json");
        private string WindowStatePath => Path.Combine(_rootFolder, "window-state.json");
        private string SensorStatePath => Path.Combine(_rootFolder, "sensors.json");
        private string SensorSwitchStatePath => Path.Combine(_rootFolder, "sensor-switches.json");
        private string SensorSelectionsPath => Path.Combine(_rootFolder, "sensor-selections.json");
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new ColorJsonConverter(), new JsonStringEnumConverter() }
        };

        // one debounce timer per file, so a window drag does not hammer the disk
        private const int DebounceMs = 1000;
        private Timer _settingsTimer;
        private Timer _windowStateTimer;
        private Timer _sensorStateTimer;
        private Timer _sensorSwitchStateTimer;
        private Timer _sensorSelectionsTimer;

        // the latest pending data per file, written by its timer or FlushAll
        private AppSettingsData _pendingSettings;
        private Dictionary<string, WindowState> _pendingWindowStates;
        private Dictionary<string, SensorState> _pendingSensorStates;
        private Dictionary<string, SensorSwitchState> _pendingSensorSwitchStates;
        private SensorSelectionState _pendingSensorSelections;


        // === singleton instance ===

        public static PersistenceService Instance { get; } = new PersistenceService();


        // === constructor ===

        private PersistenceService()
        {
            TidyQuarantine();
        }


        // === public api ===

        // where the files live, which the settings page shows and opens; differs per channel, see ResolveRootFolder
        public string RootFolder => _rootFolder;

        // load
        public AppSettingsData LoadSettings() => LoadFile<AppSettingsData>(SettingsPath) ?? new AppSettingsData();
        public Dictionary<string, WindowState> LoadWindowStates() => LoadFile<Dictionary<string, WindowState>>(WindowStatePath) ?? new();
        public Dictionary<string, SensorState> LoadSensorStates() => LoadFile<Dictionary<string, SensorState>>(SensorStatePath) ?? new();
        public Dictionary<string, SensorSwitchState> LoadSensorSwitchStates() => LoadFile<Dictionary<string, SensorSwitchState>>(SensorSwitchStatePath) ?? new();
        public SensorSelectionState LoadSensorSelections() => LoadFile<SensorSelectionState>(SensorSelectionsPath) ?? new SensorSelectionState();

        // debounced save
        public void SaveSettingsDebounced(AppSettingsData data)
        {
            _pendingSettings = data;
            ResetTimer(ref _settingsTimer, () => SaveFile(SettingsPath, _pendingSettings));
        }

        public void SaveWindowStatesDebounced(Dictionary<string, WindowState> data)
        {
            _pendingWindowStates = data;
            ResetTimer(ref _windowStateTimer, () => SaveFile(WindowStatePath, _pendingWindowStates));
        }

        public void SaveSensorStatesDebounced(Dictionary<string, SensorState> data)
        {
            _pendingSensorStates = data;
            ResetTimer(ref _sensorStateTimer, () => SaveFile(SensorStatePath, _pendingSensorStates));
        }

        public void SaveSensorSwitchStatesDebounced(Dictionary<string, SensorSwitchState> data)
        {
            _pendingSensorSwitchStates = data;
            ResetTimer(ref _sensorSwitchStateTimer, () => SaveFile(SensorSwitchStatePath, _pendingSensorSwitchStates));
        }

        public void SaveSensorSelectionsDebounced(SensorSelectionState data)
        {
            _pendingSensorSelections = data;
            ResetTimer(ref _sensorSelectionsTimer, () => SaveFile(SensorSelectionsPath, _pendingSensorSelections));
        }

        // immediate save, on exit, so a pending change does not die with its timer
        public void FlushAll()
        {
            _settingsTimer?.Dispose();
            _windowStateTimer?.Dispose();
            _sensorStateTimer?.Dispose();
            _sensorSwitchStateTimer?.Dispose();
            _sensorSelectionsTimer?.Dispose();

            if (_pendingSettings != null) SaveFile(SettingsPath, _pendingSettings);
            if (_pendingWindowStates != null) SaveFile(WindowStatePath, _pendingWindowStates);
            if (_pendingSensorStates != null) SaveFile(SensorStatePath, _pendingSensorStates);
            if (_pendingSensorSwitchStates != null) SaveFile(SensorSwitchStatePath, _pendingSensorSwitchStates);
            if (_pendingSensorSelections != null) SaveFile(SensorSelectionsPath, _pendingSensorSelections);
        }

        // reset: deletes a file and its pending save; the caller restarts right after, so the defaults load
        public void ResetSettings()
        {
            _settingsTimer?.Dispose();
            _settingsTimer = null;
            _pendingSettings = null;
            DeleteFile(SettingsPath);
        }

        public void ResetWindowStates()
        {
            _windowStateTimer?.Dispose();
            _windowStateTimer = null;
            _pendingWindowStates = null;
            DeleteFile(WindowStatePath);
        }

        public void ResetSensorStates()
        {
            _sensorStateTimer?.Dispose();
            _sensorStateTimer = null;
            _pendingSensorStates = null;
            DeleteFile(SensorStatePath);
        }

        public void ResetSensorSwitchStates()
        {
            _sensorSwitchStateTimer?.Dispose();
            _sensorSwitchStateTimer = null;
            _pendingSensorSwitchStates = null;
            DeleteFile(SensorSwitchStatePath);
        }

        public void ResetSensorSelections()
        {
            _sensorSelectionsTimer?.Dispose();
            _sensorSelectionsTimer = null;
            _pendingSensorSelections = null;
            DeleteFile(SensorSelectionsPath);
        }

        public void ResetAll()
        {
            ResetSettings();
            ResetWindowStates();
            ResetSensorStates();
            ResetSensorSwitchStates();
            ResetSensorSelections();
        }

        // backup: the five files in one zip, after a flush, so the export is the live state
        public void ExportBackup(string destinationZipPath)
        {
            FlushAll();

            if (File.Exists(destinationZipPath)) File.Delete(destinationZipPath);

            using var zip = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create);
            AddFileIfExists(zip, SettingsPath, "settings.json");
            AddFileIfExists(zip, WindowStatePath, "window-state.json");
            AddFileIfExists(zip, SensorStatePath, "sensors.json");
            AddFileIfExists(zip, SensorSwitchStatePath, "sensor-switches.json");
            AddFileIfExists(zip, SensorSelectionsPath, "sensor-selections.json");
        }

        // all or nothing: every entry has to be one of the five files and deserialize before the disk is
        // touched; false changes nothing
        public bool ImportBackup(string sourceZipPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(sourceZipPath);

                foreach (var entry in zip.Entries)
                {
                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream);
                    string json = reader.ReadToEnd();

                    bool isValid = entry.Name switch
                    {
                        "settings.json" => TryDeserialize<AppSettingsData>(json),
                        "window-state.json" => TryDeserialize<Dictionary<string, WindowState>>(json),
                        "sensors.json" => TryDeserialize<Dictionary<string, SensorState>>(json),
                        "sensor-switches.json" => TryDeserialize<Dictionary<string, SensorSwitchState>>(json),
                        "sensor-selections.json" => TryDeserialize<SensorSelectionState>(json),
                        _ => false // an unknown entry
                    };
                    if (!isValid) return false;
                }

                // valid; no pending save may overwrite the extracted files
                _settingsTimer?.Dispose(); _settingsTimer = null; _pendingSettings = null;
                _windowStateTimer?.Dispose(); _windowStateTimer = null; _pendingWindowStates = null;
                _sensorStateTimer?.Dispose(); _sensorStateTimer = null; _pendingSensorStates = null;
                _sensorSwitchStateTimer?.Dispose(); _sensorSwitchStateTimer = null; _pendingSensorSwitchStates = null;
                _sensorSelectionsTimer?.Dispose(); _sensorSelectionsTimer = null; _pendingSensorSelections = null;

                DeleteFile(SettingsPath);
                DeleteFile(WindowStatePath);
                DeleteFile(SensorStatePath);
                DeleteFile(SensorSwitchStatePath);
                DeleteFile(SensorSelectionsPath);

                Directory.CreateDirectory(_rootFolder);
                foreach (var entry in zip.Entries)
                {
                    string destPath = entry.Name switch
                    {
                        "settings.json" => SettingsPath,
                        "window-state.json" => WindowStatePath,
                        "sensors.json" => SensorStatePath,
                        "sensor-switches.json" => SensorSwitchStatePath,
                        "sensor-selections.json" => SensorSelectionsPath,
                        _ => null
                    };
                    if (destPath != null) entry.ExtractToFile(destPath, overwrite: true);
                }

                return true;
            }
            catch
            {
                // corrupt zip or unreadable entry; the import failed
                return false;
            }
        }


        // === private helpers ===

        // where the files live, decided once; portable by marker file, a setting would itself need a place to live
        private static string ResolveRootFolder()
        {
            // a packaged build asks the app model: msix redirects %LocalAppData% into its private LocalCache, so
            // Explorer outside the container would open an empty folder; LocalState is the place both agree on, and
            // an uninstall takes it along
            // (no legacy migration, the rename predates the store channel)
            if (AppDistribution.IsPackaged)
            {
                try
                {
                    return ApplicationData.Current.LocalFolder.Path;
                }
                catch (Exception ex)
                {
                    // no app data for this identity (should not happen in a package); the per-user location then
                    Debug.WriteLine($"[PersistenceService] packaged local folder unavailable: {ex.Message}");
                }
            }

            string localAppData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LocalFolderName);

            try
            {
                string? appFolder = Path.GetDirectoryName(Environment.ProcessPath);
                if (string.IsNullOrEmpty(appFolder)) return UseLocalAppData(localAppData);
                if (!AppDistribution.IsPortableBuild) return UseLocalAppData(localAppData);

                // also checks the app folder is writable; a zip unpacked somewhere read-only would drop every save
                string portableFolder = Path.Combine(appFolder, PortableFolderName);
                Directory.CreateDirectory(portableFolder);
                return portableFolder;
            }
            catch
            {
                // an unusable app folder; the per-user location then
                return UseLocalAppData(localAppData);
            }
        }

        private string QuarantineFolder => Path.Combine(_rootFolder, QuarantineFolderName);

        // one pass at startup: strays in the root folder move to quarantine, expired files go
        private void TidyQuarantine()
        {
            try
            {
                if (!Directory.Exists(_rootFolder)) return;

                foreach (string stray in Directory.GetFiles(_rootFolder, $"*{CorruptSuffix}*"))
                {
                    Directory.CreateDirectory(QuarantineFolder);
                    File.Move(stray, Path.Combine(QuarantineFolder, Path.GetFileName(stray)), overwrite: true);
                }

                if (!Directory.Exists(QuarantineFolder)) return;

                foreach (string kept in Directory.GetFiles(QuarantineFolder))
                {
                    if (DateTime.Now - File.GetLastWriteTime(kept) > QuarantineRetention) File.Delete(kept);
                }
            }
            catch (Exception ex)
            {
                // housekeeping; never fails a launch
                Debug.WriteLine($"[PersistenceService] quarantine tidy failed: {ex.Message}");
            }
        }

        // every path to the per-user location goes through here, so no start skips the one-time move
        private static string UseLocalAppData(string localAppData)
        {
            MigrateLegacyFolder(localAppData);

            return localAppData;
        }

        // the folder carries the old app name; moved once, so the renamed app does not start fresh, and only while
        // the new folder does not exist
        private static void MigrateLegacyFolder(string localAppData)
        {
            try
            {
                if (Directory.Exists(localAppData)) return;

                string legacy = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyLocalFolderName);

                if (!Directory.Exists(legacy)) return;

                Directory.Move(legacy, localAppData);
                Debug.WriteLine($"[PersistenceService] moved {LegacyLocalFolderName} to {LocalFolderName}");
            }
            catch (Exception ex)
            {
                // a locked old folder costs the settings, not the launch; defaults then
                Debug.WriteLine($"[PersistenceService] folder migration failed: {ex.Message}");
            }
        }

        private void ResetTimer(ref Timer timer, Action save)
        {
            timer?.Dispose();
            timer = new Timer(_ => save(), null, DebounceMs, Timeout.Infinite);
        }

        private T LoadFile<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(json, _jsonOptions);
            }
            catch (Exception)
            {
                // corrupt: quarantined, the defaults load
                try
                {
                    Directory.CreateDirectory(QuarantineFolder);

                    string name = $"{Path.GetFileName(path)}{CorruptSuffix}{DateTime.Now:yyyyMMdd-HHmmss}";
                    File.Move(path, Path.Combine(QuarantineFolder, name));
                }
                catch { /* if even the move fails, just move on with defaults */ }
                return null;
            }
        }

        private void SaveFile<T>(string path, T data)
        {
            try
            {
                Directory.CreateDirectory(_rootFolder);
                string json = JsonSerializer.Serialize(data, _jsonOptions);
                File.WriteAllText(path, json);
            }
            catch (Exception)
            {
                // best-effort; a failed save never crashes the app
            }
        }

        private void DeleteFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // best-effort; the app restarts anyway, a stale file waits for the next reset
            }
        }

        private void AddFileIfExists(ZipArchive zip, string sourcePath, string entryName)
        {
            if (File.Exists(sourcePath)) zip.CreateEntryFromFile(sourcePath, entryName);
        }

        private bool TryDeserialize<T>(string json) where T : class
        {
            try
            {
                return JsonSerializer.Deserialize<T>(json, _jsonOptions) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
