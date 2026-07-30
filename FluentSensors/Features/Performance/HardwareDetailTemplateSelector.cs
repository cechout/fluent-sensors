using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using FluentSensors.Features.Performance.Lhm;


namespace FluentSensors.Features.Performance
{
    // picks which hardware-specific detail view (CpuDetailView, future GpuDetailView, ...)
    // the single ContentControl in PerformancePage.xaml shows, purely by the runtime type of SelectedItem.Target
    //
    // WinUI has no automatic implicit-DataTemplate-by-type resolution the way WPF does; every DataTemplate needs
    // an x:Key; so this selector is the standard WinUI substitute: one property per hardware kind, wired to its
    // keyed DataTemplate as a StaticResource in PerformancePage.xaml
    // Adding a new hardware kind later means one new property here, one new switch arm, and one new keyed DataTemplate
    // in the page; PerformancePage itself does not otherwise change
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