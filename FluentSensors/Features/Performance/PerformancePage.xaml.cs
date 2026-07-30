using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.ComponentModel;

using FluentSensors.Features.Performance.HardwareViews;
using FluentSensors.Features.Performance.Lhm;


namespace FluentSensors.Features.Performance
{
    public sealed partial class PerformancePage : Page
    {
        // === fields ===

        public PerformanceViewModel ViewModel => PerformanceViewModel.Instance;

        // one permanent view per hardware instance, keyed by its Target object (e.g. one specific
        // LhmGpuInstanceViewModel); created lazily on first selection, never removed/destroyed afterward; see
        // UpdateDetailView() for further information
        private readonly Dictionary<object, UIElement> _detailViewCache = new();
        private UIElement _currentDetailView;


        // === constructor ===

        public PerformancePage()
        {
            InitializeComponent();

            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateDetailView();
        }


        // === event handlers ===

        // sidebar selection: every nav item button shares this one handler, the clicked items own DataContext (set by
        // the ItemTemplate) tells us which PerformanceNavItemViewModel was chosen
        private void NavItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is PerformanceNavItemViewModel item)
            {
                ViewModel.SelectedItem = item;
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PerformanceViewModel.SelectedItem))
            {
                UpdateDetailView();
            }
        }


        // === private helpers ===

        // --- memory leak: hardware detail views never released after switching ---
        // problem: each view hosts SensorGraphControl, which wraps LiveChartsCores native SkiaSharp rendering
        // surface and subscribes to several of its own events; WinUI/.NETs GC cannot see through the resulting
        // native reference cycle, so destroying and rebuilding these views on every hardware switch leaked a few
        // MB per switch, unbounded
        // same root cause category as the WinUI secondary-window leak elsewhere in this app (native WinRT/interop
        // objects do not get released back to the GC), just triggered by chart controls moving between views
        // instead of by a Window; see https://microsoft.github.io/Win2D/WinUI3/html/RefCycles.htm for the general
        // mechanism (Win2D, but the same applies to any native rendering wrapper incl. LiveChartsCore.SkiaSharpView)
        // fix: never destroy a hardware instances detail view once created; cache one permanent instance per
        // hardware instance (keyed by its Target object) and toggle Visibility on switch instead
        private void UpdateDetailView()
        {
            object target = ViewModel.SelectedItem?.Target;
            if (target == null) return;

            if (_currentDetailView != null) _currentDetailView.Visibility = Visibility.Collapsed;

            if (!_detailViewCache.TryGetValue(target, out UIElement view))
            {
                view = target switch
                {
                    LhmCpuInstanceViewModel cpu => new CpuDetailView { Cpu = cpu },
                    LhmGpuInstanceViewModel gpu => new GpuDetailView { Gpu = gpu },
                    LhmMemoryInstanceViewModel memory => new MemoryDetailView { Memory = memory },
                    LhmStorageInstanceViewModel storage => new StorageDetailView { Storage = storage },
                    LhmNetworkInstanceViewModel network => new NetworkDetailView { Network = network },
                    _ => null
                };

                if (view == null) return;

                _detailViewCache[target] = view;
                DetailHostGrid.Children.Add(view);
            }

            view.Visibility = Visibility.Visible;
            _currentDetailView = view;
        }
    }
}