using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.System;


namespace FluentSensors.Common
{
    // tells the rest of the app which of the three shipping channels it is currently running as, so behaviour that
    // genuinely differs per channel has one place to ask
    //
    // installer build and portable build are the two GitHub downloads and only differ in where state is written;
    // a packaged build comes from the Microsoft Store and must never offer its own updater: the store forbids a
    // packaged app from updating itself outside the store, the install directory is read only and signature
    // protected, and running the inno installer from inside it would leave a second unpackaged copy behind
    public static class AppDistribution
    {
        // === fields ===

        // portable mode: this marker file next to the exe moves persistence from %LocalAppData% into the app folder
        // the marker ships only in the portable zip, installer builds never contain it
        public const string PortableMarkerFileName = "portable.txt";

        // the id the Microsoft Store knows this app by, which every store deep link addresses it with
        public const string StoreProductId = "9PK7F87MWXKF";

        // no package identity at all; the documented return of GetCurrentPackageFullName for an unpackaged process
        private const int AppmodelErrorNoPackage = 15700;

        private static readonly Lazy<bool> _isPackaged = new Lazy<bool>(DetectPackaged);
        private static readonly Lazy<bool> _isPortableBuild = new Lazy<bool>(DetectPortableBuild);


        // === public api ===

        public static bool IsPackaged => _isPackaged.Value;

        public static bool IsPortableBuild => _isPortableBuild.Value;

        // the store ships its own update path, so every part of the in-app updater keys off this
        public static bool SupportsSelfUpdate => !IsPackaged;

        // opens the review form of this app in the Microsoft Store
        //
        // Launcher first and the shell as the fallback: this process runs elevated, and whether a protocol launch
        // gets from there to the store is unverified
        public static async Task OpenStoreReviewAsync()
        {
            var uri = new Uri($"ms-windows-store://review/?ProductId={StoreProductId}");

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


        // === private helpers ===

        // asking for the name with a zero length buffer is the cheap identity probe: a packaged process answers
        // ERROR_INSUFFICIENT_BUFFER, an unpackaged one answers APPMODEL_ERROR_NO_PACKAGE
        // deliberately not Package.Current in a try/catch, since that throws on every unpackaged start
        private static bool DetectPackaged()
        {
            try
            {
                int length = 0;
                return GetCurrentPackageFullName(ref length, null) != AppmodelErrorNoPackage;
            }
            catch
            {
                // an api that cannot be reached at all means no package identity either
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
                // an unreadable app folder is treated as the installed layout, which writes to %LocalAppData%
                return false;
            }
        }


        // === win32 api imports ===

        // DllImport instead of LibraryImport, deliberate exception:
        // the source generator has no marshalling for the optional null output buffer this probe relies on
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
    }
}
