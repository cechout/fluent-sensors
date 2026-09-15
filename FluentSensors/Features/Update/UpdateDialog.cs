using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using FluentSensors.Core.Update;


namespace FluentSensors.Features.Update
{
    // the confirmation step between spotting a new release and replacing the app with it
    //
    // built in code rather than in XAML because every other dialog in this app is (see ShowInfoDialog and
    // ConfirmAction on the settings page), and because the same instance has to survive the download: the primary
    // button cancels its own close so the progress bar can take over the dialog that is already on screen
    public static class UpdateDialog
    {
        // === public api ===

        public static async Task ShowAsync(XamlRoot? xamlRoot, UpdateInfo? info)
        {
            if (xamlRoot == null || info == null) return;

            var statusText = new TextBlock
            {
                Margin = new Thickness(0, 12, 0, 0),
                Visibility = Visibility.Collapsed,
                TextWrapping = TextWrapping.Wrap
            };

            var progress = new ProgressBar
            {
                Margin = new Thickness(0, 8, 0, 0),
                Maximum = 1,
                Visibility = Visibility.Collapsed
            };

            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = $"Fluent Sensors {info.Version} is available. You are running {UpdateService.CurrentVersion}.",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(new HyperlinkButton
            {
                Content = "What changed",
                Margin = new Thickness(-12, 4, 0, 0),
                NavigateUri = new Uri(info.ReleaseUrl)
            });
            content.Children.Add(progress);
            content.Children.Add(statusText);

            var dialog = new ContentDialog
            {
                Title = "Update available",
                Content = content,
                PrimaryButtonText = "Download and install",
                SecondaryButtonText = "Skip this version",
                CloseButtonText = "Later",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot
            };

            bool isDownloading = false;

            dialog.PrimaryButtonClick += async (_, args) =>
            {
                // the dialog stays on screen and turns into the progress readout instead of closing
                args.Cancel = true;
                var deferral = args.GetDeferral();

                isDownloading = true;
                dialog.IsPrimaryButtonEnabled = false;
                dialog.IsSecondaryButtonEnabled = false;
                progress.Visibility = Visibility.Visible;
                statusText.Visibility = Visibility.Visible;
                statusText.Text = $"Downloading {info.AssetName}...";

                bool handedOver = await RunUpdateAsync(info, progress, statusText);

                isDownloading = false;
                deferral.Complete();

                if (handedOver)
                {
                    // the replacement script is already waiting for this process to disappear
                    dialog.Hide();
                    FluentSensors.MainWindow.CurrentInstance?.ForceExit();
                    return;
                }

                dialog.IsPrimaryButtonEnabled = true;
                dialog.IsSecondaryButtonEnabled = true;
                progress.Visibility = Visibility.Collapsed;
            };

            dialog.SecondaryButtonClick += (_, _) => UpdateService.Instance.SkipCurrentUpdate();

            // "Later" would otherwise abandon a running download without stopping it
            dialog.CloseButtonClick += (_, args) => args.Cancel = isDownloading;

            await dialog.ShowAsync();
        }


        // === private helpers ===

        // returns true once the handoff script owns the update and the app is expected to exit
        private static async Task<bool> RunUpdateAsync(UpdateInfo info, ProgressBar progress, TextBlock statusText)
        {
            try
            {
                var reporter = new Progress<double>(fraction => progress.Value = fraction);
                string? downloadedPath = await UpdateInstaller.DownloadAsync(info, reporter, CancellationToken.None);

                if (string.IsNullOrEmpty(downloadedPath))
                {
                    // a release without the asset this build needs; nothing to install, so just point at the page
                    OpenReleasePage(info.ReleaseUrl);
                    statusText.Text = "This release has no matching download, opening the release page instead";
                    return false;
                }

                statusText.Text = "Installing, the app will restart on its own";
                progress.IsIndeterminate = true;

                UpdateInstaller.ApplyAndRestart(downloadedPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateDialog] update failed: {ex.Message}");
                statusText.Text = "The download failed, you can grab the release manually instead";
                OpenReleasePage(info.ReleaseUrl);
                return false;
            }
        }

        private static void OpenReleasePage(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { /* no browser reachable, the version is still named in the dialog itself */ }
        }
    }
}
