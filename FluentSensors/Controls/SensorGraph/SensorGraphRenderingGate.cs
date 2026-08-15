using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;


namespace FluentSensors.Controls.SensorGraph
{
    // switches the live rendering of every SensorGraphControl under a given subtree on or off
    // lives here rather than in any one feature; both the Performance page and the Widget window gate whole subtrees
    // of graphs the exact same way, neither owns the mechanism
    public static class SensorGraphRenderingGate
    {
        // walks every SensorGraphControl under root and switches its live rendering; a gated-off graph stops doing
        // per-tick work entirely without being destroyed (see SensorGraphControl.SetRenderingActive)
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
