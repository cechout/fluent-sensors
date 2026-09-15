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

        // the three buttons are the three ways forward (let the app do it, do it yourself, do neither), and the
        // checkbox is a modifier on the exit rather than a fourth button; a ContentDialog has exactly three slots
        // and no close affordance of its own, so Close is the one neutral way out and ESC maps onto it
        public static async Task ShowAsync(XamlRoot? xamlRoot, UpdateInfo? info)
        {
            if (xamlRoot == null || info == null) return;

            var skipCheckBox = new CheckBox
            {
                Content = "Skip this version",
                Margin = new Thickness(0, 12, 0, 0)
            };

            var progress = new ProgressBar
            {
                Margin = new Thickness(0, 8, 0, 0),
                Maximum = 1,
                Visibility = Visibility.Collapsed
            };

            var statusText = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Collapsed,
                TextWrapping = TextWrapping.Wrap
            };

            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = $"Fluent Sensors {info.Version} is available. You are running {UpdateService.CurrentVersion}.",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(skipCheckBox);
            content.Children.Add(progress);
            content.Children.Add(statusText);

            var dialog = new ContentDialog
            {
                Title = "Update available",
                Content = content,
                PrimaryButtonText = "Update",
                SecondaryButtonText = "Manual Install",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot
            };

            // non-null exactly while a download is running, which is also what turns Close into the cancel button
            CancellationTokenSource? downloadCts = null;
            bool handedOverToScript = false;

            void SetDownloading(bool downloading)
            {
                dialog.IsPrimaryButtonEnabled = !downloading;
                dialog.IsSecondaryButtonEnabled = !downloading;
                dialog.CloseButtonText = downloading ? "Cancel" : "Close";
                skipCheckBox.IsEnabled = !downloading;
                progress.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
            }

            dialog.PrimaryButtonClick += async (_, args) =>
            {
                // the dialog stays on screen and turns into the progress readout instead of closing
                args.Cancel = true;
                var deferral = args.GetDeferral();

                downloadCts = new CancellationTokenSource();
                SetDownloading(true);
                statusText.Visibility = Visibility.Visible;
                statusText.Text = $"Downloading {info.AssetName}...";

                bool handedOver = await RunUpdateAsync(info, progress, statusText, downloadCts.Token);

                downloadCts.Dispose();
                downloadCts = null;
                deferral.Complete();

                if (handedOver)
                {
                    // the replacement script is already waiting for this process to disappear
                    handedOverToScript = true;
                    dialog.Hide();
                    FluentSensors.MainWindow.CurrentInstance?.ForceExit();
                    return;
                }

                SetDownloading(false);
            };

            // stays open on purpose: the browser opens beside the dialog and the user can still pick a button
            dialog.SecondaryButtonClick += (_, args) =>
            {
                args.Cancel = true;
                OpenReleasePage(info.ReleaseUrl);
            };

            // Closing rather than CloseButtonClick, because this has to cover the ESC key just as reliably as the
            // button: an escape that slipped past the abort would leave a download running with no dialog left to
            // report it, and one that slipped past the checkbox would silently ignore what the user just ticked
            dialog.Closing += (_, args) =>
            {
                if (downloadCts != null)
                {
                    // mid-download this is the abort, not the exit; a 97 MB asset on a slow line would otherwise
                    // hold a modal dialog open for minutes with no way out
                    args.Cancel = true;
                    downloadCts.Cancel();
                    return;
                }

                // skipping the very version that is currently being installed would make no sense
                if (handedOverToScript) return;

                if (skipCheckBox.IsChecked == true) UpdateService.Instance.SkipCurrentUpdate();
            };

            await dialog.ShowAsync();
        }


        // === private helpers ===

        // returns true once the handoff script owns the update and the app is expected to exit
        private static async Task<bool> RunUpdateAsync(
            UpdateInfo info, ProgressBar progress, TextBlock statusText, CancellationToken ct)
        {
            try
            {
                var reporter = new Progress<double>(fraction => progress.Value = fraction);
                string? downloadedPath = await UpdateInstaller.DownloadAsync(info, reporter, ct);

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
            catch (OperationCanceledException)
            {
                // the user asked for this, so it gets no browser window and no error wording
                statusText.Text = "Download cancelled";
                ResetProgress(progress);
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateDialog] update failed: {ex.Message}");
                statusText.Text = "The download failed, you can grab the release manually instead";
                ResetProgress(progress);
                OpenReleasePage(info.ReleaseUrl);
                return false;
            }
        }

        private static void ResetProgress(ProgressBar progress)
        {
            progress.IsIndeterminate = false;
            progress.Value = 0;
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
