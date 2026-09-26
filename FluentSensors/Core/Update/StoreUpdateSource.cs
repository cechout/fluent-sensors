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
        Waiting, // queued in the store, nothing moving yet
        Downloading,
        Installing // the store is replacing the package, which ends this process
    }

    public record StoreInstallProgress(StoreInstallPhase Phase, double Fraction);

    public enum StoreInstallResult
    {
        Installed, // rarely seen, the install normally ends this process before the answer arrives
        Canceled,
        Failed // store error, low battery, metered connection or background installs turned off in the store
    }


    // the update path of the packaged build: asks the Microsoft Store whether it holds a newer package of this app,
    // and installs it in place
    //
    // the store only answers whether an update exists, never which version it is; naming the version and showing
    // its notes is left to UpdateService, which reads them from the matching GitHub release
    public static partial class StoreUpdateSource
    {
        // === fields ===

        // restart only after an update, never after a crash, a hang or a reboot
        private const uint RestartNoCrash = 0x1;
        private const uint RestartNoHang = 0x2;
        private const uint RestartNoReboot = 0x8;

        private static StoreContext? _context;
        private static IReadOnlyList<StorePackageUpdate>? _updates;


        // === public api ===

        // throws when the store cannot be reached, which UpdateService turns into a failed check
        // ownerWindow is what the store context is tied to, see GetContext
        public static async Task<bool> HasUpdateAsync(nint ownerWindow)
        {
            var context = GetContext(ownerWindow);

            _updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
            return _updates.Count > 0;
        }

        // --- workaround: store consent dialog closes in an elevated app ---
        // problem: the consent dialog of RequestDownloadAndInstallStorePackageUpdatesAsync closes the moment it
        // opens when the app runs elevated, and the call reports Canceled before the user saw anything; the same
        // fault is reported for the store purchase dialog, still open with no workaround:
        // https://github.com/microsoft/microsoft-ui-xaml/issues/10538
        // fix: install silently instead; the consent is our own update dialog, the one the GitHub builds show too
        //
        // the store ends this process to replace the package, so the app registers for a restart first and is
        // started again once the new version is in place
        // installs what the last HasUpdateAsync found, on the context that call set up
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

        // a desktop app has no CoreWindow of its own, so the store context is tied to the main window; the spike that
        // verified this whole path from the elevated packaged build ran with it
        private static StoreContext GetContext(nint ownerWindow)
        {
            if (_context != null) return _context;

            var context = StoreContext.GetDefault();
            InitializeWithWindow.Initialize(context, ownerWindow);

            _context = context;
            return context;
        }

        // the error states are left out, the overall result of the install reports those
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
