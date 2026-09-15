using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using FluentSensors.Common;


namespace FluentSensors.Core.Update
{
    // downloads the release asset that matches this build and hands the actual replacement over to a detached
    // powershell, because a process cannot overwrite or reinstall itself while it is still running
    //
    // the two GitHub channels need different treatment; copying the portable zip over an installed build would
    // leave the inno uninstall entry pointing at the old version, and the portable.txt inside that zip would
    // silently move an installed builds settings into the program files folder
    public static class UpdateInstaller
    {
        // === fields ===

        private const string InstallerAssetName = "FluentSensors_Installer.exe";
        private const string PortableAssetPrefix = "FluentSensors_Portable_";
        private const string DownloadFolderName = "FluentSensors_Update";

        // separate from the check client: a 97 MB portable zip does not fit in the ten second api timeout
        private static readonly HttpClient _http = CreateClient();


        // === public api ===

        public static string PickAssetName(string version) =>
            AppDistribution.IsPortableBuild ? $"{PortableAssetPrefix}{version}.zip" : InstallerAssetName;

        public static string DownloadFolder =>
            Path.Combine(Path.GetTempPath(), DownloadFolderName);

        // streams the asset to disk and reports 0 to 1 along the way; returns null when the release carries no
        // matching asset, which leaves the caller to open the release page instead
        public static async Task<string?> DownloadAsync(UpdateInfo? info, IProgress<double>? progress, CancellationToken ct)
        {
            if (info == null || string.IsNullOrEmpty(info.AssetUrl)) return null;

            Directory.CreateDirectory(DownloadFolder);
            string targetPath = Path.Combine(DownloadFolder, info.AssetName);

            using var response = await _http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? info.AssetSize;
            long received = 0;

            try
            {
                using (var source = await response.Content.ReadAsStreamAsync(ct))
                using (var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    byte[] buffer = new byte[81920];
                    int read;

                    while ((read = await source.ReadAsync(buffer, ct)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), ct);
                        received += read;

                        if (total > 0) progress?.Report((double)received / total);
                    }
                }
            }
            catch
            {
                // a cancelled or failed transfer leaves a partial file of up to a hundred megabytes sitting in
                // %TEMP% until Windows gets around to it
                TryDeletePartial(targetPath);
                throw;
            }

            return targetPath;
        }

        // starts the handoff script and returns immediately; the caller is expected to exit the app right after,
        // the script waits for exactly that before touching anything
        public static void ApplyAndRestart(string downloadedPath)
        {
            string? exePath = Environment.ProcessPath;
            string? appFolder = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(exePath) || string.IsNullOrEmpty(appFolder)) return;

            bool portable = AppDistribution.IsPortableBuild;
            string script = portable
                ? BuildPortableScript(downloadedPath, appFolder, exePath)
                : BuildInstallerScript(downloadedPath, exePath);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                UseShellExecute = false,

                // no console at any point; the installer path shows innos own progress window instead, and the
                // portable path is silent between the app closing and reopening, which is why the extraction below
                // avoids the slow cmdlet
                CreateNoWindow = true,
                WorkingDirectory = DownloadFolder
            };

            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);

            Process.Start(psi);
        }


        // === private helpers ===

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.Add("User-Agent", "FluentSensors");

            return client;
        }

        // the zip carries a single top level folder because the release workflow compresses the staging directory
        // itself rather than its contents, so the copy has to come out of that inner folder
        //
        // ExtractToDirectory rather than Expand-Archive: the cmdlet is very slow across the roughly one thousand
        // files of a publish output, and nothing is on screen while it runs, so every second of it reads as a
        // crashed app to whoever is waiting
        //
        // every path is single quoted with embedded quotes doubled, and the exe is relaunched from finally so a
        // half-failed copy still leaves the user with a running app rather than nothing
        private static string BuildPortableScript(string zipPath, string appFolder, string exePath)
        {
            string extractPath = Path.Combine(DownloadFolder, "extract");

            return "$ErrorActionPreference='Stop'; " +
                   "try { " +
                   "Wait-Process -Name 'FluentSensors' -ErrorAction SilentlyContinue; " +
                   $"if (Test-Path -LiteralPath {Quote(extractPath)}) {{ Remove-Item -LiteralPath {Quote(extractPath)} -Recurse -Force }}; " +
                   "Add-Type -AssemblyName System.IO.Compression.FileSystem; " +
                   $"[System.IO.Compression.ZipFile]::ExtractToDirectory({Quote(zipPath)}, {Quote(extractPath)}); " +
                   $"$inner = Get-ChildItem -LiteralPath {Quote(extractPath)} -Directory | Select-Object -First 1; " +
                   $"$source = if ($inner) {{ $inner.FullName }} else {{ {Quote(extractPath)} }}; " +
                   $"Copy-Item -Path (Join-Path $source '*') -Destination {Quote(appFolder)} -Recurse -Force; " +
                   $"Remove-Item -LiteralPath {Quote(extractPath)} -Recurse -Force; " +
                   $"Remove-Item -LiteralPath {Quote(zipPath)} -Force " +
                   "} " +
                   $"catch {{ Start-Process explorer.exe {Quote(DownloadFolder)} }} " +
                   $"finally {{ Start-Process -FilePath {Quote(exePath)} }}";
        }

        // /SILENT rather than /VERYSILENT so inno still shows its progress window; the apps own window is gone by
        // then and a completely invisible minute of nothing looks like a crash
        // the .iss marks its post install launch skipifsilent, so the relaunch has to happen here
        private static string BuildInstallerScript(string installerPath, string exePath)
        {
            return "$ErrorActionPreference='Stop'; " +
                   "try { " +
                   "Wait-Process -Name 'FluentSensors' -ErrorAction SilentlyContinue; " +
                   $"Start-Process -FilePath {Quote(installerPath)} -ArgumentList '/SILENT','/SUPPRESSMSGBOXES','/NORESTART','/CLOSEAPPLICATIONS' -Wait; " +
                   $"Remove-Item -LiteralPath {Quote(installerPath)} -Force -ErrorAction SilentlyContinue " +
                   "} " +
                   $"catch {{ Start-Process explorer.exe {Quote(DownloadFolder)} }} " +
                   $"finally {{ Start-Process -FilePath {Quote(exePath)} }}";
        }

        private static void TryDeletePartial(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* still locked or already gone; the next attempt truncates it anyway */ }
        }

        private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
    }
}
