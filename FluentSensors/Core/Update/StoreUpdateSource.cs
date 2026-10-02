using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Services.Store;
using WinRT.Interop;


namespace FluentSensors.Core.Update
{
    public enum StoreInstallPhase
    {
        Waiting, // queued, nothing moving yet
        Downloading,
        Installing // replacing the package ends this process
    }

    public record StoreInstallProgress(StoreInstallPhase Phase, double Fraction);

    public enum StoreInstallResult
    {
        Installed, // rare, the install usually ends the process first
        Canceled,
        Failed // store error, low battery, metered connection, background installs off
    }


    // the store update source:
    // asks the Microsoft Store for a newer package and installs it; the store never names the version,
    // UpdateService takes that from GitHub
    public static partial class StoreUpdateSource
    {
        // === fields ===

        // a restart after an update only, not after a crash, a hang or a reboot
        private const uint RestartNoCrash = 0x1;
        private const uint RestartNoHang = 0x2;
        private const uint RestartNoReboot = 0x8;

        private static StoreContext? _context;
        private static IReadOnlyList<StorePackageUpdate>? _updates;


        // === public api ===

        // throws on an unreachable store (a failed check); ownerWindow carries the store context, see GetContext
        public static async Task<bool> HasUpdateAsync(nint ownerWindow)
        {
            var context = GetContext(ownerWindow);

            _updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
            return _updates.Count > 0;
        }

        // --- workaround: store consent dialog closes in an elevated app ---
        // problem: the consent dialog of RequestDownloadAndInstallStorePackageUpdatesAsync closes at once in an
        // elevated app and the call reports Canceled; the store purchase dialog has the same open fault:
        // https://github.com/microsoft/microsoft-ui-xaml/issues/10538
        // fix: a silent install; our own update dialog is the consent, as in the GitHub builds
        // the store ends the process to replace the package, so the app registers for a restart first; installs what
        // the last HasUpdateAsync found, on its context
        public static async Task<StoreInstallResult> InstallAsync(IProgress<StoreInstallProgress> progress, CancellationToken ct)
        {
            var context = _context;
            if (context == null || _updates == null || _updates.Count == 0) return StoreInstallResult.Failed;
            if (!context.CanSilentlyDownloadStorePackageUpdates) return StoreInstallResult.Failed;

            RegisterApplicationRestart(null, RestartNoCrash | RestartNoHang | RestartNoReboot);

            var reporter = new Progress<StorePackageUpdateStatus>(status =>
            {
                var phase = ToPhase(status.PackageUpdateState);
                if (phase.HasValue) progress.Report(new StoreInstallProgress(phase.Value, status.PackageDownloadProgress));
            });

            try
            {
                var result = await context.TrySilentDownloadAndInstallStorePackageUpdatesAsync(_updates).AsTask(ct, reporter);

                switch (result.OverallState)
                {
                    case StorePackageUpdateState.Completed:
                        return StoreInstallResult.Installed;

                    case StorePackageUpdateState.Canceled:
                        UnregisterApplicationRestart();
                        return StoreInstallResult.Canceled;

                    default:
                        UnregisterApplicationRestart();
                        return StoreInstallResult.Failed;
                }
            }
            catch (OperationCanceledException)
            {
                UnregisterApplicationRestart();
                return StoreInstallResult.Canceled;
            }
            catch
            {
                UnregisterApplicationRestart();
                throw;
            }
        }


        // === private helpers ===

        // without a CoreWindow the store context is tied to the main window (verified from the elevated packaged build)
        private static StoreContext GetContext(nint ownerWindow)
        {
            if (_context != null) return _context;

            var context = StoreContext.GetDefault();
            InitializeWithWindow.Initialize(context, ownerWindow);

            _context = context;
            return context;
        }

        // no error states, the install result reports those
        private static StoreInstallPhase? ToPhase(StorePackageUpdateState state) => state switch
        {
            StorePackageUpdateState.Pending => StoreInstallPhase.Waiting,
            StorePackageUpdateState.Downloading => StoreInstallPhase.Downloading,
            StorePackageUpdateState.Deploying => StoreInstallPhase.Installing,
            StorePackageUpdateState.Completed => StoreInstallPhase.Installing,
            _ => null
        };


        // === win32 api imports ===

        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int RegisterApplicationRestart(string? commandLine, uint flags);

        [LibraryImport("kernel32.dll")]
        private static partial int UnregisterApplicationRestart();
    }
}
