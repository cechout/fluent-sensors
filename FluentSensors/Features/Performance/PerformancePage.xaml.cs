using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Collections.Specialized;
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
        // LhmGpuInstanceViewModel); created eagerly for every instance that exists (or later appears)
        // Never removed/destroyed afterward; see EnsureDetailView() for further information
        private readonly Dictionary<object, UIElement> _detailViewCache = new();
        private UIElement _currentDetailView;


        // === constructor ===

        public PerformancePage()
        {
            InitializeComponent();

            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            // eager-detail-view-construction:
            // problem: each detail views SensorPanelControl applies its own fixed Y-axis config
            // (ApplyViewOverrides) to its bound SensorGraphViewModel on construction; that same
            // SensorGraphViewModel instance is also the one shown as the sidebar nav items PrimaryGraph, so a
            // sensor whose fixed scale was never persisted anywhere else (e.g. Storage/Network, never pinned via
            // the Sensors page) visibly auto-scaled in the sidebar until its detail view was first built on
            // click, then jumped to the fixed scale; CPU never showed this because its detail view is already
            // the default selection, built before the user ever sees the sidebar
            // fix: build every hardware instances detail view immediately instead of lazily on first selection,
            // so the override is applied before the sidebar is shown at all
            foreach (var item in ViewModel.NavItems)
            {
                EnsureDetailView(item.Target);
            }
            ViewModel.NavItems.CollectionChanged += OnNavItemsChanged;

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

        // hardware discovered after this page was already constructed (e.g. a second GPU, or any category that
        // finishes its async LHM discovery late) needs its detail view built immediately too, for the same
        // reason as the eager construction above
        private void OnNavItemsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (PerformanceNavItemViewModel item in e.NewItems)
            {
                EnsureDetailView(item.Target);
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
        // instead of by a Window
        // see https://microsoft.github.io/Win2D/WinUI3/html/RefCycles.htm for the general mechanism (Win2D, but the
        // same applies to any native rendering wrapper incl. LiveChartsCore.SkiaSharpView)
        // fix: never destroy a hardware instances detail view once created; cache one permanent instance per
        // hardware instance (keyed by its Target object) and toggle Visibility on switch instead
        private void UpdateDetailView()
        {
            object target = ViewModel.SelectedItem?.Target;
            if (target == null) return;

            if (_currentDetailView != null) _currentDetailView.Visibility = Visibility.Collapsed;

            UIElement view = EnsureDetailView(target);
            if (view == null) return;

            view.Visibility = Visibility.Visible;
            _currentDetailView = view;
        }

        // creates (once) and caches the permanent detail view for one hardware instance; safe to call repeatedly
        // for the same target, always returns the same cached instance; newly created views start Collapsed,
        // UpdateDetailView() is responsible for making the selected one Visible
        private UIElement EnsureDetailView(object target)
        {
            if (target == null) return null;

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

                if (view == null) return null;

                view.Visibility = Visibility.Collapsed;
                _detailViewCache[target] = view;
                DetailHostGrid.Children.Add(view);
            }

            return view;
        }
    }
}