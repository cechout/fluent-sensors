using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

using FluentSensors.Features.Performance.HardwareViews;
using FluentSensors.Features.Performance.Lhm;


namespace FluentSensors.Features.Performance
{
    public sealed partial class PerformancePage : Page
    {
        // === fields ===

        public PerformanceViewModel ViewModel => PerformanceViewModel.Instance;

        // below this DetailHostGrid width, the nav sidebar and info panel become mutually exclusive - both eat into
        // the same remaining space after the nav sidebar's own column, so a narrow DetailHostGrid means too little is
        // left for usable content if both stay open at once
        private const double NarrowContentThreshold = 500;
        private bool _isNarrow;

        // one permanent view per hardware instance, keyed by its Target object (e.g. one specific
        // LhmGpuInstanceViewModel); created eagerly for every instance that exists (or later appears)
        // Never removed/destroyed afterward; see EnsureDetailView() for further information
        private readonly Dictionary<object, UIElement> _detailViewCache = new();
        private UIElement _currentDetailView;


        // === constructor ===

        public PerformancePage()
        {
            InitializeComponent();

            // keeps PerformanceViewModel.IsDarkTheme in sync with the pages actually applied theme
            Loaded += (s, e) => ViewModel.IsDarkTheme = ActualTheme == ElementTheme.Dark;
            ActualThemeChanged += (s, e) => ViewModel.IsDarkTheme = ActualTheme == ElementTheme.Dark;

            // viewmodel
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            // only the initially selected hardwares detail view (normally CPU) is built synchronously here, so the
            // page has real, correctly-scaled content the instant it appears; every other hardware instance gets its
            // detail view built one at a time via BuildRemainingDetailViewsAsync below, instead of all of them in one
            // synchronous burst
            // building even a single one of these is not free (each one hosts several SensorGraphControls, and each of
            // those spins up its own native LiveChartsCore/SkiaSharp render surface), so 5 in a row up front was what
            // made first entry into this page noticeably slow
            object initialTarget = ViewModel.SelectedItem?.Target;
            if (initialTarget != null)
            {
                EnsureDetailView(initialTarget);
            }
            UpdateDetailView();

            var remainingTargets = ViewModel.NavItems
                .Select(item => item.Target)
                .Where(target => target != initialTarget)
                .ToList();
            _ = BuildRemainingDetailViewsAsync(remainingTargets);

            ViewModel.NavItems.CollectionChanged += OnNavItemsChanged;
        }


        // === event handlers ===

