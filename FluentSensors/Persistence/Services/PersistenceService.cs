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
    // handles all disk I/O for persisted app state, split into five files (settings, window positions, per-sensor
    // state, sensor-switch choices, sensor selection profiles)
    // pure I/O layer; knows nothing about SettingsService, windows, or ViewModels; callers hand it plain data and get plain
    // data back
    public class PersistenceService
    {
        // === fields ===

        // portable mode: a marker file next to the exe moves persistence from %LocalAppData% into the app folder,
        // so a portable copy carries its state on the drive it runs from and leaves nothing behind on the host
        // the marker ships only in the portable zip, installer builds never contain it
        // the marker check itself lives in AppDistribution, because the updater needs the same answer
        private const string PortableFolderName = "Persistence";

        // the folder under %LocalAppData% an installed build writes to
        private const string LocalFolderName = "FluentSensors";

        // what that folder was called while the app was still named FluentHwInfo; moved once, see MigrateLegacyFolder
        private const string LegacyLocalFolderName = "FluentHwInfo";

        // a file that failed to parse is kept under here rather than beside the ones the app reads, so the root
        // folder holds nothing but live state
        private const string QuarantineFolderName = "quarantine";
        private const string CorruptSuffix = ".corrupt-";

        // how long a quarantined file is kept: long enough to still be there when a problem is reported a few days
        // later, short enough that the folder cannot collect files for years
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

        // one debounce timer per file, so rapid changes (e.g. dragging a window) dont spam disk writes
        private const int DebounceMs = 1000;
        private Timer _settingsTimer;
        private Timer _windowStateTimer;
        private Timer _sensorStateTimer;
        private Timer _sensorSwitchStateTimer;
        private Timer _sensorSelectionsTimer;

        // most recent pending data per file, written when its timer fires (or on FlushAll)
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


        // === public API ===

        // where the five json files actually ended up, which the start page opens in Explorer; the answer differs
        // between an installed and a portable build, see ResolveRootFolder
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

        // immediate save:
        // called on app exit, so the last pending change isnt lost to a debounce timer that never gets to fire because
        // the process is already gone
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

        // reset:
        // wipes a state file from disk and clears any pending debounced save for it, so nothing gets re-written after the
        // reset; caller is expected to restart the app right after, so the in-memory state gets rebuilt from defaults on
        // the next startup load
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

        // backup:
        // bundles the five raw json files into one zip; flushes any pending debounced writes first so the export always
        // reflects the latest in-memory state, not a stale version still waiting on its debounce timer
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

        // all-or-nothing: every entry in the zip must be one of the five known files and must deserialize into its
        // expected type before anything on disk gets touched; returns false without changing any state if validation fails
        // at any point
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
                        _ => false // unknown entry: not a valid backup file
                    };
                    if (!isValid) return false;
                }

                // validation passed: stop pending debounced saves so nothing overwrites what we are about to extract
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
                // corrupt zip, unreadable entry, or anything else unexpected: treat the whole import as failed
                return false;
            }
        }


        // === private helpers ===

        // decides once at startup where the five json files live; portable builds are detected by the marker file
        // rather than by a user setting, because such a setting would itself need a location to be stored in
        private static string ResolveRootFolder()
        {
            // a packaged build asks the app model instead of assembling the path itself: msix redirects a write to
            // %LocalAppData% into the packages private LocalCache, so the literal path below would still be written
            // through, but the start pages "open folder" button hands that path to an explorer running outside the
            // container and would land in an empty directory
            // LocalState is the one location both sides agree on, and uninstalling the package takes it with it
            // no legacy folder migration here on purpose, the rename predates the store channel entirely
            if (AppDistribution.IsPackaged)
            {
                try
                {
                    return ApplicationData.Current.LocalFolder.Path;
                }
                catch (Exception ex)
                {
                    // no app data for this identity, which should not happen inside a package; fall through to the
                    // per-user location rather than losing state
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

                // creating the folder here doubles as an early check that the app directory is writable at all;
                // an unpacked zip sitting in a read-only location would otherwise silently drop every save
                string portableFolder = Path.Combine(appFolder, PortableFolderName);
                Directory.CreateDirectory(portableFolder);
                return portableFolder;
            }
            catch
            {
                // unreadable app folder, or one that cannot be created: fall back to the per-user location instead
                // of losing state
                return UseLocalAppData(localAppData);
            }
        }

        private string QuarantineFolder => Path.Combine(_rootFolder, QuarantineFolderName);

        // one pass over the quarantine at startup: builds before this one dropped the broken file straight into the
        // root folder and nothing ever removed it again, so both of those are cleaned up here
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
                // housekeeping, never worth failing a launch over
                Debug.WriteLine($"[PersistenceService] quarantine tidy failed: {ex.Message}");
            }
        }

        // every path that settles on the per-user location goes through here, so the one-time move below cannot be
        // skipped by whichever of them a given start happens to take
        private static string UseLocalAppData(string localAppData)
        {
            MigrateLegacyFolder(localAppData);

            return localAppData;
        }

        // the settings folder was named after the app, and the app was renamed; moving it once is what keeps an
        // installed copy from looking freshly installed after the update that carries the new name
        //
        // it only ever runs while the new folder does not exist, so a folder that is already in use is never
        // touched and the move cannot happen twice
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
                // a locked or unreadable old folder costs the user their settings, not the launch; the app starts
                // on defaults and writes them to the new location
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
                // corrupt file: move it out of the way and fall back to defaults instead of crashing
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
                // best-effort persistence; a failed save should never crash the app
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
                // best-effort; the app is about to restart anyway, so a stale file just means the next reset attempt handles it
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
