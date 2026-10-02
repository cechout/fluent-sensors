using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.System;


namespace FluentSensors.Common
{
    // the distribution channel:
    // installer, portable or store, the one place to ask; installer and portable are the GitHub downloads and
    // differ only in where state lives
    // a packaged store build never runs its own updater: the store forbids it, the install folder is read only and
    // signed, and the inno installer would leave a second copy
    public static class AppDistribution
    {
        // === fields ===

        // portable mode: this marker next to the exe moves the state from %LocalAppData% into the app
        // folder; (only in the portable zip)
        public const string PortableMarkerFileName = "portable.txt";

        // the store id every deep link uses
        public const string StoreProductId = "9PK7F87MWXKF";

        // what GetCurrentPackageFullName returns without a package identity
        private const int AppmodelErrorNoPackage = 15700;

        private static readonly Lazy<bool> _isPackaged = new Lazy<bool>(DetectPackaged);
        private static readonly Lazy<bool> _isPortableBuild = new Lazy<bool>(DetectPortableBuild);


        // === public api ===

        public static bool IsPackaged => _isPackaged.Value;

        public static bool IsPortableBuild => _isPortableBuild.Value;

        // the in-app updater keys off this; (the store build updates through the store, see StoreUpdateSource)
        public static bool SupportsSelfUpdate => !IsPackaged;

        public static Task OpenStoreReviewAsync() =>
            LaunchStoreAsync(new Uri($"ms-windows-store://review/?ProductId={StoreProductId}"));

        // the product page, where an update can always be installed by hand
        public static Task OpenStorePageAsync() =>
            LaunchStoreAsync(new Uri($"ms-windows-store://pdp/?ProductId={StoreProductId}"));


        // === private helpers ===

        // Launcher first, the shell as the fallback; (from the elevated store build Launcher reaches the store, but its
        // answer may never come back, so nothing waits on it)
        private static async Task LaunchStoreAsync(Uri uri)
        {
            try
            {
                if (await Launcher.LaunchUriAsync(uri)) return;
            }
            catch { /* the shell below gets the next try */ }

            try
            {
                Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
            }
            catch { /* nothing left to try, the click simply does nothing */ }
        }

        // a zero length buffer probes the identity: ERROR_INSUFFICIENT_BUFFER when packaged, APPMODEL_ERROR_NO_PACKAGE
        // otherwise; (Package.Current would throw on every unpackaged start)
        private static bool DetectPackaged()
        {
            try
            {
                int length = 0;
                return GetCurrentPackageFullName(ref length, null) != AppmodelErrorNoPackage;
            }
            catch
            {
                // an unreachable api means no identity either
                return false;
            }
        }

        private static bool DetectPortableBuild()
        {
            try
            {
                string? appFolder = Path.GetDirectoryName(Environment.ProcessPath);
                if (string.IsNullOrEmpty(appFolder)) return false;

                return File.Exists(Path.Combine(appFolder, PortableMarkerFileName));
            }
            catch
            {
                // an unreadable app folder counts as installed (%LocalAppData%)
                return false;
            }
        }


        // === win32 api imports ===

        // DllImport, not LibraryImport; the generator cannot marshal the optional null buffer the probe needs
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
    }
}
