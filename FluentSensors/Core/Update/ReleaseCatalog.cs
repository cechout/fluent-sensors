using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using FluentSensors.Persistence.Services;


namespace FluentSensors.Core.Update
{
    // one published release for the notes reader; (UpdateInfo carries the asset and knows the latest only)
    public record ReleaseEntry(
        string Version, // three part, no v, e.g. "1.3.0"
        string TagName,
        string Name,
        DateTimeOffset PublishedAt,
        string Notes, // raw markdown body
        string ReleaseUrl
    );


    // the release catalog:
    // every published minor and major release from 1.0.0, for the release notes dialog; apart from UpdateService, which
    // asks "anything newer" per start, this asks "what changed, ever" when the dialog opens
    // not gated on the startup check (opening the dialog is asking); cached on disk for offline use
    public class ReleaseCatalog
    {
        // === fields ===

        private const string ReleasesUrl = "https://api.github.com/repos/cechout/fluent-sensors/releases?per_page=100";
        private const string CacheFileName = "releases.json";

        // what the network can rebuild, apart from the state files; deleting it loses no setting
        private const string CacheFolderName = "cache";

        // older is pre-1.0, offered to nobody
        private static readonly Version MinimumVersion = new Version(1, 0, 0);

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        private IReadOnlyList<ReleaseEntry> _releases;
        private bool _hasFetchedThisRun;


        // === singleton instance ===

        private static readonly ReleaseCatalog _instance = new ReleaseCatalog();
        public static ReleaseCatalog Instance => _instance;

        private ReleaseCatalog() { }


        // === public api ===

        // fetched or read this session, newest first
        public IReadOnlyList<ReleaseEntry> Releases => _releases ?? Array.Empty<ReleaseEntry>();

        // what the dialog calls; the disk copy holds until a release is missing, so an open costs none of the 60 hourly
        // GitHub requests; null = nothing fetched, keep what is shown
        // one fetch per run at most, so a local build numbered above every release does not request on every open
        public async Task<IReadOnlyList<ReleaseEntry>> EnsureCurrentAsync()
        {
            var cached = LoadCached();

            if (cached.Count > 0 && !IsMissingLatest()) return null;
            if (_hasFetchedThisRun) return null;

            _hasFetchedThisRun = true;

            return await RefreshAsync();
        }

        // the disk copy, for before the network answers and without one
        public IReadOnlyList<ReleaseEntry> LoadCached()
        {
            if (_releases != null) return _releases;

            try
            {
                string path = CachePath();
                if (!File.Exists(path)) return Array.Empty<ReleaseEntry>();

                var cached = JsonSerializer.Deserialize<List<ReleaseEntry>>(File.ReadAllText(path));
                if (cached != null) _releases = cached;
            }
            catch (Exception ex)
            {
                // a broken cache is not worth failing over, a refresh replaces it
                Debug.WriteLine($"[ReleaseCatalog] cache read failed: {ex.Message}");
            }

            return Releases;
        }

        // null when GitHub is unreachable; the cache stays
        public async Task<IReadOnlyList<ReleaseEntry>> RefreshAsync()
        {
            try
            {
                string json = await UpdateService.Http.GetStringAsync(ReleasesUrl);

                var parsed = Parse(json);
                if (parsed.Count == 0) return null;

                _releases = parsed;
                WriteCache(parsed);

                return parsed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ReleaseCatalog] refresh failed: {ex.Message}");
                return null;
            }
        }


        // === private helpers ===

        // a release the catalog ought to carry is missing: the newest one the update check found (once it ran), or the
        // running version (always known, so this works with the check off)
        // the catalog lists x.y.0 only, so a patch matches its minor; (or 1.3.1 would look missing forever)
        private bool IsMissingLatest()
        {
            return IsMissing(UpdateService.Instance.LatestRelease?.Version)
                || IsMissing(UpdateService.CurrentVersion);
        }

        private bool IsMissing(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return false;
            if (!Version.TryParse(version, out var parsed)) return false;

            return !Releases.Any(entry => entry.Version == $"{parsed.Major}.{parsed.Minor}.0");
        }

        // skips drafts and prereleases like the updater, anything below 1.0.0, and patches (their minor says it all)
        private static List<ReleaseEntry> Parse(string json)
        {
            var entries = new List<ReleaseEntry>();

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return entries;

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (Flag(element, "draft") || Flag(element, "prerelease")) continue;

                string tag = Text(element, "tag_name");
                string version = tag.TrimStart('v', 'V');
                if (!Version.TryParse(version, out var parsedVersion)) continue;
                if (parsedVersion < MinimumVersion) continue;
                if (parsedVersion.Build > 0) continue;

                var published = element.TryGetProperty("published_at", out var publishedElement)
                    && publishedElement.TryGetDateTimeOffset(out var value)
                        ? value
                        : DateTimeOffset.MinValue;

                string name = Text(element, "name");
                if (string.IsNullOrWhiteSpace(name)) name = tag;

                entries.Add(new ReleaseEntry(
                    $"{parsedVersion.Major}.{parsedVersion.Minor}.{Math.Max(parsedVersion.Build, 0)}",
                    tag,
                    name,
                    published,
                    Text(element, "body"),
                    Text(element, "html_url")));
            }

            // the api answers newest first; the dialog depends on it, so it is sorted anyway
            return entries.OrderByDescending(e => e.PublishedAt).ToList();
        }

        private static string Text(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) ? value.GetString() ?? "" : "";

        private static bool Flag(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

        private static void WriteCache(List<ReleaseEntry> releases)
        {
            try
            {
                Directory.CreateDirectory(CacheFolder());
                File.WriteAllText(CachePath(), JsonSerializer.Serialize(releases, _jsonOptions));
            }
            catch (Exception ex)
            {
                // a read-only or full disk costs the offline copy only
                Debug.WriteLine($"[ReleaseCatalog] cache write failed: {ex.Message}");
            }
        }

        // under the settings folder, so a portable copy keeps it on its drive
        private static string CacheFolder() =>
            Path.Combine(PersistenceService.Instance.RootFolder, CacheFolderName);

        private static string CachePath() => Path.Combine(CacheFolder(), CacheFileName);
    }
}
