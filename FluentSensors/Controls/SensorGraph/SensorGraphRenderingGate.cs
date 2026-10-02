using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;


namespace FluentSensors.Controls.SensorGraph
{
    // the rendering gate:
    // the live rendering of every SensorGraphControl in a subtree on or off; shared by the performance page, the
    // widget, the taskbar widget and its flyout
    public static class SensorGraphRenderingGate
    {
        // a gated graph stops all per-tick work without being destroyed (see SensorGraphControl.SetRenderingActive)
        public static void SetActive(DependencyObject root, bool active)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);

                if (child is SensorGraphControl graph)
                {
                    graph.SetRenderingActive(active);
                }

                SetActive(child, active);
            }
        }
    }
}
