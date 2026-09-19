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
    // one published release as the notes reader needs it; deliberately not UpdateInfo, which carries the asset a
    // build would install and says nothing about releases other than the latest
    public record ReleaseEntry(
        string Version, // three part, no leading v, e.g. "1.3.0"
        string TagName,
        string Name,
        DateTimeOffset PublishedAt,
        string Notes, // raw markdown body
        string ReleaseUrl
    );


    // every published minor and major release from 1.0.0 onwards, for the release notes dialog
    //
    // kept apart from UpdateService on purpose: that one answers "is there something newer to install" once per
    // start, this one answers "what changed, ever" and is only ever touched when the dialog is opened
    //
    // it is not gated on the startup check setting: that switch governs what the app does without being asked,
    // and opening the dialog is asking
    //
    // the answer is cached on disk, so the dialog still has the full history with no connection
    public class ReleaseCatalog
    {
        // === fields ===

        private const string ReleasesUrl = "https://api.github.com/repos/cechout/fluent-sensors/releases?per_page=100";
        private const string CacheFileName = "releases.json";

        // everything the network can rebuild sits in here, apart from the state files, so the whole folder can be
        // deleted without anyone losing a setting
        private const string CacheFolderName = "cache";

        // anything older is a pre-1.0 release nobody is offered any more
        private static readonly Version MinimumVersion = new Version(1, 0, 0);

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        private IReadOnlyList<ReleaseEntry> _releases;
        private bool _hasFetchedThisRun;


        // === singleton instance ===

        private static readonly ReleaseCatalog _instance = new ReleaseCatalog();
        public static ReleaseCatalog Instance => _instance;

        private ReleaseCatalog() { }


        // === public api ===

        // whatever was fetched or read from disk during this session, newest first
        public IReadOnlyList<ReleaseEntry> Releases => _releases ?? Array.Empty<ReleaseEntry>();

        // what the dialog asks for instead of RefreshAsync
        //
        // the on-disk copy stays good until a release exists that it does not carry, so opening the dialog is not
        // by itself a reason to spend one of the 60 unauthenticated api requests GitHub grants per hour and ip
        // returns null when nothing was fetched, which is the signal to keep showing what is already there
        //
        // deliberately not gated on CheckUpdatesOnStartup: that switch is about what the app does on its own, and
        // the update button on the start page already reaches GitHub while it is off, for the same reason
        // at most one fetch per run, so a version the catalog can never carry, which is what a local build
        // numbered above every release is, cannot turn every dialog open into a request
        public async Task<IReadOnlyList<ReleaseEntry>> EnsureCurrentAsync()
        {
            var cached = LoadCached();

            if (cached.Count > 0 && !IsMissingLatest()) return null;
            if (_hasFetchedThisRun) return null;

            _hasFetchedThisRun = true;

            return await RefreshAsync();
        }

        // the on-disk copy, so the dialog has something to render before the network answers and keeps having it
        // when there is no network at all
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
                // a truncated or hand-edited cache is not worth failing over, the refresh below replaces it anyway
                Debug.WriteLine($"[ReleaseCatalog] cache read failed: {ex.Message}");
            }

            return Releases;
        }

        // returns null when GitHub could not be reached, which is the signal to keep showing the cache
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

        // whether the catalog is missing a release it ought to carry
        //
        // two sources answer that and either one is enough: the update check knows the newest published release
        // but only once it has run, which it never does while the startup check is off, and the running version
        // is always known
        // a published release is in the catalog by definition, so a catalog that does not list the version this
        // app is cannot be current; that is the source that keeps the dialog working with the check switched off
        //
        // the catalog only lists x.y.0, so a patch release is matched against the minor it belongs to; without
        // that a published 1.3.1 would look missing forever
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

        // drafts and prereleases are skipped the same way the updater skips them, anything below 1.0.0 is
        // history nobody is offered any more, and a patch is folded away because its notes never say anything
        // the minor release it belongs to does not already say
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

            // the api already answers newest first, but the dialog depends on that order rather than hoping for it
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
                // a read-only or full disk costs the offline copy, nothing more
                Debug.WriteLine($"[ReleaseCatalog] cache write failed: {ex.Message}");
            }
        }

        // under the settings folder, so a portable copy carries its release history on the same drive
        private static string CacheFolder() =>
            Path.Combine(PersistenceService.Instance.RootFolder, CacheFolderName);

        private static string CachePath() => Path.Combine(CacheFolder(), CacheFileName);
    }
}
