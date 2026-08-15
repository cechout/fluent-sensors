using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

using FluentSensors.Controls.SensorGraph;
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

        // permanent start page view; built lazily on first visit, since CPU (not the start page) is the default
        // selected view
        private PerformanceStartView _startView;

        // whether this page is currently the Frames content (OnNavigatedTo/OnNavigatedFrom) and whether the app
        // window itself is actually shown on screen right now (set externally by MainWindow, minimized or hidden
        // e.g. minimize-to-tray both count); combined in UpdatePageRenderingState, both default true since the
        // window is visible and this page is the active content whenever it first gets constructed through normal
        // navigation
        private bool _isNavigatedToPage = true;
        private bool _isWindowVisible = true;

        // last applied result of _isNavigatedToPage && _isWindowVisible, only used to skip redundant gate calls
        private bool _isPageRenderingActive = true;


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

        // jumps to the Performance start page; mirrors NavItem_Click, just with no specific hardware to select
        private void StartPageButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectedItem = null;
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

        // page entering/leaving the Frame (Sensors/Settings <-> Performance navigation); NavigationCacheMode keeps
        // this same instance around, so these fire on every visit, not just the first
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _isNavigatedToPage = true;
            UpdatePageRenderingState();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _isNavigatedToPage = false;
            UpdatePageRenderingState();
        }

        // called by MainWindow whenever the app window itself stops or starts actually being shown on screen
        // (minimized, or hidden entirely e.g. minimize-to-tray); independent of whether this page is currently
        // navigated to, both conditions gate the same underlying rendering state, see UpdatePageRenderingState
        public void SetWindowVisibilityActive(bool isVisible)
        {
            _isWindowVisible = isVisible;
            UpdatePageRenderingState();
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

            if (_currentDetailView != null)
            {
                _currentDetailView.Opacity = 0;
                _currentDetailView.IsHitTestVisible = false;

                // the view is now hidden; stop all of its graphs from doing any per-tick rendering work
                SensorGraphRenderingGate.SetActive(_currentDetailView, false);
            }

            // no SelectedItem means the start page is shown instead of a hardware instances detail view
            UIElement view = target != null ? EnsureDetailView(target) : EnsureStartView();
            if (view == null) return;

            _currentDetailView = view;

            // only actually resume rendering if the page itself is currently on screen right now; if it is not
            // (navigated away, window minimized/hidden) the newly selected view stays gated off until
            // UpdatePageRenderingState reactivates it on return, exactly like every other graph on the page
            if (_isPageRenderingActive) ActivateCurrentDetailViewRendering();

            view.Opacity = 1;
            view.IsHitTestVisible = true;
        }

        // combines page-navigation and window-visibility into this pages one rendering-active state; same
        // philosophy SensorGraphRenderingGate itself already uses one level up: only the live redraw ever pauses,
        // SensorData keeps filling in the background regardless, so returning shows continuous history
        private void UpdatePageRenderingState()
        {
            bool active = _isNavigatedToPage && _isWindowVisible;
            if (active == _isPageRenderingActive) return;
            _isPageRenderingActive = active;

            if (active)
            {
                SensorGraphRenderingGate.SetActive(NavItemsControl, true);
                ActivateCurrentDetailViewRendering();
            }
            else
            {
                // blanket off across the whole page; safe even though it also re-touches the cached views and
                // sub-sections that are already off, turning something already off, off again is a no-op
                SensorGraphRenderingGate.SetActive(RootGrid, false);
            }
        }

        // re-enables live rendering for whichever view is currently selected, including resyncing its own visible
        // sub-section/layout (Overview vs AllThreads/Extended, Wide vs Narrow); shared by UpdateDetailView
        // (hardware switch) and UpdatePageRenderingState (page/window visibility returning) - both need exactly
        // this and nothing more, reactivating the whole DetailHostGrid indiscriminately would also wake up every
        // other cached hardware views graphs
        private void ActivateCurrentDetailViewRendering()
        {
            if (_currentDetailView == null) return;

            // resume rendering before the view becomes visible, so its first shown frame already shows current data
            SensorGraphRenderingGate.SetActive(_currentDetailView, true);

            // the walk above just turned every graph in this view back on, including whichever of
            // Overview/Extended (or Overview/AllThreads) and whichever of Wide/Narrow is not actually shown right
            // now; hand it back to the view itself to correct that down to just the visible section/layout
            switch (_currentDetailView)
            {
                case CpuDetailView cpu: cpu.SyncSectionRenderingGate(); break;
                case GpuDetailView gpu: gpu.SyncSectionRenderingGate(); break;
                case StorageDetailView storage: storage.SyncLayoutRenderingGate(); break;
                case NetworkDetailView network: network.SyncLayoutRenderingGate(); break;
            }
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

                // a newly built view starts with all its graphs rendering (the control default); unless it is (or is
                // about to become) the selected one, shut that rendering off once it has actually been laid out, so
                // only the visible views graphs ever draw
                // deferred to Low priority so the graphs exist in the visual tree by the time the walk runs, and
                // skipped if this view has meanwhile become the selected one (UpdateDetailView activates that one)
                UIElement created = view;
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    if (ReferenceEquals(created, _currentDetailView)) return;
                    created.UpdateLayout();
                    SensorGraphRenderingGate.SetActive(created, false);
                });
            }

            return view;
        }

        // creates (once) and caches the permanent start page view; same retained-instance rule as EnsureDetailView,
        // just not keyed by a hardware Target, since this view shows every NavItem at once instead of one instance
        private UIElement EnsureStartView()
        {
            if (_startView == null)
            {
                _startView = new PerformanceStartView
                {
                    Opacity = 0,
                    IsHitTestVisible = false
                };
                DetailHostGrid.Children.Add(_startView);
            }

            return _startView;
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