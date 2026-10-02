using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using CommunityToolkit.WinUI;
using VirtualKey = Windows.System.VirtualKey;

using FluentSensors.Common.Sensors;
using FluentSensors.Common.UI;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Features.Performance.HardwareViews;
using FluentSensors.Features.Performance.Lhm;


namespace FluentSensors.Features.Performance
{
    public sealed partial class PerformancePage : Page
    {
        // === fields ===

        public PerformanceViewModel ViewModel => PerformanceViewModel.Instance;

        // the narrowest hardware view between the two panels; below it sidebar and info panel are exclusive
        // (measured as if both were open, so a toggle never flips the result)
        private const double NarrowContentThreshold = 315;
        private bool _isNarrow;

        // resizable side panels, the hardware list left and the info panel right; they open at the minimum and drag up
        // to a share of the page width
        private const double NavSidebarMinWidth = 185;
        private const double InfoPanelMinWidth = 185;
        private const double SidePanelMaxWidthShare = 0.50;

        // one arrow press from the sidebar or a bottom bar; (about one mouse wheel notch)
        private const double ArrowScrollStep = 100;

        // one permanent view per hardware instance, keyed by its Target; never removed, see UpdateDetailView
        private readonly Dictionary<object, UIElement> _detailViewCache = new();
        private UIElement _currentDetailView;

        // permanent start page view; built on the first visit (CPU is the default selection)
        private PerformanceStartView _startView;

        // whether this page is the Frame content and whether the window is on screen (set by MainWindow); combined in
        // UpdatePageRenderingState, both true on construction
        private bool _isNavigatedToPage = true;
        private bool _isWindowVisible = true;

        // the last applied combination, to skip redundant gate calls
        private bool _isPageRenderingActive = true;


        // === constructor ===

        public PerformancePage()
        {
            InitializeComponent();

            // before any detail view, every info panel column binds its width and limits from here; (the maximum
            // follows the page width, see UpdateSidePanelLayout)
            SidebarColumn.Width = new GridLength(NavSidebarMinWidth);
            ViewModel.InfoPanelWidth = InfoPanelMinWidth;
            ViewModel.SetSidePanelLimits(NavSidebarMinWidth, double.PositiveInfinity, InfoPanelMinWidth, double.PositiveInfinity);

            // IsDarkTheme follows the applied theme; the sidebar icon brushes are plain brushes and are rebuilt
            Loaded += (s, e) => ApplyActualTheme();
            ActualThemeChanged += (s, e) => ApplyActualTheme();

            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            void ApplyActualTheme()
            {
                ViewModel.IsDarkTheme = ActualTheme == ElementTheme.Dark;
                HardwareColorMode.IsDarkTheme = ViewModel.IsDarkTheme;
                ViewModel.RefreshNavItemIconBrushes();
            }

            // only the selected detail view (normally CPU) is built right away, so the page appears with real content;
            // the rest follow one at a time (each view spins up several native SkiaSharp surfaces)
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

        // on the sidebar and a bottom bar, up and down scroll the hardware view (its graphs and tiles are FocusSkip, so
        // the keyboard has no other way down); on the sidebar left and right move between entries
        private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Up && e.Key != VirtualKey.Down && e.Key != VirtualKey.Left && e.Key != VirtualKey.Right) return;
            if (FocusManager.GetFocusedElement(XamlRoot) is not DependencyObject focused) return;

            bool onSidebar = FocusGroup.IsInside(focused, NavItemsControl);
            if (!onSidebar && !IsOnBottomBar(focused)) return;

