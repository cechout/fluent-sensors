using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using FluentHwInfo.Models;
using System.IO.Compression;

namespace FluentHwInfo.Services
{
    // handles all disk I/O for persisted app state, split into three files (settings, window positions, per-sensor state)
    // pure I/O layer; knows nothing about SettingsService, windows, or ViewModels; callers hand it plain data and get plain
    // data back
    public class PersistenceService
    {
        // fields
        public static PersistenceService Instance { get; } = new PersistenceService();
        private readonly string _rootFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentHwInfo");
        private string SettingsPath => Path.Combine(_rootFolder, "settings.json");
        private string WindowStatePath => Path.Combine(_rootFolder, "window-state.json");
        private string SensorStatePath => Path.Combine(_rootFolder, "sensors.json");
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

        // most recent pending data per file, written when its timer fires (or on FlushAll)
        private AppSettingsData _pendingSettings;
        private Dictionary<string, WindowState> _pendingWindowStates;
        private Dictionary<string, SensorState> _pendingSensorStates;


        // constructor
        private PersistenceService() { }


        // public binding surface: load
        public AppSettingsData LoadSettings() => LoadFile<AppSettingsData>(SettingsPath) ?? new AppSettingsData();
        public Dictionary<string, WindowState> LoadWindowStates() => LoadFile<Dictionary<string, WindowState>>(WindowStatePath) ?? new();
        public Dictionary<string, SensorState> LoadSensorStates() => LoadFile<Dictionary<string, SensorState>>(SensorStatePath) ?? new();


        // public binding surface: debounced save
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


        // public binding surface: immediate save
        // called on app exit, so the last pending change isnt lost to a debounce timer that never gets to fire because
        // the process is already gone
        public void FlushAll()
        {
            _settingsTimer?.Dispose();
            _windowStateTimer?.Dispose();
            _sensorStateTimer?.Dispose();

            if (_pendingSettings != null) SaveFile(SettingsPath, _pendingSettings);
            if (_pendingWindowStates != null) SaveFile(WindowStatePath, _pendingWindowStates);
            if (_pendingSensorStates != null) SaveFile(SensorStatePath, _pendingSensorStates);
        }


        // public binding surface: reset
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
        public void ResetAll()
        {
            ResetSettings();
            ResetWindowStates();
            ResetSensorStates();
        }


        // public binding surface: backup
        // bundles the three raw json files into one zip; flushes any pending debounced writes first so the export always
        // reflects the latest in-memory state, not a stale version still waiting on its debounce timer
        public void ExportBackup(string destinationZipPath)
        {
            FlushAll();

            if (File.Exists(destinationZipPath)) File.Delete(destinationZipPath);

            using var zip = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create);
            AddFileIfExists(zip, SettingsPath, "settings.json");
            AddFileIfExists(zip, WindowStatePath, "window-state.json");
            AddFileIfExists(zip, SensorStatePath, "sensors.json");
        }

        // all-or-nothing: every entry in the zip must be one of the three known files and must deserialize into its
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
                        _ => false // unknown entry: not a valid backup file
                    };
                    if (!isValid) return false;
                }

                // validation passed: stop pending debounced saves so nothing overwrites what we are about to extract
                _settingsTimer?.Dispose(); _settingsTimer = null; _pendingSettings = null;
                _windowStateTimer?.Dispose(); _windowStateTimer = null; _pendingWindowStates = null;
                _sensorStateTimer?.Dispose(); _sensorStateTimer = null; _pendingSensorStates = null;

                DeleteFile(SettingsPath);
                DeleteFile(WindowStatePath);
                DeleteFile(SensorStatePath);

                Directory.CreateDirectory(_rootFolder);
                foreach (var entry in zip.Entries)
                {
                    string destPath = entry.Name switch
                    {
                        "settings.json" => SettingsPath,
                        "window-state.json" => WindowStatePath,
                        "sensors.json" => SensorStatePath,
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


        // private helpers 
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
                // corrupt file: rename it out of the way and fall back to defaults instead of crashing
                try
                {
                    File.Move(path, path + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}");
                }
                catch { /* if even the rename fails, just move on with defaults */ }
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