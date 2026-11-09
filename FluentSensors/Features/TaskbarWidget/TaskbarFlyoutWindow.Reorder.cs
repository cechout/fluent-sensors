using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using FluentSensors.Common.Localization;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Persistence.Services;

namespace FluentSensors.Features.TaskbarWidget
{
    public sealed partial class TaskbarFlyoutWindow
    {
        public ObservableCollection<SensorGraphViewModel> ReorderSensors { get; } = new();
        private bool _isReordering;
        private SensorGraphViewModel? _draggedSensor;
        private uint _reorderPointerId;
        private double _reorderStartY;
        private const double ReorderRowHeight = 44;

        private void ReorderButton_Click(object sender, RoutedEventArgs e) => SetReorderMode(!_isReordering);

        private void SetReorderMode(bool enabled)
        {
            if (_isReordering == enabled) return;
            CancelReorderDrag();
            _isReordering = enabled;
            ReorderSensors.Clear();
            if (enabled)
                foreach (var sensor in ViewModel.FlyoutSensors) ReorderSensors.Add(sensor);
            GraphsScrollViewer.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            ReorderPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            ReorderButtonIcon.Glyph = enabled ? "\uE73E" : "\uE7C2";
            string label = AppStrings.Get(enabled ? "TaskbarFlyout_ReorderDone" : "TaskbarFlyout_ReorderStart");
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(ReorderButton, label);
            AutomationProperties.SetName(ReorderButton, label);
        }

        // --- workaround: elevated WinUI drag-and-drop ---
        // Native list reordering fails in elevated unpackaged apps. Capture only the handle's pointer;
        // the graph controls never enter this input path. https://github.com/microsoft/microsoft-ui-xaml/issues/7690
        private void ReorderHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_isReordering || sender is not FrameworkElement { Tag: SensorGraphViewModel sensor }) return;
            var point = e.GetCurrentPoint(ReorderItemsControl);
            if (!point.Properties.IsLeftButtonPressed || !ReorderScrollViewer.CapturePointer(e.Pointer)) return;
            _draggedSensor = sensor;
            _reorderPointerId = e.Pointer.PointerId;
            _reorderStartY = point.Position.Y;
            e.Handled = true;
        }

        private void Reorder_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_draggedSensor == null || e.Pointer.PointerId != _reorderPointerId) return;
            double y = e.GetCurrentPoint(ReorderItemsControl).Position.Y;
            if (Math.Abs(y - _reorderStartY) < 5) return;
            int current = ReorderSensors.IndexOf(_draggedSensor);
            int target = Math.Clamp((int)Math.Floor(y / ReorderRowHeight), 0, ReorderSensors.Count - 1);
            if (current >= 0 && current != target) ReorderSensors.Move(current, target);
            double viewportY = e.GetCurrentPoint(ReorderScrollViewer).Position.Y;
            if (viewportY < 24 || viewportY > ReorderScrollViewer.ActualHeight - 24)
                ReorderScrollViewer.ChangeView(null, Math.Max(0, ReorderScrollViewer.VerticalOffset + (viewportY < 24 ? -16 : 16)), null, true);
            e.Handled = true;
        }

        private void Reorder_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_draggedSensor == null || e.Pointer.PointerId != _reorderPointerId) return;
            _draggedSensor = null;
            ReorderScrollViewer.ReleasePointerCaptures();
            SaveReorder();
            e.Handled = true;
        }

        private void Reorder_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (_draggedSensor == null || e.Pointer.PointerId != _reorderPointerId) return;
            CancelReorderDrag();
            ReorderSensors.Clear();
            foreach (var sensor in ViewModel.FlyoutSensors) ReorderSensors.Add(sensor);
        }

        private void CancelReorderDrag()
        {
            _draggedSensor = null;
            ReorderScrollViewer.ReleasePointerCaptures();
        }

        private void MoveSensorUp_Click(object sender, RoutedEventArgs e) => MoveReorderSensor(sender, -1);
        private void MoveSensorDown_Click(object sender, RoutedEventArgs e) => MoveReorderSensor(sender, 1);

        private void MoveReorderSensor(object sender, int delta)
        {
            if (!_isReordering || _draggedSensor != null || sender is not FrameworkElement { Tag: SensorGraphViewModel sensor }) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isReordering || _draggedSensor != null) return;
                int current = ReorderSensors.IndexOf(sensor);
                int target = current + delta;
                if (current < 0 || target < 0 || target >= ReorderSensors.Count) return;
                ReorderSensors.Move(current, target);
                SaveReorder();
            });
        }

        private void SaveReorder() => SensorSelectionService.Instance.SetTaskbarSensorOrder(ReorderSensors.Select(sensor => sensor.SensorId));
    }
}
