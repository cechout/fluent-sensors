using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using FluentSensors.Features.Performance.Lhm;


namespace FluentSensors.Features.Performance
{
    // the detail view selector:
    // picks the detail view (CpuDetailView, GpuDetailView) of the PerformancePage ContentControl by the runtime
    // type of SelectedItem.Target
    // WinUI has no implicit DataTemplate by type like WPF, so one property per kind, each a keyed StaticResource; a new
    // kind is a property, a switch arm and a template
    public class HardwareDetailTemplateSelector : DataTemplateSelector
    {
        public DataTemplate CpuTemplate { get; set; }
        public DataTemplate GpuTemplate { get; set; }
        public DataTemplate MemoryTemplate { get; set; }
        public DataTemplate StorageTemplate { get; set; }
        public DataTemplate NetworkTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item)
        {
            return item switch
            {
                LhmCpuInstanceViewModel => CpuTemplate,
                LhmGpuInstanceViewModel => GpuTemplate,
                LhmMemoryInstanceViewModel => MemoryTemplate,
                LhmStorageInstanceViewModel => StorageTemplate,
                LhmNetworkInstanceViewModel => NetworkTemplate,
                _ => null
            };
        }

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            return SelectTemplateCore(item);
        }
    }
}
