using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using FluentSensors.Core.Taskbar;
using FluentSensors.Persistence.Services;

namespace FluentSensors.Features.TaskbarWidget
{
    public sealed partial class TaskbarWidgetWindow
    {
        // Busy's near-tray divider: a 1 DIP line and 4 DIP on either side.
        private const int TrayDividerLengthDip = 9;
        private readonly WinTaskbarTrayTracker _trayTracker = new();

        private void OnNextToTrayChanged(bool enabled)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isClosed) return;
                EndDrag();
                _trayTracker.Reset();
                if (_isEmbedded && _taskbarHwnd != System.IntPtr.Zero)
                {
                    TaskbarFlyoutWindow.CurrentInstance?.HideFlyout();
                    if (_placedTaskbar != null) ApplyTrayDivider(_placedTaskbar);
                    PositionOnTaskbar();
                }
            });
        }

        private void ApplyTrayDivider(WinTaskbarInfo taskbar)
        {
            TrayDivider.Visibility = SettingsService.Instance.TaskbarWidgetNextToTray
                ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetRow(TrayDivider, taskbar.IsVertical ? 1 : 0);
            Grid.SetColumn(TrayDivider, taskbar.IsVertical ? 0 : 1);
            TrayDivider.Width = taskbar.IsVertical ? 24 : 1;
            TrayDivider.Height = taskbar.IsVertical ? 1 : 20;
            TrayDivider.Margin = taskbar.IsVertical ? new Thickness(0, 4, 0, 4) : new Thickness(4, 0, 4, 0);
        }
    }
}
