using Microsoft.UI.Dispatching;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

using FluentSensors.Common;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Core.Update
{
    // everything the app knows about a newer release: the bare version for display and comparison, the page to send
    // a user to, and the one asset that matches this builds distribution channel
    public record UpdateInfo(
        string Version, // three part, no leading v, e.g. "1.3.0"
        string ReleaseUrl,
        string Notes, // the raw markdown body of the release, what the start pages notes reader renders
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


    // what the start pages update button shows; wider than UpdateCheckResult because that one answers a single
    // call while this one also has to describe the states between and around calls
    public enum UpdateUiState
    {
        Unknown, // nothing asked yet, either because the startup check is off or because it has not run
        Checking,
        UpToDate,
        UpdateAvailable,
        Skipped, // a newer release exists but the user chose to skip this exact version
        Failed,
        StoreManaged // packaged build; the store owns updates and this app never checks on its own
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

        // whatever the api last answered, newer than this build or not; the notes reader needs the release even
        // when there is nothing to install, since an up to date app shows the notes of the version it is running
        private UpdateInfo? _latestRelease;
        private bool _isNewer;


        // === singleton instance ===

        private static readonly UpdateService _instance = new UpdateService();
        public static UpdateService Instance => _instance;

        private UpdateService() { }


        // === public api ===

        // raised from the UI thread whenever IsUpdateAvailable or Latest moves, which is both the moment a newer
        // release is found and the moment one is skipped; a single event keeps the title bar and the settings page
        // in step no matter which of the two triggered the change
        public event Action? UpdateStateChanged;

        // what the start pages update button renders; the title bar pill only cares about IsUpdateAvailable below
        public UpdateUiState UiState { get; private set; } = UpdateUiState.Unknown;

        // the last check that actually reached GitHub; a failed one deliberately leaves this alone, so the button
        // keeps naming the last answer it really got
        public DateTimeOffset? LastCheckedAt { get; private set; }

        public bool IsUpdateAvailable => UiState == UpdateUiState.UpdateAvailable;

        // the release this app would install; null unless one is genuinely newer and not skipped, which is what
        // keeps the pill and the update dialog off a version the user already declined
        public UpdateInfo? Latest => _isNewer && UiState != UpdateUiState.Skipped ? _latestRelease : null;

        // the release the notes reader shows, newer or not; on an up to date app this is the running version
        public UpdateInfo? LatestRelease => _latestRelease;

        public static string CurrentVersion => FormatVersion(Assembly.GetExecutingAssembly().GetName().Version);

        public static string ReleasesPage => ReleasesPageUrl;

        // fires the one automatic check, from wherever the app has finished starting up
        // store builds never check: the store ships its own update path, and a packaged app is not allowed to
        // replace itself from outside it
        // both guards live here rather than at the call site so the whole policy on when this app reaches out sits
        // in one place
        public void Start()
        {
            if (!AppDistribution.SupportsSelfUpdate)
            {
                UiState = UpdateUiState.StoreManaged;
                return;
            }

            if (!SettingsService.Instance.CheckUpdatesOnStartup) return;
            if (_hasCheckedOnStartup) return;

            _hasCheckedOnStartup = true;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            _ = CheckAsync();
        }

        // ignoreSkippedVersion is what the start pages update button passes when the user explicitly asks to see a
        // version they skipped earlier
        public async Task<UpdateCheckResult> CheckAsync(bool ignoreSkippedVersion = false)
        {
            if (!AppDistribution.SupportsSelfUpdate)
            {
                UiState = UpdateUiState.StoreManaged;
                return UpdateCheckResult.UpToDate;
            }

            _dispatcherQueue ??= DispatcherQueue.GetForCurrentThread();

            UiState = UpdateUiState.Checking;
            RaiseStateChanged();

            try
            {
                string json = await _http.GetStringAsync(LatestReleaseUrl);

                _latestRelease = ParseRelease(json, out bool isNewer);
                _isNewer = isNewer;
                LastCheckedAt = DateTimeOffset.Now;

                if (_latestRelease == null || !isNewer)
                {
                    UiState = UpdateUiState.UpToDate;
                    RaiseStateChanged();
                    return UpdateCheckResult.UpToDate;
                }

                // a skipped version stays out of the pill and out of the dialog, but the release itself is kept
                // so the notes reader and the way back out of the skip both still have something to work with
                bool skipped = !ignoreSkippedVersion && IsSkipped(_latestRelease.Version);
                if (skipped)
                {
                    UiState = UpdateUiState.Skipped;
                    RaiseStateChanged();
                    return UpdateCheckResult.UpToDate;
                }

                UiState = UpdateUiState.UpdateAvailable;
                RaiseStateChanged();

                return UpdateCheckResult.UpdateAvailable;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] check failed: {ex.Message}");

                UiState = UpdateUiState.Failed;
                RaiseStateChanged();

                return UpdateCheckResult.Failed;
            }
        }

        // the notes reader is a plain read of a public release page, so it runs on every channel including a store
        // build, and only when the user actually opens it; a store build therefore still reaches GitHub exactly
        // never unless someone asks it to
        //
        // deliberately does not touch UiState on a store build: finding a newer release there must not turn into an
        // offer to install one, the store owns that
        public async Task<UpdateInfo?> EnsureLatestReleaseAsync()
        {
            if (_latestRelease != null) return _latestRelease;

            try
            {
                string json = await _http.GetStringAsync(LatestReleaseUrl);

                _latestRelease = ParseRelease(json, out bool isNewer);

                if (AppDistribution.SupportsSelfUpdate)
                {
                    _isNewer = isNewer;
                    LastCheckedAt = DateTimeOffset.Now;

                    UiState = !isNewer || _latestRelease == null ? UpdateUiState.UpToDate
                        : IsSkipped(_latestRelease.Version) ? UpdateUiState.Skipped
                        : UpdateUiState.UpdateAvailable;

                    RaiseStateChanged();
                }

                return _latestRelease;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] notes fetch failed: {ex.Message}");
                return null;
            }
        }

        // remembers the version across restarts, which is what the dialogs "Skip this version" label has always
        // promised
        //
        // safe to persist only because the start pages update button is the way back out: it names the skipped
        // version and clears it again, so a skip can no longer strand a release until the one after it
        public void SkipVersion()
        {
            if (_latestRelease == null || !_isNewer) return;

            SettingsService.Instance.SkippedUpdateVersion = _latestRelease.Version;

            UiState = UpdateUiState.Skipped;
            RaiseStateChanged();
        }

        // the counterpart the start page offers once a version is skipped
        public void ClearSkippedVersion()
        {
            SettingsService.Instance.SkippedUpdateVersion = "";

            if (_isNewer && _latestRelease != null) UiState = UpdateUiState.UpdateAvailable;
            RaiseStateChanged();
        }

        public string SkippedVersion => SettingsService.Instance.SkippedUpdateVersion;


        // === private helpers ===

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // GitHub rejects an api request without a user agent outright
            client.DefaultRequestHeaders.Add("User-Agent", "FluentSensors");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

            return client;
        }

        // hands back whatever the api described rather than only an installable update, because the notes reader
        // needs a release even when the app is current; whether it is worth installing is the separate isNewer answer
        // null is reserved for a response that is not a usable release at all
        private static UpdateInfo? ParseRelease(string json, out bool isNewer)
        {
            isNewer = false;

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            string tag = root.GetProperty("tag_name").GetString() ?? "";
            string version = tag.TrimStart('v', 'V');
            if (!Version.TryParse(version, out var releaseVersion)) return null;

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (currentVersion == null) return null;

            // both sides are cut to three parts before comparing: the tag parses as 1.3.0 with Revision -1 while the
            // assembly always carries a fourth component, and -1 sorts below 0 on every otherwise equal pair
            isNewer = Normalize(releaseVersion) > Normalize(currentVersion);

            string releaseUrl = root.TryGetProperty("html_url", out var htmlUrl)
                ? htmlUrl.GetString() ?? ReleasesPageUrl
                : ReleasesPageUrl;

            string notes = root.TryGetProperty("body", out var bodyElement)
                ? bodyElement.GetString() ?? ""
                : "";

            string formattedVersion = FormatVersion(releaseVersion);
            string wantedAsset = UpdateInstaller.PickAssetName(formattedVersion);

            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";
                    if (!string.Equals(name, wantedAsset, StringComparison.OrdinalIgnoreCase)) continue;

                    string url = asset.TryGetProperty("browser_download_url", out var urlElement)
                        ? urlElement.GetString() ?? ""
                        : "";
                    if (string.IsNullOrEmpty(url)) continue;

                    long size = asset.TryGetProperty("size", out var sizeElement) ? sizeElement.GetInt64() : 0;

                    return new UpdateInfo(formattedVersion, releaseUrl, notes, name, url, size);
                }
            }

            // a newer release whose matching asset is missing still counts as an update, the dialog then falls back
            // to opening the release page instead of downloading anything
            return new UpdateInfo(formattedVersion, releaseUrl, notes, "", "", 0);
        }

        private static bool IsSkipped(string version) =>
            !string.IsNullOrEmpty(version)
            && string.Equals(SettingsService.Instance.SkippedUpdateVersion, version, StringComparison.OrdinalIgnoreCase);

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
