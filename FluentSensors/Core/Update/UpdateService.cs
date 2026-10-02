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
    // a release: the bare version, its page, and the asset matching this distribution channel
    public record UpdateInfo(
        string Version, // three part, no v, e.g. "1.3.0"
        string ReleaseUrl,
        string Notes, // the raw markdown body, for the notes reader
        string AssetName,
        string AssetUrl,
        long AssetSize
    );


    public enum UpdateCheckResult
    {
        UpToDate,
        UpdateAvailable,
        Failed // no network, rate limited, malformed response; the reason is never shown
    }


    // what the start page update button shows; (wider than UpdateCheckResult, it covers the states between calls)
    public enum UpdateUiState
    {
        Unknown, // nothing asked yet (startup check off, or not run yet)
        Checking,
        UpToDate,
        UpdateAvailable,
        Skipped, // a newer release the user skipped
        Failed
    }


    // the update service:
    // asks the GitHub releases api once per app start for a newer version, for the title bar and the start page; the
    // api returns published releases only, so a release workflow draft never reaches a user
    // the store build asks the Microsoft Store instead (see StoreUpdateSource); GitHub only names the version there,
    // since a GitHub release can go public before the store version clears certification
    public class UpdateService
    {
        // === fields ===

        private const string LatestReleaseUrl = "https://api.github.com/repos/cechout/fluent-sensors/releases/latest";
        private const string ReleasesPageUrl = "https://github.com/cechout/fluent-sensors/releases";

        // one client per process; a per-call using would leave a socket in TIME_WAIT per check
        // no HTTP/3: the csproj prunes msquic.dll from the published build, so raising the request version from
        // 1.1 would only fail in release
        private static readonly HttpClient _http = CreateClient();

        // GitHub allows 60 unauthenticated requests per hour and ip, shared with the release notes dialog, so
        // a manual check inside this window gets the last result; (a failed check leaves LastCheckedAt alone,
        // a retry is never held back)
        private static readonly TimeSpan CheckCooldown = TimeSpan.FromSeconds(60);

        // shared with ReleaseCatalog, so the headers GitHub requires exist once
        internal static HttpClient Http => _http;

        private DispatcherQueue? _dispatcherQueue;
        private bool _hasCheckedOnStartup;

        // the main window, which the store context is tied to
        private nint _ownerWindow;

        // the last answer, newer or not; an up to date app shows the notes of its own version
        private UpdateInfo? _latestRelease;
        private bool _isNewer;


        // === singleton instance ===

        private static readonly UpdateService _instance = new UpdateService();
        public static UpdateService Instance => _instance;

        private UpdateService() { }


        // === public api ===

        // on the UI thread whenever IsUpdateAvailable or Latest moves (found or skipped); one event keeps
        // title bar and start page in step
        public event Action? UpdateStateChanged;

        // the start page update button; the title bar pill only reads IsUpdateAvailable
        public UpdateUiState UiState { get; private set; } = UpdateUiState.Unknown;

        // the last check that reached GitHub; a failed one leaves it alone
        public DateTimeOffset? LastCheckedAt { get; private set; }

        public bool IsUpdateAvailable => UiState == UpdateUiState.UpdateAvailable;

        // the release to install; null unless newer and not skipped, which keeps pill and dialog off a declined version
        public UpdateInfo? Latest => _isNewer && UiState != UpdateUiState.Skipped ? _latestRelease : null;

        // the release the notes reader shows, newer or not
        public UpdateInfo? LatestRelease => _latestRelease;

        public static string CurrentVersion => FormatVersion(Assembly.GetExecutingAssembly().GetName().Version);

        // every version in front of the user, with the v (v1.3.0); internally the bare string stays (asset names, the
        // skipped version, comparisons)
        public static string VersionLabel(string? version) =>
            string.IsNullOrWhiteSpace(version) ? "" : $"v{version.TrimStart('v', 'V')}";

        public static string ReleasesPage => ReleasesPageUrl;

        // the one automatic check, once the app has started; the guards live here, so the policy on reaching out sits
        // in one place (the window is kept even with the check off, the start page checks later)
        public void Start(nint ownerWindow)
        {
            _ownerWindow = ownerWindow;

            if (!SettingsService.Instance.CheckUpdatesOnStartup) return;
            if (_hasCheckedOnStartup) return;

            _hasCheckedOnStartup = true;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            _ = CheckAsync();
        }

        // ignoreSkippedVersion, from the start page button when the user asks for a skipped version
        public async Task<UpdateCheckResult> CheckAsync(bool ignoreSkippedVersion = false)
        {
            _dispatcherQueue ??= DispatcherQueue.GetForCurrentThread();

            // see CheckCooldown; the state stays as the last answer left it
            if (LastCheckedAt.HasValue && DateTimeOffset.Now - LastCheckedAt.Value < CheckCooldown)
            {
                return UiState == UpdateUiState.UpdateAvailable
                    ? UpdateCheckResult.UpdateAvailable
                    : UpdateCheckResult.UpToDate;
            }

            UiState = UpdateUiState.Checking;
            RaiseStateChanged();

            try
            {
                var (parsed, isNewer) = AppDistribution.SupportsSelfUpdate
                    ? await FetchLatestReleaseAsync()
                    : await AskStoreAsync();

                _latestRelease = Simulate(parsed, ref isNewer);
                _isNewer = isNewer;
                LastCheckedAt = DateTimeOffset.Now;

                if (_latestRelease == null || !isNewer)
                {
                    UiState = UpdateUiState.UpToDate;
                    RaiseStateChanged();
                    return UpdateCheckResult.UpToDate;
                }

                // a skipped version stays out of pill and dialog; the release is kept for the
                // notes reader and the way back
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

        // persisted across restarts, as "Skip this version" promises; safe because the start page button names the
        // skipped version and clears it
        public void SkipVersion()
        {
            if (_latestRelease == null || !_isNewer) return;
            if (string.IsNullOrEmpty(_latestRelease.Version)) return; // a store update without a GitHub name

            SettingsService.Instance.SkippedUpdateVersion = _latestRelease.Version;

            UiState = UpdateUiState.Skipped;
            RaiseStateChanged();
        }

        // the way back the start page offers
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

            // GitHub rejects a request without a user agent
            client.DefaultRequestHeaders.Add("User-Agent", "FluentSensors");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

            return client;
        }

        private static async Task<(UpdateInfo? Release, bool IsNewer)> FetchLatestReleaseAsync()
        {
            string json = await _http.GetStringAsync(LatestReleaseUrl);

            var release = ParseRelease(json, out bool isNewer);
            return (release, isNewer);
        }

        // the store build: the store decides, the latest GitHub release only names it, and only while newer
        // than this build (otherwise the update has no version); with nothing on offer a GitHub release
        // ahead of the store is dropped
        private async Task<(UpdateInfo? Release, bool IsNewer)> AskStoreAsync()
        {
            var storeCheck = StoreUpdateSource.HasUpdateAsync(_ownerWindow);

            UpdateInfo? release = null;
            bool isNewer = false;

            try
            {
                (release, isNewer) = await FetchLatestReleaseAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] release lookup failed: {ex.Message}");
            }

            // an unreachable store fails the check, so this one throws
            bool hasUpdate = await storeCheck;

            if (!hasUpdate) return (isNewer ? null : release, false);

            return isNewer && release != null
                ? (release, true)
                : (new UpdateInfo("", ReleasesPageUrl, "", "", "", 0), true);
        }

        // debug only: a simulatedVersion above the running one makes every check report that release, with the real
        // notes, url and asset; compiled out of release
        private static UpdateInfo? Simulate(UpdateInfo? release, ref bool isNewer)
        {
#if DEBUG
            const string simulatedVersion = ""; // e.g. "9.9.9"; empty = off

            if (simulatedVersion.Length > 0 && release != null)
            {
                Debug.WriteLine($"[UpdateService] simulating an available update: {simulatedVersion}");

                isNewer = true;
                return release with { Version = simulatedVersion };
            }
#endif

            return release;
        }

        // whatever the api described, installable or not (isNewer says that); null only for an unusable response
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

            // both cut to three parts; the tag parses with Revision -1, the assembly has a
            // fourth part, and -1 sorts below 0
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

            // a release without the matching asset still counts; the dialog opens the release page instead
            return new UpdateInfo(formattedVersion, releaseUrl, notes, "", "", 0);
        }

        private static bool IsSkipped(string version) =>
            !string.IsNullOrEmpty(version)
            && string.Equals(SettingsService.Instance.SkippedUpdateVersion, version, StringComparison.OrdinalIgnoreCase);

        private static Version Normalize(Version version) =>
            new Version(version.Major, version.Minor, Math.Max(version.Build, 0));

        private static string FormatVersion(Version? version) =>
            version == null ? "" : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

        // the check may resume off the UI thread; marshalled once here, consumers do not dispatch again
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