        // sidebar selection: every nav item button shares this one handler, the clicked items own DataContext (set by
        // the ItemTemplate) tells us which PerformanceNavItemViewModel was chosen
        //
        // IsChecked is only bound OneWay from IsSelected, but a ToggleButton always flips its own IsChecked on click
        // regardless of bindings; clicking the already-selected item therefore visually unchecks it, since
        // SelectedItem does not change and so IsSelected never raises PropertyChanged to push it back forcing IsChecked
        // back to true here makes the sidebar behave like a radio selection instead
        private void NavItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle && toggle.DataContext is PerformanceNavItemViewModel item)
            {
                ViewModel.SelectedItem = item;
                toggle.IsChecked = true;
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PerformanceViewModel.SelectedItem))
            {
                UpdateDetailView();
            }

            // narrow-width exclusivity
            else if (e.PropertyName == nameof(PerformanceViewModel.IsNavSidebarVisible))
            {
                if (_isNarrow && ViewModel.IsNavSidebarVisible && ViewModel.IsInfoPanelVisible)
                {
                    ViewModel.IsInfoPanelVisible = false;
                }
                RecalculateCurrentDetailViewHeight();
            }

            else if (e.PropertyName == nameof(PerformanceViewModel.IsInfoPanelVisible))
            {
                if (_isNarrow && ViewModel.IsInfoPanelVisible && ViewModel.IsNavSidebarVisible)
                {
                    ViewModel.IsNavSidebarVisible = false;
                }
                RecalculateCurrentDetailViewHeight();
            }
        }

        // tracks whether DetailHostGrid currently counts as "narrow"; also handles the one case the property-changed
        // logic above can't cover - the window shrinking while both panels are already open, with neither one having
        // just been toggled
        private void DetailHostGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            bool wasNarrow = _isNarrow;
            _isNarrow = e.NewSize.Width < NarrowContentThreshold;

            if (_isNarrow && !wasNarrow && ViewModel.IsNavSidebarVisible && ViewModel.IsInfoPanelVisible)
            {
                // hardcoded priority: info panel always loses when space runs out from a resize, not a toggle click
                ViewModel.IsInfoPanelVisible = false;
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
        //
        // --- workaround: SensorGraphControl permanently blank after Collapsed + Unload/Reload ---
        // problem: confirmed via diagnostic logging (Loaded event + ActualWidth/Height): a SensorGraphControl
        // that is Visibility.Collapsed when its parent page gets unloaded and reloaded (e.g. leaving and
        // returning to PerformancePage) measures at 0x0 on reload; LiveChartsCores native SkiaSharp rendering
        // surface never recovers from this, even once the element later becomes Visible again with a real size
        // fix: never set Visibility.Collapsed on a detail view once created; keep it permanently
        // Visibility.Visible with a real, non-zero layout size, and hide/show via Opacity + IsHitTestVisible
        // instead, so the native surface never sees a 0x0 measure pass to begin with
        private void UpdateDetailView()
        {
            object target = ViewModel.SelectedItem?.Target;
            if (target == null) return;

            if (_currentDetailView != null)
            {
                _currentDetailView.Opacity = 0;
                _currentDetailView.IsHitTestVisible = false;
            }

            UIElement view = EnsureDetailView(target);
            if (view == null) return;

            view.Opacity = 1;
            view.IsHitTestVisible = true;
            _currentDetailView = view;
        }

        // creates (once) and caches the permanent detail view for one hardware instance; safe to call repeatedly
        // for the same target, always returns the same cached instance; newly created views stay
        // Visibility.Visible with Opacity 0 (see workaround comment on UpdateDetailView for why), UpdateDetailView()
        // is responsible for opacity-swapping the selected one to 1
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

                view.Opacity = 0;
                view.IsHitTestVisible = false;
                _detailViewCache[target] = view;
                DetailHostGrid.Children.Add(view);
            }

            return view;
        }

        // builds every remaining hardware instances detail view one at a time, each on its own separate dispatcher
        // pass instead of all in one synchronous loop
        // This is what actually lets the UI thread render/respond between each one, so the page stays interactive
        // immediately and the not-yet-selected sidebar entries simply pop in their correct Y-axis scale over the next
        // moment instead of blocking first entry into the page
        // Low priority: this can freely lose out to anything the user is actually doing on the page right now
        private async Task BuildRemainingDetailViewsAsync(List<object> targets)
        {
            foreach (var target in targets)
            {
                var tcs = new TaskCompletionSource();
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    EnsureDetailView(target);
                    tcs.SetResult();
                });
                await tcs.Task;
            }
        }

        // resolves the sidebar mini-graphs card background:
        // in dark mode, reuses the same fill color the outer ToggleButtons own Checked VisualState
        // (SidebarNavToggleButtonStyle) already uses when this item is selected
        // light mode intentionally left alone: the tile background already got its own light-mode fix, this graph
        // override is not part of that and stays on its normal default there
        private static Windows.UI.Color? ResolveSelectedGraphBackground(bool isSelected, bool isDarkTheme) =>
            isSelected && isDarkTheme ? (Windows.UI.Color)Application.Current.Resources["ControlFillColorDisabled"] : (Windows.UI.Color?)null;

        // re-measures the current detail views vertical layout after a nav sidebar/info panel visibility change;
        // Dispatched rather than called synchronously
        private void RecalculateCurrentDetailViewHeight()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                RootGrid.UpdateLayout();

                switch (_currentDetailView)
                {
                    case CpuDetailView cpu: cpu.RecalculateOverviewHeight(); break;
                    case GpuDetailView gpu: gpu.RecalculateOverviewHeight(); break;
                    case MemoryDetailView memory: memory.RecalculateOverviewHeight(); break;
                    case StorageDetailView storage: storage.RecalculateOverviewHeight(); break;
                    case NetworkDetailView network: network.RecalculateOverviewHeight(); break;
                }
            });
        }
    }
}