            if (e.Key == VirtualKey.Up || e.Key == VirtualKey.Down)
            {
                ScrollCurrentView(e.Key == VirtualKey.Up ? -ArrowScrollStep : ArrowScrollStep);
                e.Handled = true;
            }
            else if (onSidebar)
            {
                var direction = e.Key == VirtualKey.Left ? FocusNavigationDirection.Up : FocusNavigationDirection.Down;
                FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = NavItemsControl });
                e.Handled = true;
            }
        }

        // sidebar selection, one handler for every item; the DataContext names the item
        // a ToggleButton flips its own IsChecked on click, so the already selected item would uncheck; forcing it back
        // makes the sidebar a radio selection
        private void NavItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle && toggle.DataContext is PerformanceNavItemViewModel item)
            {
                ViewModel.SelectedItem = item;
                toggle.IsChecked = true;
            }
        }

        // the start page; (no hardware selected)
        private void StartPageButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectedItem = null;
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PerformanceViewModel.SelectedItem))
            {
                UpdateDetailView();

                // leaving the start page brings both panels and their drag limit back
                UpdateSidePanelLayout();
            }

            // narrow-width exclusivity
            else if (e.PropertyName == nameof(PerformanceViewModel.IsNavSidebarVisible))
            {
                if (_isNarrow && ViewModel.IsNavSidebarVisible && ViewModel.IsInfoPanelVisible)
                {
                    ViewModel.IsInfoPanelVisible = false;
                }
                UpdateSidePanelLayout();
                RecalculateCurrentDetailViewHeight();
            }

            else if (e.PropertyName == nameof(PerformanceViewModel.IsInfoPanelVisible))
            {
                if (_isNarrow && ViewModel.IsInfoPanelVisible && ViewModel.IsNavSidebarVisible)
                {
                    ViewModel.IsNavSidebarVisible = false;
                }
                UpdateSidePanelLayout();
                RecalculateCurrentDetailViewHeight();
            }

            // a finished info panel drag; (the detail area width did not change, nothing else settles the view)
            else if (e.PropertyName == nameof(PerformanceViewModel.InfoPanelWidth))
            {
                UpdateSidePanelLayout();
                RecalculateCurrentDetailViewHeight();
            }
        }

        private void ContentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateSidePanelLayout();
        }

        // after a drag the sidebar gets a fixed width again and the detail area the star, so a later resize only moves
        // the detail area; one last pass at the final width
        private void SidebarSplitter_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            double width = SidebarColumn.ActualWidth;
            SidebarColumn.Width = new GridLength(width);
            DetailColumn.Width = new GridLength(1, GridUnitType.Star);

            UpdateSidePanelLayout();
            RecalculateCurrentDetailViewHeight();
        }

        // hardware discovered after construction (a late LHM category) gets its detail view right away too
        private void OnNavItemsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (PerformanceNavItemViewModel item in e.NewItems)
            {
                EnsureDetailView(item.Target);
            }
        }

        // entering and leaving the Frame; NavigationCacheMode keeps this instance, so these fire on every visit
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

        // MainWindow, when the window is minimized, hidden or back; see UpdatePageRenderingState
        public void SetWindowVisibilityActive(bool isVisible)
        {
            _isWindowVisible = isVisible;
            UpdatePageRenderingState();
        }


        // === private helpers ===

        // the command bar inside the shown view (CPU and GPU have one), not the one in the page header
        private bool IsOnBottomBar(DependencyObject focused)
        {
            return _currentDetailView != null
                && FocusGroup.IsInside(focused, _currentDetailView)
                && (focused as FrameworkElement)?.FindAscendant<CommandBar>() != null;
        }

        private void ScrollCurrentView(double delta)
        {
            if ((_currentDetailView as FrameworkElement)?.FindDescendant("ContentScrollViewer") is not ScrollViewer viewer) return;

            viewer.ChangeView(null, viewer.VerticalOffset + delta, null);
        }

        // --- memory leak: hardware detail views never released after switching ---
        // problem: each SensorGraphControl wraps a native LiveChartsCore SkiaSharp surface and its events, a
        // reference cycle the GC cannot see through; rebuilding the views on every switch leaked a few MB each time
        // (the general mechanism, for Win2D):
        // https://microsoft.github.io/Win2D/WinUI3/html/RefCycles.htm
        // fix: one permanent view per hardware instance, cached by its Target and never destroyed
        //
        // --- workaround: SensorGraphControl permanently blank after Collapsed + Unload/Reload ---
        // problem: a SensorGraphControl that is Collapsed while its page unloads and reloads measures 0x0 on
        // reload, and the SkiaSharp surface never recovers, even once visible again (confirmed by logging
        // Loaded and ActualWidth/Height)
        // fix: a detail view is never Collapsed; it stays Visible with a real size and hides through
        // Opacity and IsHitTestVisible
        private void UpdateDetailView()
        {
            object target = ViewModel.SelectedItem?.Target;

            if (_currentDetailView != null)
            {
                _currentDetailView.Opacity = 0;
                _currentDetailView.IsHitTestVisible = false;
                SetKeyboardReachable(_currentDetailView, false);

                // hidden; its graphs stop rendering
                SensorGraphRenderingGate.SetActive(_currentDetailView, false);
            }

            // no SelectedItem: the start page
            UIElement view = target != null ? EnsureDetailView(target) : EnsureStartView();
            if (view == null) return;

            _currentDetailView = view;

            // only while the page is on screen; otherwise UpdatePageRenderingState wakes it on return
            if (_isPageRenderingActive) ActivateCurrentDetailViewRendering();

            view.Opacity = 1;
            view.IsHitTestVisible = true;
            SetKeyboardReachable(view, true);
        }

        // a hidden view at Opacity 0 would keep its tab stops; disabled, it leaves keyboard
        // navigation until shown again
        private static void SetKeyboardReachable(UIElement view, bool reachable)
        {
            if (view is Control control) control.IsEnabled = reachable;
        }

        // navigation and window visibility into one rendering state; only the redraw pauses, SensorData keeps filling,
        // so a return shows the continuous history
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
                // off across the whole page; (switching an already off graph off is a no-op)
                SensorGraphRenderingGate.SetActive(RootGrid, false);
            }
        }

        // re-enables rendering for the selected view only, down to its visible section or layout (Overview vs
        // AllThreads or Extended, Wide vs Narrow); the whole DetailHostGrid would wake every cached view
        private void ActivateCurrentDetailViewRendering()
        {
            if (_currentDetailView == null) return;

            // before the view shows, so its first frame is current
            SensorGraphRenderingGate.SetActive(_currentDetailView, true);

            // the walk above woke the hidden section or layout too; the view narrows it down itself
            switch (_currentDetailView)
            {
                case CpuDetailView cpu: cpu.SyncSectionRenderingGate(); break;
                case GpuDetailView gpu: gpu.SyncSectionRenderingGate(); break;
                case StorageDetailView storage: storage.SyncLayoutRenderingGate(); break;
                case NetworkDetailView network: network.SyncLayoutRenderingGate(); break;
            }
        }

        // creates once and caches the permanent detail view of one hardware instance; new views start at
        // Opacity 0 (see UpdateDetailView)
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
                SetKeyboardReachable(view, false);
                _detailViewCache[target] = view;
                DetailHostGrid.Children.Add(view);

                // a new view renders by default; unless it became the selected one, it is switched off once laid out
                // (at Low priority, so the graphs exist by then)
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

        // the start page counterpart of EnsureDetailView, not keyed by a Target
        private UIElement EnsureStartView()
        {
            if (_startView == null)
            {
                _startView = new PerformanceStartView
                {
                    Opacity = 0,
                    IsHitTestVisible = false,
                    IsEnabled = false
                };
                DetailHostGrid.Children.Add(_startView);
            }

            return _startView;
        }

        // the remaining detail views, one per dispatcher pass at Low priority, so the page stays
        // interactive while they come in
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

        // the sidebar mini graph card background: in dark mode the Checked fill of SidebarNavToggleButtonStyle when
        // selected, in light mode the default
        private static Windows.UI.Color? ResolveSelectedGraphBackground(bool isSelected, bool isDarkTheme) =>
            isSelected && isDarkTheme ? (Windows.UI.Color)Application.Current.Resources["ControlFillColorDisabled"] : (Windows.UI.Color?)null;

        // everything that hangs on the page and panel widths: whether both panels fit, and how far each may be dragged
        // (the limits stop the splitter mid drag)
        private void UpdateSidePanelLayout()
        {
            // not laid out yet; ContentGrid_SizeChanged comes back
            double pageWidth = ContentGrid.ActualWidth;
            if (pageWidth <= 0) return;

            double maxWidth = pageWidth * SidePanelMaxWidthShare;

            // each panel width, or the one it comes back at; capped at the maximum
            double sidebarWidth = Math.Min(SidebarColumn.Width.Value, maxWidth);
            double infoPanelWidth = Math.Min(ViewModel.InfoPanelWidth, maxWidth);

            _isNarrow = pageWidth - sidebarWidth - infoPanelWidth < NarrowContentThreshold;

            if (_isNarrow && ViewModel.IsNavSidebarVisible && ViewModel.IsInfoPanelVisible)
            {
                // on a resize the info panel always loses; the toggle handler runs this again, which sets the limits
                ViewModel.IsInfoPanelVisible = false;
                return;
            }

            // with both open, a drag stops at the threshold of the hardware view between them
            bool bothShown = ViewModel.IsNavSidebarShown && ViewModel.IsInfoPanelVisible;
            double sidebarMaxWidth = bothShown ? Math.Min(maxWidth, pageWidth - infoPanelWidth - NarrowContentThreshold) : maxWidth;
            double infoPanelMaxWidth = bothShown ? Math.Min(maxWidth, pageWidth - sidebarWidth - NarrowContentThreshold) : maxWidth;

            ViewModel.SetSidePanelLimits(NavSidebarMinWidth, sidebarMaxWidth, InfoPanelMinWidth, infoPanelMaxWidth);
        }

        // re-measures the current detail view after a panel toggle or width change, dispatched
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
