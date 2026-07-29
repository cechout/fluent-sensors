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

        protected override DataTemplate SelectTemplateCore(object item)
        {
            return item switch
            {
                LhmCpuInstanceViewModel => CpuTemplate,
                _ => null
            };
        }

        // ContentControl actually calls this container-aware overload; without overriding it too, WinUI never
        // reaches the overload above and just falls back to showing the items ToString()
        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            return SelectTemplateCore(item);
        }
    }
}