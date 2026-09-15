using Microsoft.UI.Dispatching;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

using FluentSensors.Common;


namespace FluentSensors.Core.Update
{
    // everything the app knows about a newer release: the bare version for display and comparison, the page to send
    // a user to, and the one asset that matches this builds distribution channel
    public record UpdateInfo(
        string Version, // three part, no leading v, e.g. "1.3.0"
        string ReleaseUrl,
        string AssetName,
        string AssetUrl,
        long AssetSize
    );


    public enum UpdateCheckResult
    {
        UpToDate,
        UpdateAvailable,
        Failed // no network, rate limited, malformed response; the reason is never shown, only that it did not work
    }


    // asks the GitHub releases api once per app start whether a newer version exists, and hands the answer to the
    // title bar and the settings page
    //
    // the api only ever returns published, non-draft, non-prerelease releases, which lines up exactly with the
    // release workflow: it drafts a release on every version tag and a human publishes it afterwards, so a draft
    // sitting around can never reach a user as an update
    public class UpdateService
    {
        // === fields ===

        private const string LatestReleaseUrl = "https://api.github.com/repos/cechout/fluent-sensors/releases/latest";
        private const string ReleasesPageUrl = "https://github.com/cechout/fluent-sensors/releases";

        // one client for the process; a per-call "using" would burn a socket per check and leave it in TIME_WAIT
        //
        // the published build has no HTTP/3: msquic.dll is removed from the payload by the PruneUnusedPublishPayload
        // target in the csproj
        // the default request version is 1.1 with RequestVersionOrLower, so nothing here ever reaches for it; raising
        // it would work in a debug build and fail only in a release one
        private static readonly HttpClient _http = CreateClient();

        private DispatcherQueue? _dispatcherQueue;
        private bool _hasCheckedOnStartup;


        // === singleton instance ===

        private static readonly UpdateService _instance = new UpdateService();
        public static UpdateService Instance => _instance;

        private UpdateService() { }


        // === public api ===

        // raised from the UI thread whenever IsUpdateAvailable or Latest moves, which is both the moment a newer
        // release is found and the moment one is skipped; a single event keeps the title bar and the settings page
        // in step no matter which of the two triggered the change
        public event Action? UpdateStateChanged;

        public bool IsUpdateAvailable { get; private set; }

        public UpdateInfo? Latest { get; private set; }

        public static string CurrentVersion => FormatVersion(Assembly.GetExecutingAssembly().GetName().Version);

        public static string ReleasesPage => ReleasesPageUrl;

        // fires the one automatic check, from wherever the app has finished starting up
        // store builds never check: the store ships its own update path, and a packaged app is not allowed to
        // replace itself from outside it
        public void Start()
        {
            if (!AppDistribution.SupportsSelfUpdate) return;
            if (_hasCheckedOnStartup) return;

            _hasCheckedOnStartup = true;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            _ = CheckAsync();
        }

        public async Task<UpdateCheckResult> CheckAsync()
        {
            if (!AppDistribution.SupportsSelfUpdate) return UpdateCheckResult.UpToDate;

            _dispatcherQueue ??= DispatcherQueue.GetForCurrentThread();

            try
            {
                string json = await _http.GetStringAsync(LatestReleaseUrl);
                var info = ParseRelease(json);
                if (info == null) return UpdateCheckResult.UpToDate;

                IsUpdateAvailable = true;
                Latest = info;
                RaiseStateChanged();

                return UpdateCheckResult.UpdateAvailable;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] check failed: {ex.Message}");
                return UpdateCheckResult.Failed;
            }
        }

        // --- revisit: start page ---
        // deliberately forgets the dismissal on exit instead of persisting the version, because right now there is
        // no way back: no settings entry and no start page, so a remembered skip leaves the update unreachable
        // until the next release; that already stranded a test run
        // the dialog already labels this "Skip this version", so the UI promises the persistent behaviour and only
        // the storage is held back; once the start page can reopen this dialog, this goes back to writing
        // SettingsService.SkippedUpdateVersion (removed from AppSettingsData and SettingsService along with this
        // change) and CheckAsync gets its ignoreSkippedVersion parameter back for the explicit re-check
        public void DismissUntilRestart()
        {
            if (Latest == null) return;

            IsUpdateAvailable = false;
            RaiseStateChanged();
        }


        // === private helpers ===

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // GitHub rejects an api request without a user agent outright
            client.DefaultRequestHeaders.Add("User-Agent", "FluentSensors");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

            return client;
        }

        // returns null whenever the release is not actually newer, so the caller has one nullness check instead of a
        // separate comparison result
        private static UpdateInfo? ParseRelease(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            string tag = root.GetProperty("tag_name").GetString() ?? "";
            string version = tag.TrimStart('v', 'V');
            if (!Version.TryParse(version, out var releaseVersion)) return null;

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (currentVersion == null) return null;

            // both sides are cut to three parts before comparing: the tag parses as 1.3.0 with Revision -1 while the
            // assembly always carries a fourth component, and -1 sorts below 0 on every otherwise equal pair
            if (Normalize(releaseVersion) <= Normalize(currentVersion)) return null;

            string releaseUrl = root.TryGetProperty("html_url", out var htmlUrl)
                ? htmlUrl.GetString() ?? ReleasesPageUrl
                : ReleasesPageUrl;

            string wantedAsset = UpdateInstaller.PickAssetName(FormatVersion(releaseVersion));
            if (!root.TryGetProperty("assets", out var assets)) return null;

            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";
                if (!string.Equals(name, wantedAsset, StringComparison.OrdinalIgnoreCase)) continue;

                string url = asset.TryGetProperty("browser_download_url", out var urlElement)
                    ? urlElement.GetString() ?? ""
                    : "";
                if (string.IsNullOrEmpty(url)) continue;

                long size = asset.TryGetProperty("size", out var sizeElement) ? sizeElement.GetInt64() : 0;

                return new UpdateInfo(FormatVersion(releaseVersion), releaseUrl, name, url, size);
            }

            // a newer release whose matching asset is missing still counts as an update, the dialog then falls back
            // to opening the release page instead of downloading anything
            return new UpdateInfo(FormatVersion(releaseVersion), releaseUrl, "", "", 0);
        }

        private static Version Normalize(Version version) =>
            new Version(version.Major, version.Minor, Math.Max(version.Build, 0));

        private static string FormatVersion(Version? version) =>
            version == null ? "" : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

        // the check may resume off the UI thread, so the event is marshalled here once and consumers do not
        // dispatch again
        private void RaiseStateChanged()
        {
            var queue = _dispatcherQueue;
            if (queue == null || queue.HasThreadAccess)
            {
                UpdateStateChanged?.Invoke();
                return;
            }

            queue.TryEnqueue(() => UpdateStateChanged?.Invoke());
        }
    }
}
