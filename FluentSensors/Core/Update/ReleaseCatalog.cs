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
    // start, this one answers "what changed, ever" and is only ever touched when the dialog is opened, which is
    // what keeps a store build from reaching the network on its own
    //
    // the answer is cached on disk, so the dialog still has the full history with no connection
    public class ReleaseCatalog
    {
        // === fields ===

        private const string ReleasesUrl = "https://api.github.com/repos/cechout/fluent-sensors/releases?per_page=100";
        private const string CacheFileName = "releases.json";

        // anything older is a pre-1.0 release nobody is offered any more
        private static readonly Version MinimumVersion = new Version(1, 0, 0);

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        private IReadOnlyList<ReleaseEntry> _releases;


        // === singleton instance ===

        private static readonly ReleaseCatalog _instance = new ReleaseCatalog();
        public static ReleaseCatalog Instance => _instance;

        private ReleaseCatalog() { }


        // === public api ===

        // whatever was fetched or read from disk during this session, newest first
        public IReadOnlyList<ReleaseEntry> Releases => _releases ?? Array.Empty<ReleaseEntry>();

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
                File.WriteAllText(CachePath(), JsonSerializer.Serialize(releases, _jsonOptions));
            }
            catch (Exception ex)
            {
                // a read-only or full disk costs the offline copy, nothing more
                Debug.WriteLine($"[ReleaseCatalog] cache write failed: {ex.Message}");
            }
        }

        // alongside the settings json files, so a portable copy carries its release history on the same drive
        private static string CachePath() =>
            Path.Combine(PersistenceService.Instance.RootFolder, CacheFileName);
    }
}
