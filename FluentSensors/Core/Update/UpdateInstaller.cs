using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using FluentSensors.Common;


namespace FluentSensors.Core.Update
{
    // the update installer:
    // downloads the asset of this build and hands the replacement to a detached powershell, since a running
    // process cannot replace itself
    // the two GitHub channels differ: the portable zip over an installed build would leave a stale uninstall entry and
    // move the settings into program files (portable.txt)
    public static class UpdateInstaller
    {
        // === fields ===

        private const string InstallerAssetName = "FluentSensors_Installer.exe";
        private const string PortableAssetPrefix = "FluentSensors_Portable_";
        private const string DownloadFolderName = "FluentSensors_Update";

        // apart from the check client; a portable zip of about 100 MB outlasts the ten second api timeout
        private static readonly HttpClient _http = CreateClient();


        // === public api ===

        public static string PickAssetName(string version) =>
            AppDistribution.IsPortableBuild ? $"{PortableAssetPrefix}{version}.zip" : InstallerAssetName;

        public static string DownloadFolder =>
            Path.Combine(Path.GetTempPath(), DownloadFolderName);

        // streams the asset to disk with progress 0 to 1; null without a matching asset (the
        // caller opens the release page)
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
                // or a partial file of up to 100 MB sits in %TEMP%
                TryDeletePartial(targetPath);
                throw;
            }

            return targetPath;
        }

        // starts the handoff script; the caller exits right after, which the script waits for
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

                // no console; the installer shows the inno progress, the portable path is silent (why its
                // extraction avoids the slow cmdlet)
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

        // the zip has one top level folder (the workflow compresses the staging folder), so the copy comes out of it
        // ExtractToDirectory, not Expand-Archive, which is slow over a thousand files with nothing on screen
        // paths single quoted with quotes doubled; the relaunch sits in finally, so a half-failed
        // copy still leaves a running app
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

        // /SILENT, not /VERYSILENT, so the inno progress shows (an invisible minute looks like a crash); the .iss
        // launch is skipifsilent, so the relaunch happens here
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
