using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using FluentSensors.Common;
using FluentSensors.Common.UI;
using FluentSensors.Core.Update;


namespace FluentSensors.Features.Update
{
    // the update dialog:
    // the step between a new release and replacing the app; built in code like the settings page dialogs, and one
    // instance survives the download (the primary button cancels its own close, the progress bar takes over)
    // the store build shows it too, the store installs and the manual way is the store page
    public static class UpdateDialog
    {
        // === public api ===

        // three ways forward (the app does it, you do it, neither) and the checkbox as a modifier on the exit; Close is
        // the neutral way out, ESC included
        public static async Task ShowAsync(XamlRoot? xamlRoot, UpdateInfo? info)
        {
            if (xamlRoot == null || info == null) return;

            bool isStoreBuild = !AppDistribution.SupportsSelfUpdate;

            // empty for a store update without a GitHub name, see UpdateService.AskStoreAsync
            bool hasVersion = !string.IsNullOrEmpty(info.Version);

            // remembered across restarts, the start page button undoes it; (a nameless version cannot be skipped)
            var skipCheckBox = new CheckBox
            {
                Content = "Skip this version",
                Margin = new Thickness(0, 12, 0, 0),
                Visibility = hasVersion ? Visibility.Visible : Visibility.Collapsed
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
                Text = hasVersion
                    ? $"Fluent Sensors {UpdateService.VersionLabel(info.Version)} is available. You are running {UpdateService.VersionLabel(UpdateService.CurrentVersion)}."
                    : $"A new version of Fluent Sensors is available. You are running {UpdateService.VersionLabel(UpdateService.CurrentVersion)}.",
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
                SecondaryButtonText = isStoreBuild ? "Open Store" : "Manual Install",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
                RequestedTheme = DialogTheme.For(xamlRoot)
            };

            // set while a download runs, which turns Close into Cancel
            CancellationTokenSource? downloadCts = null;
            bool handedOverToScript = false;

            // store build: the store replaces the package, nothing can cancel that
            bool storeIsInstalling = false;

            void SetDownloading(bool downloading)
            {
                dialog.IsPrimaryButtonEnabled = !downloading;
                dialog.IsSecondaryButtonEnabled = !downloading;
                dialog.CloseButtonText = downloading ? "Cancel" : "Close";
                skipCheckBox.IsEnabled = !downloading;
                progress.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
            }

            // no GetDeferral(): a pending deferral makes the dialog ignore every further press, the cancel included;
            // with Cancel=true there is nothing to defer
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                // stays on screen as the progress readout
                args.Cancel = true;

                downloadCts = new CancellationTokenSource();
                SetDownloading(true);
                statusText.Visibility = Visibility.Visible;

                if (isStoreBuild)
                {
                    // stays until the store ends the process; the close button goes once nothing can be cancelled
                    await RunStoreUpdateAsync(progress, statusText, () =>
                    {
                        storeIsInstalling = true;
                        dialog.CloseButtonText = "";
                    }, downloadCts.Token);

                    storeIsInstalling = false;
                    downloadCts.Dispose();
                    downloadCts = null;
                    SetDownloading(false);
                    return;
                }

                statusText.Text = $"Downloading {info.AssetName}...";

                bool handedOver = await RunUpdateAsync(info, progress, statusText, downloadCts.Token);

                downloadCts.Dispose();
                downloadCts = null;

                if (handedOver)
                {
                    // the replacement script waits for this process to end
                    handedOverToScript = true;
                    dialog.Hide();
                    FluentSensors.MainWindow.CurrentInstance?.ForceExit();
                    return;
                }

                SetDownloading(false);
            };

            // stays open, the browser (or the store page) opens beside it
            dialog.SecondaryButtonClick += (sender, args) =>
            {
                args.Cancel = true;

                if (isStoreBuild) _ = AppDistribution.OpenStorePageAsync();
                else OpenReleasePage(info.ReleaseUrl);
            };

            // Closing, not CloseButtonClick, so ESC takes the same path: past the abort a download would run unseen,
            // past the checkbox a tick would be lost
            dialog.Closing += (_, args) =>
            {
                if (storeIsInstalling)
                {
                    args.Cancel = true;
                    return;
                }

                if (downloadCts != null)
                {
                    // mid-download the abort, not the exit; (a slow line holds the dialog for minutes)
                    args.Cancel = true;
                    downloadCts.Cancel();
                    return;
                }

                // never skip the version being installed
                if (handedOverToScript) return;

                if (skipCheckBox.IsChecked == true) UpdateService.Instance.SkipVersion();
            };

            await dialog.ShowAsync();
        }


        // === private helpers ===

        // true once the handoff script owns the update and the app has to exit
        private static async Task<bool> RunUpdateAsync(
            UpdateInfo info, ProgressBar progress, TextBlock statusText, CancellationToken ct)
        {
            try
            {
                var reporter = new Progress<double>(fraction => progress.Value = fraction);
                string? downloadedPath = await UpdateInstaller.DownloadAsync(info, reporter, ct);

                if (string.IsNullOrEmpty(downloadedPath))
                {
                    // no asset for this build; the release page
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
                // asked for, so no browser and no error wording
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

        // the store counterpart of RunUpdateAsync; returns once the store gave up or finished (a finished install
        // usually ends the process first); onInstalling fires when the package is replaced
        private static async Task RunStoreUpdateAsync(
            ProgressBar progress, TextBlock statusText, Action onInstalling, CancellationToken ct)
        {
            // reports are posted, a late one could land after the result
            bool isFinished = false;

            var reporter = new Progress<StoreInstallProgress>(step =>
            {
                if (isFinished) return;

                switch (step.Phase)
                {
                    case StoreInstallPhase.Waiting:
                        statusText.Text = "Waiting for the Microsoft Store...";
                        progress.IsIndeterminate = true;
                        break;

                    case StoreInstallPhase.Downloading:
                        // 0 for a few seconds before the first bytes
                        statusText.Text = "Downloading from the Microsoft Store...";
                        progress.IsIndeterminate = step.Fraction <= 0;
                        progress.Value = step.Fraction;
                        break;

                    case StoreInstallPhase.Installing:
                        statusText.Text = "Installing, the app will restart on its own";
                        progress.IsIndeterminate = true;
                        onInstalling();
                        break;
                }
            });

            statusText.Text = "Waiting for the Microsoft Store...";
            progress.IsIndeterminate = true;

            StoreInstallResult result;
            try
            {
                result = await StoreUpdateSource.InstallAsync(reporter, ct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateDialog] store update failed: {ex.Message}");
                result = StoreInstallResult.Failed;
            }

            isFinished = true;
            ResetProgress(progress);

            if (result == StoreInstallResult.Installed)
            {
                statusText.Text = "Update installed, it takes effect the next time the app starts";
                return;
            }

            // a cancel nobody asked for is the store giving up, a failure
            if (result == StoreInstallResult.Canceled && ct.IsCancellationRequested)
            {
                statusText.Text = "Download cancelled";
                return;
            }

            statusText.Text = "The Microsoft Store could not install the update, opening the Store instead";
            _ = AppDistribution.OpenStorePageAsync();
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
            catch { /* no browser; the dialog names the version anyway */ }
        }
    }
}
