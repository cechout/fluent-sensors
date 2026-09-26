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
    // the confirmation step between spotting a new release and replacing the app with it
    //
    // built in code rather than in XAML because every other dialog in this app is (see ShowInfoDialog and
    // ConfirmAction on the settings page), and because the same instance has to survive the download: the primary
    // button cancels its own close so the progress bar can take over the dialog that is already on screen
    //
    // the store build shows the same dialog, but the store downloads and installs, and doing it yourself means the
    // store page instead of the release page
    public static class UpdateDialog
    {
        // === public api ===

        // the three buttons are the three ways forward (let the app do it, do it yourself, do neither), and the
        // checkbox is a modifier on the exit rather than a fourth button; a ContentDialog has exactly three slots
        // and no close affordance of its own, so Close is the one neutral way out and ESC maps onto it
        public static async Task ShowAsync(XamlRoot? xamlRoot, UpdateInfo? info)
        {
            if (xamlRoot == null || info == null) return;

            bool isStoreBuild = !AppDistribution.SupportsSelfUpdate;

            // an empty version is a store update GitHub could not name yet, see UpdateService.AskStoreAsync
            bool hasVersion = !string.IsNullOrEmpty(info.Version);

            // a ticked box is remembered across restarts; the start pages update button names the skipped version
            // and is the way to undo it again
            // a version that has no name cannot be skipped, so the box is left out for it
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

            // non-null exactly while a download is running, which is also what turns Close into the cancel button
            CancellationTokenSource? downloadCts = null;
            bool handedOverToScript = false;

            // store build only: true while the store replaces the package, which nothing can cancel any more
            bool storeIsInstalling = false;

            void SetDownloading(bool downloading)
            {
                dialog.IsPrimaryButtonEnabled = !downloading;
                dialog.IsSecondaryButtonEnabled = !downloading;
                dialog.CloseButtonText = downloading ? "Cancel" : "Close";
                skipCheckBox.IsEnabled = !downloading;
                progress.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
            }

            // deliberately no GetDeferral(): a pending button-click deferral holds the dialog in a state where it
            // ignores every further button press, which silently swallowed the cancel button for the whole download
            // there is nothing to defer here anyway, since Cancel=true means the dialog is not closing in the first
            // place; everything below the first await simply runs on while the dialog stays interactive
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                // the dialog stays on screen and turns into the progress readout instead of closing
                args.Cancel = true;

                downloadCts = new CancellationTokenSource();
                SetDownloading(true);
                statusText.Visibility = Visibility.Visible;

                if (isStoreBuild)
                {
                    // the dialog stays until the store ends this process; the close button goes once there is
                    // nothing left it could cancel
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
                    // the replacement script is already waiting for this process to disappear
                    handedOverToScript = true;
                    dialog.Hide();
                    FluentSensors.MainWindow.CurrentInstance?.ForceExit();
                    return;
                }

                SetDownloading(false);
            };

            // stays open on purpose: the browser opens beside the dialog and the user can still pick a button
            // the store build opens its store page instead of the release page
            dialog.SecondaryButtonClick += (sender, args) =>
            {
                args.Cancel = true;

                if (isStoreBuild) _ = AppDistribution.OpenStorePageAsync();
                else OpenReleasePage(info.ReleaseUrl);
            };

            // Closing rather than CloseButtonClick, because this has to cover the ESC key just as reliably as the
            // button: an escape that slipped past the abort would leave a download running with no dialog left to
            // report it, and one that slipped past the checkbox would silently ignore what the user just ticked
            dialog.Closing += (_, args) =>
            {
                if (storeIsInstalling)
                {
                    args.Cancel = true;
                    return;
                }

                if (downloadCts != null)
                {
                    // mid-download this is the abort, not the exit; a 97 MB asset on a slow line would otherwise
                    // hold a modal dialog open for minutes with no way out
                    args.Cancel = true;
                    downloadCts.Cancel();
                    return;
                }

                // hiding the very version that is currently being installed would make no sense
                if (handedOverToScript) return;

                if (skipCheckBox.IsChecked == true) UpdateService.Instance.SkipVersion();
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

        // the store counterpart of RunUpdateAsync; it returns only once the store has given up or finished, and a
        // finished install normally ends this process before that
        // onInstalling fires once the store starts replacing the package
        private static async Task RunStoreUpdateAsync(
            ProgressBar progress, TextBlock statusText, Action onInstalling, CancellationToken ct)
        {
            // progress reports are posted to the UI thread, so a late one could land after the result below
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
                        // the store reports 0 for a few seconds before the first bytes arrive
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

            // a cancel the user did not ask for is the store giving up, which is a failure like any other
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
            catch { /* no browser reachable, the version is still named in the dialog itself */ }
        }
    }
}
