using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using WinRT;
using WinUIEx;
using WinUIEx.Messaging;
using Microsoft.UI.Xaml.Controls;

using FluentSensors.Common.Localization;
using FluentSensors.Common.Sensors;
using FluentSensors.Common.UI;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Controls.SensorRow;
using FluentSensors.Controls.TimeRange;
using FluentSensors.Core.Taskbar;
using FluentSensors.Persistence.Models;
using FluentSensors.Persistence.Services;
using FluentSensors.Features.Widget;
using FluentSensors.Features.CsvLogging;


namespace FluentSensors.Features.TaskbarWidget
{
    // the taskbar flyout:
    // the live graphs of the taskbar profile, anchored next to the taskbar widget; WinUI has no way to anchor a
    // borderless window to a shell window, so this combines:
    // 1. no titlebar stripe, WM_NCCALCSIZE returning 0
    // 2. a height capped against the work area, the graph list scrolls past it
    // 3. Z-order directly beneath Shell_TrayWnd, so it slides out from under the taskbar
    // 4. a composition slide of the card inside the still window plus an opacity fade of the content
    // 5. a transparent window around the card: the material rides a backdrop link clipped to the card, the
    //    shadow is a computed bitmap in the margin; DWM corners and shadow only when the link is unavailable
    // 6. a MicaController or DesktopAcrylicController backdrop (the materials in BackdropMaterials)
    //
    // references:
    // https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.input.inputnonclientpointersource
    // https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-nccalcsize
    // https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
    // https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops
    public sealed partial class TaskbarFlyoutWindow : Window
    {
        // === win32 api imports ===

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS
        {
            public int cxLeftWidth;
            public int cxRightWidth;
            public int cyTopHeight;
            public int cyBottomHeight;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, uint cbAttribute);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint dwAttribute, out NativeMethods.RECT pvAttribute, int cbAttribute);

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
        private static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
        private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private const int GCL_STYLE = -26;
        private const int CS_DROPSHADOW = 0x00020000;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const uint DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

        private const int GWL_STYLE = -16;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_CAPTION = 0x00C00000;

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private const uint DWMWA_NCRENDERING_POLICY = 2;
        private const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;

        private enum DWMNCRENDERINGPOLICY
        {
            DWMNCRP_USEWINDOWSTYLE = 0,
            DWMNCRP_DISABLED = 1, // no shadow
            DWMNCRP_ENABLED = 2   // standard shadow
        }

        private enum DWM_WINDOW_CORNER_PREFERENCE
        {
            DWMWCP_DEFAULT = 0,
            DWMWCP_DONOTROUND = 1,
            DWMWCP_ROUND = 2, // 8px standard
            DWMWCP_ROUNDSMALL = 3 // 4px
        }


        // --- animation & performance settings ---
        // the card slides inside the still window as a composition translation, like the native flyouts, and the
        // window edge on the taskbar side clips it; it reaches the full refresh rate when the app renders on the GPU
        // that drives the display (on a hybrid laptop set to the other GPU every frame is copied across, measured
        // 165 Hz against 30 to 80)

        // open: ease-out cubic over a fixed duration, only the distance grows with the pinned sensors; (the native
        // Start menu, 726 dip tall, settles in 160 ms, so a taller card must not take longer)
        public const int SlideDistanceDip = 300;
        public const int EnterAnimationDurationMs = 240;

        // close: the Windows Quick Settings curve, measured; accelerating over 515 dip in 167 ms, hidden at its end
        public const int ExitAnimationDurationMs = 167;
        public const int ExitSlideDistanceDip = 515;
        private static readonly Vector2 ExitEasingControlPoint1 = new(0.751f, 0f);
        private static readonly Vector2 ExitEasingControlPoint2 = new(0.950f, 0.612f);

        // scaling of the open distance with the pinned sensor count; tuned for AnimationReferenceSensorCount, each
        // sensor above or below scales it by the factor
        public const int AnimationReferenceSensorCount = 2;
        public const double SlideDistancePerSensorFactor = 0.10;

        // clamps the scaling
        public const double MinAnimationScaleFactor = 0.75;
        public const double MaxAnimationScaleFactor = 3.0;

        // fade opacity; the content only, material, stroke and shadow do not fade
        public const float EnterFadeStartOpacity = 0.0f;
        public const float EnterFadeEndOpacity = 1.0f;
        public const float ExitFadeEndOpacity = 0.9f;

        // keeps the graphs rendering while hidden, so they are current the moment the flyout opens
        public const bool KeepFlyoutGraphsActiveInBackground = true;


        // === fields ===

        private const string WindowKey = "TaskbarFlyout";

        // anchor gaps; (on a horizontal taskbar the taskbar gap is also the gap to the far side of the work
        // area, which caps the height)
        public const int FlyoutMarginToTaskbarDip = 12;
        public const int FlyoutMarginToScreenEdgeDip = 12; // along the taskbar

        // card construction: the window is the card plus a transparent margin the shadow falls into, on every side;
        // the anchor gaps above stay gaps to the card
        public const int FlyoutShadowMarginDip = 12;
        // below the card when no taskbar is there to cut the shadow, so it fades out in full
        public const int FlyoutShadowFalloffMarginDip = 32;

        // inward offset from the widget edge TaskbarFlyoutAlignment picks; (Center ignores it)
        public const int FlyoutAlignmentOffsetDip = 0;

        // fixed width
        public const double FlyoutDefaultWidthDip = AppSettingsData.TaskbarFlyoutWidthDip;

        // height of one graph slot; (the setting)
        private static double FlyoutGraphHeightDip => SettingsService.Instance.TaskbarFlyoutGraphHeightDip;

        // --- flyout layout ---
        // insets and control heights of the rows, from the FlyoutRootBorder resources in the xaml; the height math
        // reads them, since it runs before the rows are ever measured
        private Thickness FlyoutGraphsMargin => LayoutResource<Thickness>("FlyoutGraphsMargin");
        private double FlyoutGraphSpacingDip => LayoutResource<double>("FlyoutGraphSpacing");
        private double FlyoutBarButtonHeightDip => LayoutResource<double>("FlyoutBarButtonHeight");
        private Thickness FlyoutTitleRowPadding => LayoutResource<Thickness>("FlyoutTitleRowPadding");
        private Thickness FlyoutBottomBarPadding => LayoutResource<Thickness>("FlyoutBottomBarPadding");
        private double FlyoutBottomBarSeparatorDip => LayoutResource<Thickness>("FlyoutBottomBarSeparatorThickness").Top;
        private Thickness FlyoutTimeRangeRowPadding => LayoutResource<Thickness>("FlyoutTimeRangeRowPadding");
        private double FlyoutTimeRangePickerHeightDip => LayoutResource<double>("FlyoutTimeRangePickerHeight");

        // the rows in the height math
        private double FlyoutTitleRowHeightDip =>
            FlyoutTitleRowPadding.Top + FlyoutBarButtonHeightDip + FlyoutTitleRowPadding.Bottom;
        private double FlyoutTimeRangeRowHeightDip =>
            FlyoutTimeRangeRowPadding.Top + FlyoutTimeRangePickerHeightDip + FlyoutTimeRangeRowPadding.Bottom;
        private double FlyoutBottomBarHeightDip =>
            FlyoutBottomBarSeparatorDip + FlyoutBottomBarPadding.Top + FlyoutBarButtonHeightDip + FlyoutBottomBarPadding.Bottom;

        private AppWindow _appWindow;
        private IntPtr _hwnd;
        private WindowMessageMonitor _messageMonitor;
        private UISettings? _uiSettings;

        private bool _isAnchored; // set once PositionNextToTaskbar has placed the window
        private int _targetX;
        private int _targetY;
        private bool _isAdjustingPosition;
        private bool _isHiding;

        // sensor count the slide is scaled by; (capped at what fits under the height cap)
        private int _animationSensorCount = AnimationReferenceSensorCount;

        // the items panel, cached on the first layout pass (an ItemsPanelTemplate cannot be named);
        // _isGraphsScrolling holds the mode until then
        private FluentSensors.Controls.VerticalStretchPanel? _graphsPanel;
        private bool _isGraphsScrolling;

        // slide toward the taskbar edge, a unit vector in screen axes, and the open distance in dip; set per open
        private Vector3 _slideDirection = new(0, 1, 0);
        private float _slideDistanceDip = SlideDistanceDip;

        // the close in flight; its batch completes after the animations and hides the window, a newer slide replaces
        // it and its completion is ignored
        private CompositionScopedBatch? _exitBatch;
        private bool _isSlideTurningAround;

        public TaskbarWidgetViewModel ViewModel { get; }
        public static TaskbarFlyoutWindow? CurrentInstance { get; private set; }
        private static TaskbarFlyoutWindow? _retainedInstance;

        // system backdrop; one of the two at a time, see SetBackdrop
        private DesktopAcrylicController? _acrylicController;
        private MicaController? _micaController;
        private SystemBackdropConfiguration? _configurationSource;

        // the backdrop link the card material rides on, one per window and so one per process (the flyout is never
        // closed); closing it off the UI thread kills the process, so a retired one is closed here and parked in
        // _retiredBackdropLinks, out of reach of the finalizer
        private WinBackdropLink? _backdropLink;
        private static readonly List<WinBackdropLink> _retiredBackdropLinks = new();

        // card corner radius and stroke in dip; (CornerRadius and BorderThickness of FlyoutRootBorder)
        private const float CardCornerRadiusDip = 8f;
        private const float CardBorderDip = 1f;

        // edge treatment of the link material and of the placement visual (its rounded clip)
        private const CompositionBorderMode BackdropLinkBorderMode = CompositionBorderMode.Hard;
        private const CompositionBorderMode BackdropPlacementBorderMode = CompositionBorderMode.Soft;

        // the construction the current geometry was placed for, see UsesCardWindow
        private bool _isCardWindow;


        // === constructor ===

        public TaskbarFlyoutWindow(TaskbarWidgetViewModel viewModel)
        {
            ViewModel = viewModel;
            this.InitializeComponent();
            CurrentInstance = this;

            // a click on empty space hides the keyboard focus rectangle again
            PointerFocusReset.Attach(Content);

            // the Button marks its own press and keys handled
            TaskbarSettingsButton.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(TaskbarSettingsButton_PointerPressed), true);
            TaskbarSettingsButton.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(TaskbarSettingsButton_KeyDown), true);

            _appWindow = this.AppWindow;
            _appWindow.IsShownInSwitchers = false;
            _appWindow.SetIcon("Assets\\Icon\\Icon.ico");
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            // frameless presenter, no caption buttons
            var presenter = OverlappedPresenter.Create();
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = false; // sits behind the taskbar
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false; // height follows the sensor count
            _appWindow.SetPresenter(presenter);

            // popup tool window without frame or caption
            int style = GetWindowLong(_hwnd, GWL_STYLE);
            SetWindowLong(_hwnd, GWL_STYLE, (style | WS_POPUP) & ~WS_THICKFRAME & ~WS_CAPTION);

            int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

            // strip CS_DROPSHADOW class style
            try
            {
                IntPtr classStyle = GetClassLongPtr(_hwnd, GCL_STYLE);
                long newClassStyle = classStyle.ToInt64() & ~CS_DROPSHADOW;
                SetClassLongPtr(_hwnd, GCL_STYLE, new IntPtr(newClassStyle));
            }
            catch { }

            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            // win32 hooks; WM_NCCALCSIZE drops the 8px titlebar stripe, WM_SYSCOMMAND blocks moving
            _messageMonitor = new WindowMessageMonitor(_hwnd);
            _messageMonitor.WindowMessageReceived += OnWindowMessageReceived;

            // before the first backdrop, which picks the construction from it
            EnsureBackdropLink();


            // shadow policy follows the Windows transparency setting
            InitializeShadowPolicy();

            // theming and backdrop, from the taskbar settings
            SetBackdrop(SettingsService.Instance.TaskbarBackgroundMaterial);
            ApplyTheme(SettingsService.Instance.AppTheme);

            SettingsService.Instance.ThemeChanged += OnThemeChanged;
            SettingsService.Instance.TaskbarBackgroundMaterialChanged += OnBackgroundMaterialChanged;
            SettingsService.Instance.TaskbarOpacityChanged += OnOpacityChanged;
            SettingsService.Instance.TaskbarTintColorChanged += OnTintColorChanged;
            SettingsService.Instance.TaskbarFlyoutAlignmentChanged += OnFlyoutAlignmentChanged;
            SettingsService.Instance.TaskbarFlyoutGraphHeightChanged += OnFlyoutGraphHeightChanged;

            // the two time ranges; the pickers and the settings page write the same settings
            TaskbarTimeRangePicker.SelectedSeconds = SettingsService.Instance.TaskbarGraphTimeSpanSeconds;
            FlyoutTimeRangePicker.SelectedSeconds = SettingsService.Instance.TaskbarFlyoutGraphTimeSpanSeconds;
            TaskbarTimeRangePicker.RegisterPropertyChangedCallback(TimeRangePickerControl.SelectedSecondsProperty, OnTaskbarTimeRangePicked);
            FlyoutTimeRangePicker.RegisterPropertyChangedCallback(TimeRangePickerControl.SelectedSecondsProperty, OnFlyoutTimeRangePicked);
            SettingsService.Instance.TaskbarGraphTimeSpanChanged += OnTaskbarGraphTimeSpanChanged;
            SettingsService.Instance.TaskbarFlyoutGraphTimeSpanChanged += OnFlyoutGraphTimeSpanChanged;

            // a rebuilt window takes over a running snapshot with the view model
            ApplyPauseState();

            FlyoutShortcutRegistration.RegistrationChanged += OnShortcutRegistrationChanged;
            ApplyShortcutHint();

            ((FrameworkElement)this.Content).ActualThemeChanged += (s, e) =>
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    SetConfigurationSourceTheme();
                    UpdateAcrylicProperties();
                    UpdateSolidBackground();
                });
            };

            // the card slide animates the translation of the window content on its composition visual
            ElementCompositionPreview.SetIsTranslationEnabled(FlyoutWindowRoot, true);

            GraphsItemsControl.LayoutUpdated += OnGraphsItemsControlLayoutUpdated;

            _appWindow.Changed += AppWindow_Changed;
            FlyoutRootBorder.SizeChanged += FlyoutRootBorder_SizeChanged;
            _appWindow.Closing += AppWindow_Closing;
            this.Activated += Window_Activated;
        }


        // === backdrop link ===

        // creates the link and hangs its visual behind the card; without it the flyout keeps the window level
        // construction
        private void EnsureBackdropLink()
        {
            if (_backdropLink != null) return;

            WinBackdropLink? link = null;
            try
            {
                var compositor = ElementCompositionPreview.GetElementVisual(FlyoutBackdropHost).Compositor;
                link = WinBackdropLink.Create(compositor);
                link.BorderMode = BackdropLinkBorderMode;
                link.PlacementVisual.BorderMode = BackdropPlacementBorderMode;

                ElementCompositionPreview.SetElementChildVisual(FlyoutBackdropHost, link.PlacementVisual);
                FlyoutBackdropHost.SizeChanged += FlyoutBackdropHost_SizeChanged;

                _backdropLink = link;
                Debug.WriteLine($"[BackdropLink] created via {link.ActivationRoute}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BackdropLink] failed: 0x{ex.HResult:X8} {ex.GetType().Name}: {ex.Message}");
                if (link != null) RetireLink(link);
            }
        }

        // the material stops inside the card stroke, so the stroke lies over the backdrop and the shadow like the
        // native one; sizes are in dip, the inset follows the pixel snapped stroke (a scale change resizes the host)
        private void FlyoutBackdropHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_backdropLink == null) return;

            var visual = _backdropLink.PlacementVisual;
            float width = (float)e.NewSize.Width;
            float height = (float)e.NewSize.Height;
            visual.Size = new Vector2(width, height);

            double scale = FlyoutBackdropHost.XamlRoot?.RasterizationScale ?? 1.0;
            float inset = (float)(CardStrokePx(scale) / scale);
            // the corners 1 px tighter: clip and stroke antialias the same corner pixels, and their partial coverages
            // left a hairline of backdrop between them; this way the material reaches under the stroke edge there
            var radius = new Vector2(CardCornerRadiusDip - inset - (float)(1.0 / scale));
            visual.Clip = visual.Compositor.CreateRectangleClip(
                inset, inset, width - inset, height - inset, radius, radius, radius, radius);
        }

        // the card stroke in physical px; layout rounding snaps the 1 dip stroke to whole pixels (2 at 175 percent,
        // both rows read as pure stroke over the backdrop), so material and shadow cut-out start behind that
        private static double CardStrokePx(double scale) => Math.Max(1.0, Math.Round(CardBorderDip * scale));

        // drops the link for good: no controller may target it any more (UI thread only, see _backdropLink)
        private void RetireBackdropLink()
        {
            if (_backdropLink == null) return;

            var link = _backdropLink;
            _backdropLink = null;
            FlyoutBackdropHost.SizeChanged -= FlyoutBackdropHost_SizeChanged;

            try
            {
                ElementCompositionPreview.SetElementChildVisual(FlyoutBackdropHost, null);
            }
            catch { }

            RetireLink(link);
        }

        private static void RetireLink(WinBackdropLink link)
        {
            _retiredBackdropLinks.Add(link);
            try
            {
                link.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BackdropLink] close failed: 0x{ex.HResult:X8} {ex.Message}");
            }
        }

        // the card construction, whenever the link exists: the window is transparent, the material rides the card
        private bool UsesCardWindow => _backdropLink != null;


        // === shadow policy ===

        private void InitializeShadowPolicy()
        {
            try
            {
                _uiSettings = new UISettings();
                SeedSystemVisualsSnapshot(_uiSettings);
                _uiSettings.AdvancedEffectsEnabledChanged += OnSystemVisualSettingsChanged;
                _uiSettings.ColorValuesChanged += OnSystemVisualSettingsChanged;
                UpdateShadowPolicy();
            }
            catch
            {
                // UISettings unavailable; no shadow policy
            }
        }

        // goes through the router, since ColorValuesChanged also fires for a pure accent change that needs no rebuild
        private void OnSystemVisualSettingsChanged(UISettings sender, object args)
        {
            RouteSystemVisualsChange(sender);
        }

        private void UpdateShadowPolicy()
        {
            if (_hwnd == IntPtr.Zero) return;

            bool isTransparencyEnabled = _uiSettings != null && _uiSettings.AdvancedEffectsEnabled;

            // the card window takes the flat branch in both states; the card draws corners and border itself
            if (isTransparencyEnabled && !UsesCardWindow)
            {
                // transparency on; DWM rounds the corners and clips the acrylic (the native shadow comes with it)
                int cornerPreference = (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
                DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

                var margins = new MARGINS { cxLeftWidth = 0, cxRightWidth = 0, cyTopHeight = 1, cyBottomHeight = 0 };
                DwmExtendFrameIntoClientArea(_hwnd, ref margins);

                int policy = (int)DWMNCRENDERINGPOLICY.DWMNCRP_DISABLED;
                DwmSetWindowAttribute(_hwnd, DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));
            }
            else
            {
                // transparency off or the card window; no DWM rounding and no shadow, the XAML border draws the 8px
                // corners
                int cornerPreference = (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;
                DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

                var margins = new MARGINS { cxLeftWidth = 0, cxRightWidth = 0, cyTopHeight = 0, cyBottomHeight = 0 };
                DwmExtendFrameIntoClientArea(_hwnd, ref margins);

                int policy = (int)DWMNCRENDERINGPOLICY.DWMNCRP_DISABLED;
                DwmSetWindowAttribute(_hwnd, DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));

                int borderColor = DWMWA_COLOR_NONE;
                DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
            }

            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }


        // === win32 message handling ===

        private void OnWindowMessageReceived(object? sender, WindowMessageEventArgs e)
        {
            if (e.Message.MessageId == 0x0083) // WM_NCCALCSIZE
            {
                if (e.Message.WParam != 0)
                {
                    // client area covers the whole window rect
                    e.Result = IntPtr.Zero;
                    e.Handled = true;
                }
            }
            else if (e.Message.MessageId == 0x0112) // WM_SYSCOMMAND
            {
                if ((e.Message.WParam.ToUInt32() & 0xFFF0) == 0xF010) // SC_MOVE
                {
                    e.Result = IntPtr.Zero;
                    e.Handled = true;
                }
            }
        }


        // === public methods ===

        // re-places the flyout for the current pinned sensor count
        public static void ResetGeometry()
        {
            if (CurrentInstance != null && TaskbarWidgetWindow.CurrentInstance != null)
            {
                CurrentInstance.PositionNextToTaskbar(TaskbarWidgetWindow.CurrentInstance);
            }
            else if (_retainedInstance != null && TaskbarWidgetWindow.CurrentInstance != null)
            {
                _retainedInstance.PositionNextToTaskbar(TaskbarWidgetWindow.CurrentInstance);
            }
        }

        // builds the flyout at taskbar startup, so the first open has no latency
        public static void Preload(TaskbarWidgetWindow widgetWindow)
        {
            // assumes a retained instance is live (a destroyed one here leaves the flyout dead, see AppWindow_Closing)
            if (widgetWindow == null || CurrentInstance != null || _retainedInstance != null) return;

            var window = new TaskbarFlyoutWindow(widgetWindow.ViewModel);
            _retainedInstance = window;
            window.PositionNextToTaskbar(widgetWindow);

            if (KeepFlyoutGraphsActiveInBackground)
            {
                window.SetGraphsRenderingActive(true);
            }
        }

        private void EnsureBehindTaskbarZOrder()
        {
            var taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd != IntPtr.Zero)
            {
                // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_NOOWNERZORDER
                SetWindowPos(_hwnd, taskbarHwnd, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
            }
        }

        public static void Toggle(TaskbarWidgetWindow widgetWindow)
        {
            if (widgetWindow == null) return;

            if (CurrentInstance != null && CurrentInstance._appWindow.IsVisible && !CurrentInstance._isHiding)
            {
                CurrentInstance.HideFlyout();
                return;
            }

            ShowFlyout(widgetWindow);
        }

        public static void ShowFlyout(TaskbarWidgetWindow widgetWindow)
        {
            if (widgetWindow == null) return;

            if (CurrentInstance != null)
            {
                CurrentInstance.ApplyTheme(SettingsService.Instance.AppTheme);
                CurrentInstance.PositionNextToTaskbar(widgetWindow);
                CurrentInstance.SetGraphsRenderingActive(true);
                CurrentInstance.PrepareSlideIn();
                CurrentInstance._appWindow.Show();
                CurrentInstance.EnsureBehindTaskbarZOrder();
                CurrentInstance.Activate();
                CurrentInstance.SlideIn();
                return;
            }

            if (_retainedInstance != null)
            {
                var window = _retainedInstance;
                _retainedInstance = null;
                CurrentInstance = window;

                window.ApplyTheme(SettingsService.Instance.AppTheme);
                window.PositionNextToTaskbar(widgetWindow);
                window.SetGraphsRenderingActive(true);
                window.PrepareSlideIn();
                window._appWindow.Show();
                window.EnsureBehindTaskbarZOrder();
                window.Activate();
                window.SlideIn();
                return;
            }

            var newWindow = new TaskbarFlyoutWindow(widgetWindow.ViewModel);
            newWindow.ApplyTheme(SettingsService.Instance.AppTheme);
            newWindow.PositionNextToTaskbar(widgetWindow);
            newWindow.SetGraphsRenderingActive(true);
            newWindow.PrepareSlideIn();
            newWindow._appWindow.Show();
            newWindow.EnsureBehindTaskbarZOrder();
            newWindow.Activate();
            newWindow.SlideIn();
        }

        public void HideFlyout()
        {
            if (_isHiding || !_appWindow.IsVisible) return;
            _isHiding = true;

            SlideOut(() =>
            {
                _isHiding = false;
                SetGraphsRenderingActive(false);
                SaveWindowState();
                _appWindow.Hide();
                TaskbarWidgetWindow.CurrentInstance?.SetFlyoutActive(false);
            });
        }

        private static Microsoft.UI.Dispatching.DispatcherQueueTimer? _recreateDebounceTimer;
        private static Microsoft.UI.Dispatching.DispatcherQueueTimer? _accentRefreshDebounceTimer;

        // last seen OS visual state; (ColorValuesChanged does not say whether accent or theme
        // changed, so snapshots are compared)
        private static Windows.UI.Color _lastAccentColor;
        private static Windows.UI.Color _lastBackgroundColor;
        private static bool _lastAdvancedEffectsEnabled;
        private static bool _hasVisualSnapshot;

        // baseline before the first UISettings event, so that event diffs against real state instead of forcing a
        // rebuild (called from both WidgetWindow and this constructor, calling twice is harmless)
        public static void SeedSystemVisualsSnapshot(UISettings settings)
        {
            _lastAccentColor = settings.GetColorValue(UIColorType.Accent);
            _lastBackgroundColor = settings.GetColorValue(UIColorType.Background);
            _lastAdvancedEffectsEnabled = settings.AdvancedEffectsEnabled;
            _hasVisualSnapshot = true;
        }

        // routes a Windows visual settings change to the in-place accent refresh or the full rebuild
        // (both windows land here for the same OS event; the second call sees no difference and does nothing)
        public static void RouteSystemVisualsChange(UISettings sender)
        {
            var accent = sender.GetColorValue(UIColorType.Accent);
            var background = sender.GetColorValue(UIColorType.Background);
            bool advancedEffects = sender.AdvancedEffectsEnabled;

            if (!_hasVisualSnapshot)
            {
                SeedSystemVisualsSnapshot(sender);
                ScheduleRecreation();
                return;
            }

            bool accentChanged = !accent.Equals(_lastAccentColor);
            bool structuralChanged = !background.Equals(_lastBackgroundColor) || advancedEffects != _lastAdvancedEffectsEnabled;

            _lastAccentColor = accent;
            _lastBackgroundColor = background;
            _lastAdvancedEffectsEnabled = advancedEffects;

            if (structuralChanged)
            {
                ScheduleRecreation();
            }
            else if (accentChanged)
            {
                ScheduleAccentRefresh();
            }
        }

        // debounced pure accent change, refreshed in place; (150ms like ScheduleRecreation, a picker drag raises
        // ColorValuesChanged repeatedly)
        private static void ScheduleAccentRefresh()
        {
            var queue = TaskbarWidgetWindow.CurrentInstance?.DispatcherQueue
                ?? MainWindow.CurrentInstance?.DispatcherQueue
                ?? DispatcherQueue.GetForCurrentThread();

            if (queue == null) return;

            queue.TryEnqueue(() =>
            {
                if (_accentRefreshDebounceTimer != null)
                {
                    _accentRefreshDebounceTimer.Stop();
                    _accentRefreshDebounceTimer = null;
                }

                _accentRefreshDebounceTimer = queue.CreateTimer();
                _accentRefreshDebounceTimer.Interval = TimeSpan.FromMilliseconds(150);
                _accentRefreshDebounceTimer.IsRepeating = false;
                _accentRefreshDebounceTimer.Tick += (s, e) =>
                {
                    _accentRefreshDebounceTimer?.Stop();
                    _accentRefreshDebounceTimer = null;
                    ExecuteAccentRefresh();
                };
                _accentRefreshDebounceTimer.Start();
            });
        }

        // refreshes the accent surfaces of every open window: graph colors (and the taskbar card tint riding on them),
        // acrylic tint and solid background; the flyout shares the taskbar widget ViewModel, so its graphs come along
        private static void ExecuteAccentRefresh()
        {
            TaskbarWidgetWindow.CurrentInstance?.ViewModel.RefreshGraphColors();

            foreach (var widget in WidgetWindow.OpenInstances)
            {
                widget.ViewModel.RefreshGraphColors();
                widget.RefreshAccentSurfaces();
            }

            CsvLoggerWindow.CurrentInstance?.RefreshAccentSurfaces();

            var flyout = CurrentInstance ?? _retainedInstance;
            flyout?.RefreshAccentSurfaces();
        }

        // --- workaround: window recreation on global OS theme/transparency change ---
        // problem: after a Windows theme or transparency change, DWM does not bind the DesktopAcrylicController blur
        // without a full window recreation (empirical, cause unknown)
        // fix: destroy and rebuild every open window (WidgetWindow, TaskbarWidgetWindow, CsvLoggerWindow); the acrylic
        // ones kick their backdrop from the constructor via KickBackdropRefresh
        // the flyout is left out: its material binds without either, on the link and on the window, so it only applies
        // the new state (measured on build 26300, Intel and NVIDIA rendering)
        // only the Windows-level UISettings events land here; never toggle a persisted setting to force a repaint
        // instead (an interrupted toggle persists its intermediate value)
        public static void ScheduleRecreation()
        {
            var queue = TaskbarWidgetWindow.CurrentInstance?.DispatcherQueue
                ?? MainWindow.CurrentInstance?.DispatcherQueue
                ?? DispatcherQueue.GetForCurrentThread();

            if (queue == null) return;

            queue.TryEnqueue(() =>
            {
                if (_recreateDebounceTimer != null)
                {
                    _recreateDebounceTimer.Stop();
                    _recreateDebounceTimer = null;
                }

                _recreateDebounceTimer = queue.CreateTimer();
                _recreateDebounceTimer.Interval = TimeSpan.FromMilliseconds(150);
                _recreateDebounceTimer.IsRepeating = false;
                _recreateDebounceTimer.Tick += (s, e) =>
                {
                    _recreateDebounceTimer?.Stop();
                    _recreateDebounceTimer = null;
                    ExecuteFullRebuild();
                };
                _recreateDebounceTimer.Start();
            });
        }

        // the destructive half of the ScheduleRecreation workaround; the flyout stays and only applies the new state,
        // an open one hides and comes back last, since it anchors to the new taskbar widget
        private static void ExecuteFullRebuild()
        {
            var flyout = CurrentInstance ?? _retainedInstance;
            bool flyoutWasVisible = flyout != null && flyout._appWindow.IsVisible;

            // 1. the flyout
            flyout?.ApplySystemVisuals(flyoutWasVisible);

            // 2. every WidgetWindow, if open
            WidgetWindow.RecreateWindows();

            // 3. TaskbarWidgetWindow; (the rebuilt widget reopens the flyout itself once embedded)
            TaskbarWidgetWindow.RecreateWindow(restoreFlyout: flyoutWasVisible);

            // 4. CsvLoggerWindow; (a running recording lives in CsvLoggingService and is unaffected)
            CsvLoggerWindow.RecreateWindow();
        }


        // a Windows theme or transparency change; an open flyout hides at once, without the slide, so the rebuilt
        // widget can reopen it at its new place
        private void ApplySystemVisuals(bool hide)
        {
            if (hide)
            {
                CancelSlideOut();
                SetGraphsRenderingActive(false);
                _appWindow.Hide();
            }

            SetBackdrop(SettingsService.Instance.TaskbarBackgroundMaterial);
            ApplyTheme(SettingsService.Instance.AppTheme);
        }


        // === card slide and content fade ===

        // the card at its start before the window shows, so the first frame never shows it at the final place; a
        // reopen during the close turns around where the card is instead
        private void PrepareSlideIn()
        {
            CancelSlideOut();
            _isSlideTurningAround = _appWindow.IsVisible;

            if (_isCardWindow && !_isSlideTurningAround)
            {
                var visual = ElementCompositionPreview.GetElementVisual(FlyoutWindowRoot);
                visual.StopAnimation("Translation");
                visual.Properties.InsertVector3("Translation", _slideDirection * _slideDistanceDip);
            }
        }

        // out from behind the taskbar edge, our ease-out cubic; the window level construction only fades, its
        // backdrop fills the window rect and would stay behind as a plate
        private void SlideIn()
        {
            TaskbarWidgetWindow.CurrentInstance?.SetFlyoutActive(true);
            CancelSlideOut();

            var compositor = ElementCompositionPreview.GetElementVisual(FlyoutWindowRoot).Compositor;
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(1f / 3f, 1f), new Vector2(2f / 3f, 1f));

            PlayContentFade(_isSlideTurningAround ? null : EnterFadeStartOpacity, EnterFadeEndOpacity, EnterAnimationDurationMs, easing);

            if (_isCardWindow)
            {
                PlayCardSlide(_isSlideTurningAround ? null : _slideDirection * _slideDistanceDip, Vector3.Zero, EnterAnimationDurationMs, easing);
            }
        }

        // back behind the taskbar edge on the Quick Settings curve, from wherever the open has got to; the window
        // hides once the batch completes
        private void SlideOut(Action onCompleted)
        {
            TaskbarWidgetWindow.CurrentInstance?.SetFlyoutActive(false);

            var compositor = ElementCompositionPreview.GetElementVisual(FlyoutWindowRoot).Compositor;
            var easing = compositor.CreateCubicBezierEasingFunction(ExitEasingControlPoint1, ExitEasingControlPoint2);

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            _exitBatch = batch;

            PlayContentFade(null, ExitFadeEndOpacity, ExitAnimationDurationMs, easing);

            if (_isCardWindow)
            {
                PlayCardSlide(null, _slideDirection * ExitSlideDistanceDip, ExitAnimationDurationMs, easing);
            }

            batch.End();
            batch.Completed += (s, e) => DispatcherQueue.TryEnqueue(() =>
            {
                if (_exitBatch != batch) return;
                _exitBatch = null;
                onCompleted();
            });
        }

        // drops a close in flight, so its completion no longer hides the window (a reopen during the close)
        private void CancelSlideOut()
        {
            _exitBatch = null;
            _isHiding = false;
        }

        // opacity of the content only; from null starts at the current value, so a close can interrupt an open
        private void PlayContentFade(float? fromOpacity, float toOpacity, int durationMs, CompositionEasingFunction easing)
        {
            if (RootGrid == null) return;
            var visual = ElementCompositionPreview.GetElementVisual(RootGrid);

            var opacityAnim = visual.Compositor.CreateScalarKeyFrameAnimation();
            if (fromOpacity is float from)
            {
                opacityAnim.InsertKeyFrame(0.0f, from);
            }
            else
            {
                opacityAnim.InsertExpressionKeyFrame(0.0f, "this.StartingValue");
            }
            opacityAnim.InsertKeyFrame(1.0f, toOpacity, easing);
            opacityAnim.Duration = TimeSpan.FromMilliseconds(durationMs);
            visual.StartAnimation("Opacity", opacityAnim);
        }

        // the translation of the whole window content (shadow, material, card), in dip; from null as above
        // on the handoff visual only (start value included, see PrepareSlideIn); the XAML Translation property is
        // never set, a static value there wins against the animation and leaves the card at its start
        private void PlayCardSlide(Vector3? from, Vector3 to, int durationMs, CompositionEasingFunction easing)
        {
            var visual = ElementCompositionPreview.GetElementVisual(FlyoutWindowRoot);

            var slideAnim = visual.Compositor.CreateVector3KeyFrameAnimation();
            if (from is Vector3 start)
            {
                slideAnim.InsertKeyFrame(0.0f, start);
            }
            else
            {
                slideAnim.InsertExpressionKeyFrame(0.0f, "this.StartingValue");
            }
            slideAnim.InsertKeyFrame(1.0f, to, easing);
            slideAnim.Duration = TimeSpan.FromMilliseconds(durationMs);
            visual.StartAnimation("Translation", slideAnim);
        }

        // shared scaling law: how far a value moves from its base with the number of graph rows shown
        private double AnimationScaleFactor(double perSensorFactor)
        {
            double factor = 1.0 + ((_animationSensorCount - AnimationReferenceSensorCount) * perSensorFactor);

            return Math.Clamp(factor, MinAnimationScaleFactor, MaxAnimationScaleFactor);
        }


        // === window sizing and positioning ===

        // places the flyout beside the taskbar widget, on the desktop side of the taskbar edge: along the taskbar per
        // TaskbarFlyoutAlignment, FlyoutMarginToTaskbarDip off it, clamped to the primary work area
        private void PositionNextToTaskbar(TaskbarWidgetWindow widgetWindow)
        {
            var widgetHwnd = WinRT.Interop.WindowNative.GetWindowHandle(widgetWindow);
            NativeMethods.GetWindowRect(widgetHwnd, out var widgetRect);

            var primaryTaskbar = WinTaskbarService.Instance.DiscoverNow().FirstOrDefault();
            double scale = primaryTaskbar != null ? (primaryTaskbar.Dpi / 96.0) : GetScaleFactor();

            // without a taskbar the widget rect stands in for it, on the default bottom edge
            var edge = primaryTaskbar?.Edge ?? ScreenEdge.Bottom;
            var bar = primaryTaskbar?.Rect
                ?? new RectInt32(widgetRect.Left, widgetRect.Top, widgetRect.Right - widgetRect.Left, widgetRect.Bottom - widgetRect.Top);

            int desiredWidthPx = (int)Math.Round(FlyoutDefaultWidthDip * scale);

            int sensorCount = ViewModel.FlyoutSensors.Count;

            // gaps and alignment offset
            int marginPx = (int)Math.Round(FlyoutMarginToTaskbarDip * scale);
            int offsetPx = (int)Math.Round(FlyoutAlignmentOffsetDip * scale);
            int edgeMarginPx = (int)Math.Round(FlyoutMarginToScreenEdgeDip * scale);

            // height cap: the work area less the taskbar gap at both ends (horizontal taskbar) or the screen edge
            // gap at both ends (vertical), floored at one graph slot; past it the graphs scroll
            var workArea = DisplayArea.Primary.WorkArea;
            int availableHeightPx = edge switch
            {
                ScreenEdge.Top => (workArea.Y + workArea.Height - marginPx) - (bar.Y + bar.Height + marginPx),
                ScreenEdge.Left or ScreenEdge.Right => workArea.Height - (2 * edgeMarginPx),
                _ => (bar.Y - marginPx) - (workArea.Y + marginPx)
            };
            int maxHeightPx = Math.Max(CalculateFlyoutDefaultHeight(1, scale), availableHeightPx);

            int desiredHeightPx = CalculateFlyoutDefaultHeight(sensorCount, scale);
            bool isScrolling = desiredHeightPx > maxHeightPx;
            if (isScrolling)
            {
                desiredHeightPx = maxHeightPx;
            }

            // a capped window stops growing, so the slide scaling stops too
            _animationSensorCount = isScrolling ? CountFittingGraphSlots(maxHeightPx, scale) : sensorCount;
            ApplyGraphsScrollMode(isScrolling);

            if (edge is ScreenEdge.Left or ScreenEdge.Right)
            {
                // beside the taskbar; Left/Right of the alignment setting anchor to the top and bottom widget edge
                _targetX = edge == ScreenEdge.Left
                    ? bar.X + bar.Width + marginPx
                    : bar.X - marginPx - desiredWidthPx;
                _targetY = SettingsService.Instance.TaskbarFlyoutAlignment switch
                {
                    "Left" => widgetRect.Top + offsetPx,
                    "Right" => widgetRect.Bottom - desiredHeightPx - offsetPx,
                    _ => ((widgetRect.Top + widgetRect.Bottom) / 2) - (desiredHeightPx / 2)
                };

                // clamp to the work area with edgeMarginPx; (the top edge wins when both do not fit)
                int bottomLimitY = workArea.Y + workArea.Height - desiredHeightPx - edgeMarginPx;
                int topLimitY = workArea.Y + edgeMarginPx;
                _targetY = Math.Max(topLimitY, Math.Min(_targetY, bottomLimitY));
            }
            else
            {
                // Left/Right anchor to the matching widget edge plus the offset; Center lines up the centers
                _targetX = SettingsService.Instance.TaskbarFlyoutAlignment switch
                {
                    "Left" => widgetRect.Left + offsetPx,
                    "Right" => widgetRect.Right - desiredWidthPx - offsetPx,
                    _ => ((widgetRect.Left + widgetRect.Right) / 2) - (desiredWidthPx / 2)
                };
                _targetY = edge == ScreenEdge.Top
                    ? bar.Y + bar.Height + marginPx
                    : bar.Y - marginPx - desiredHeightPx;

                // clamp to the work area with edgeMarginPx; (the left edge wins when both do not fit, the height cap
                // already keeps the far edge on marginPx)
                int rightLimitX = workArea.X + workArea.Width - desiredWidthPx - edgeMarginPx;
                int leftLimitX = workArea.X + edgeMarginPx;
                _targetX = Math.Max(leftLimitX, Math.Min(_targetX, rightLimitX));
            }

            _isAnchored = true;

            // everything above placed the card; the card window grows around it by the shadow margin, the larger one
            // at the bottom unless the taskbar sits there and cuts the shadow at its edge
            _isCardWindow = UsesCardWindow;
            int shadowPx = _isCardWindow ? (int)Math.Round(FlyoutShadowMarginDip * scale) : 0;
            int shadowBottomPx = !_isCardWindow ? 0 : (int)Math.Round(
                (edge == ScreenEdge.Bottom ? FlyoutShadowMarginDip : FlyoutShadowFalloffMarginDip) * scale);

            _targetX -= shadowPx;
            _targetY -= shadowPx;
            int windowWidthPx = desiredWidthPx + (2 * shadowPx);
            int windowHeightPx = desiredHeightPx + shadowPx + shadowBottomPx;

            // the same pixels as XAML margin, so the card lands on exactly the rect computed above
            var cardMargin = new Thickness(shadowPx / scale, shadowPx / scale, shadowPx / scale, shadowBottomPx / scale);
            FlyoutRootBorder.Margin = cardMargin;
            FlyoutBackdropHost.Margin = cardMargin;

            _shadowWindowSizePx = new SizeInt32(windowWidthPx, windowHeightPx);
            _shadowCardRectPx = new RectInt32(shadowPx, shadowPx, desiredWidthPx, desiredHeightPx);
            _shadowScale = scale;

            // the card slides toward the taskbar edge, where the window edge clips it
            _slideDirection = edge switch
            {
                ScreenEdge.Top => new Vector3(0, -1, 0),
                ScreenEdge.Left => new Vector3(-1, 0, 0),
                ScreenEdge.Right => new Vector3(1, 0, 0),
                _ => new Vector3(0, 1, 0)
            };
            _slideDistanceDip = (float)(SlideDistanceDip * AnimationScaleFactor(SlideDistancePerSensorFactor));

            _isAdjustingPosition = true;
            _appWindow.MoveAndResize(new RectInt32(_targetX, _targetY, windowWidthPx, windowHeightPx));
            _isAdjustingPosition = false;

            UpdateShadowPolicy();
            RenderCardShadow();
        }

        private double GetScaleFactor()
        {
            uint dpi = GetDpiForWindow(_hwnd);
            return dpi / 96.0;
        }

        private int CalculateFlyoutDefaultHeight(int sensorCount, double scaleFactor)
        {
            return (int)(CalculateFlyoutContentHeight(sensorCount, FlyoutGraphHeightDip) * scaleFactor);
        }

        // graph slots that fit under a capped window height
        private int CountFittingGraphSlots(int maxHeightPx, double scaleFactor)
        {
            double interiorDip = (maxHeightPx / scaleFactor) - FlyoutRowsHeightDip
                - FlyoutGraphsMargin.Top - FlyoutGraphsMargin.Bottom;

            int slots = (int)Math.Floor(
                (interiorDip + FlyoutGraphSpacingDip) / (FlyoutGraphHeightDip + FlyoutGraphSpacingDip));

            return Math.Max(1, slots);
        }

        // the title row, the time range row and the bar strip, everything but the graphs
        private double FlyoutRowsHeightDip =>
            FlyoutTitleRowHeightDip + FlyoutTimeRangeRowHeightDip + FlyoutBottomBarHeightDip;

        // window height for n graph slots: the rows around them, the graphs margin, n slots and the n-1 gaps
        private double CalculateFlyoutContentHeight(int sensorCount, double graphHeightDip)
        {
            if (sensorCount <= 0) return FlyoutRowsHeightDip;

            return FlyoutRowsHeightDip
                + FlyoutGraphsMargin.Top + FlyoutGraphsMargin.Bottom
                + (sensorCount * graphHeightDip)
                + ((sensorCount - 1) * FlyoutGraphSpacingDip);
        }

        // a layout value from the FlyoutRootBorder resources
        private T LayoutResource<T>(string key) => (T)FlyoutRootBorder.Resources[key];

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (_isAdjustingPosition) return;

            if (args.DidSizeChange && _isAnchored)
            {
                UpdateShadowPolicy();
                SaveWindowState();
            }
            else if (args.DidPositionChange && _appWindow.IsVisible)
            {
                SaveWindowState();
            }
        }

        private void SaveWindowState()
        {
            if (!_appWindow.IsVisible || _isHiding) return;

            var state = WindowStateService.Instance.GetState(WindowKey) ?? new Persistence.Models.WindowState();
            state.X = _appWindow.Position.X;
            state.Y = _appWindow.Position.Y;
            state.WasOpen = false;

            // the size is derived on every open, never persisted; zeroed so no stale size survives
            state.Width = 0;
            state.Height = 0;

            WindowStateService.Instance.SetState(WindowKey, state);
        }

        // pushes the scroll mode onto the unnamed items panel, once the first layout pass has created it
        private void OnGraphsItemsControlLayoutUpdated(object? sender, object e)
        {
            if (GraphsItemsControl.ItemsPanelRoot is FluentSensors.Controls.VerticalStretchPanel panel)
            {
                _graphsPanel = panel;
                ApplyGraphsScrollMode(_isGraphsScrolling);
                GraphsItemsControl.LayoutUpdated -= OnGraphsItemsControlLayoutUpdated;
            }
        }

        // fills the window, or stacks fixed-height slots in a scroll region (a ScrollViewer measures with infinite
        // height, an equal split has nothing to divide); two explicit modes, so a one pixel rounding difference
        // cannot put a scrollbar on a window that fits
        private void ApplyGraphsScrollMode(bool isScrolling)
        {
            _isGraphsScrolling = isScrolling;

            if (GraphsScrollViewer != null)
            {
                GraphsScrollViewer.VerticalScrollMode = isScrolling ? ScrollMode.Enabled : ScrollMode.Disabled;
                GraphsScrollViewer.VerticalScrollBarVisibility = isScrolling
                    ? ScrollBarVisibility.Auto
                    : ScrollBarVisibility.Disabled;
            }

            if (_graphsPanel != null)
            {
                _graphsPanel.FixedItemHeight = isScrolling ? FlyoutGraphHeightDip : 0;
            }
        }

        private void SetGraphsRenderingActive(bool active)
        {
            if (KeepFlyoutGraphsActiveInBackground && !active) return;

            if (this.Content is DependencyObject root)
            {
                SensorGraphRenderingGate.SetActive(root, active);
            }
        }


        // === user interaction & dismiss ===

        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            // always input active; a light-dismiss flyout is deactivated the moment focus leaves it, which would drop
            // the blur while it is still on screen
            if (_configurationSource != null)
            {
                _configurationSource.IsInputActive = true;
            }

            // light dismiss
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                // a click on the taskbar widget is left to its own toggle
                if (TaskbarWidgetWindow.CurrentInstance != null)
                {
                    var widgetHwnd = WinRT.Interop.WindowNative.GetWindowHandle(TaskbarWidgetWindow.CurrentInstance);
                    NativeMethods.GetWindowRect(widgetHwnd, out var widgetRect);
                    NativeMethods.GetCursorPos(out var cursorPos);

                    if (cursorPos.X >= widgetRect.Left && cursorPos.X <= widgetRect.Right &&
                        cursorPos.Y >= widgetRect.Top && cursorPos.Y <= widgetRect.Bottom)
                    {
                        return;
                    }
                }

                HideFlyout();
            }
            else if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                EnsureBehindTaskbarZOrder();
            }
        }

        // opens the sensor list on the taskbar profile, the one thing the flyout cannot do itself
        private void BackToDashboard_Click(object sender, RoutedEventArgs e)
        {
            HideFlyout();

            if (MainWindow.CurrentInstance != null)
            {
                MainWindow.CurrentInstance.OpenSensorsForProfile(SensorSelectionProfile.Taskbar);
            }
            else
            {
                var newMainWindow = new MainWindow();
                newMainWindow.Activate();
                newMainWindow.OpenSensorsForProfile(SensorSelectionProfile.Taskbar);
            }
        }

        // the gear turns right on every press, pointer or key
        private void TaskbarSettingsButton_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            SpinSettingsGear();
        }

        private void TaskbarSettingsButton_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if ((e.Key == Windows.System.VirtualKey.Enter || e.Key == Windows.System.VirtualKey.Space) && !e.KeyStatus.WasKeyDown)
            {
                SpinSettingsGear();
            }
        }

        // plays the release half of the native animation (Pressed to PointerOver, the turn) without the press half
        // (the tilt); AnimatedIcon applies a new State only on its next layout pass, so the second state waits for
        // that pass, set in one go both collapse into Normal to PointerOver, which does not move
        private void SpinSettingsGear()
        {
            AnimatedIcon.SetState(TaskbarSettingsIcon, "Pressed");
            TaskbarSettingsIcon.LayoutUpdated -= OnSettingsGearPressedApplied;
            TaskbarSettingsIcon.LayoutUpdated += OnSettingsGearPressedApplied;
        }

        private void OnSettingsGearPressedApplied(object? sender, object e)
        {
            TaskbarSettingsIcon.LayoutUpdated -= OnSettingsGearPressedApplied;
            AnimatedIcon.SetState(TaskbarSettingsIcon, "PointerOver");
        }

        private void TaskbarSettings_Click(object sender, RoutedEventArgs e)
        {
            HideFlyout();

            if (MainWindow.CurrentInstance != null)
            {
                MainWindow.CurrentInstance.OpenTaskbarSettings();
            }
            else
            {
                var newMainWindow = new MainWindow();
                newMainWindow.Activate();
                newMainWindow.OpenTaskbarSettings();
            }
        }

        // the snapshot; the flyout and the taskbar graphs stand still together
        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SetFlyoutPaused(!ViewModel.IsFlyoutPaused);
            ApplyPauseState();
        }

        // glyph, tooltip and name follow the state, like the csv logger pause button
        private void ApplyPauseState()
        {
            string label = AppStrings.Get(ViewModel.IsFlyoutPaused ? "Common_Resume" : "Common_Pause");
            PauseButtonIcon.Glyph = ViewModel.IsFlyoutPaused ? "\uE768" : "\uE769";
            ToolTipService.SetToolTip(PauseButton, label);
            AutomationProperties.SetName(PauseButton, label);
        }

        // [Ctrl]+[Alt]+[S]: a bordered key per part, a plain plus between them
        private void ApplyShortcutHint()
        {
            var keys = FlyoutShortcutRegistration.RegisteredKeys;
            ShortcutHintPanel.Children.Clear();
            ShortcutHintPanel.Visibility = keys != null ? Visibility.Visible : Visibility.Collapsed;
            if (keys == null) return;

            var borderStyle = (Style)ShortcutHintPanel.Resources["ShortcutKeyBorderStyle"];
            var textStyle = (Style)ShortcutHintPanel.Resources["ShortcutKeyTextStyle"];
            var plusStyle = (Style)ShortcutHintPanel.Resources["ShortcutKeyPlusTextStyle"];

            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0) ShortcutHintPanel.Children.Add(new TextBlock { Style = plusStyle, Text = "+" });

                ShortcutHintPanel.Children.Add(new Border
                {
                    Style = borderStyle,
                    Child = new TextBlock { Style = textStyle, Text = keys[i] }
                });
            }
        }

        private void OnShortcutRegistrationChanged()
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                ApplyShortcutHint();
            });
        }

        private void OnTaskbarTimeRangePicked(DependencyObject sender, DependencyProperty dp)
        {
            SettingsService.Instance.TaskbarGraphTimeSpanSeconds = TaskbarTimeRangePicker.SelectedSeconds;
        }

        private void OnFlyoutTimeRangePicked(DependencyObject sender, DependencyProperty dp)
        {
            SettingsService.Instance.TaskbarFlyoutGraphTimeSpanSeconds = FlyoutTimeRangePicker.SelectedSeconds;
        }

        // --- memory leak: TaskbarFlyoutWindow never released after close ---
        // problem: WinUI 3 never releases a closed secondary Window (confirmed, still open, even with empty content):
        // https://github.com/microsoft/microsoft-ui-xaml/issues/9063
        // fix: hide instead of closing and keep the instance as _retainedInstance (same as
        // WidgetWindow and TaskbarWidgetWindow)
        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            args.Cancel = true;
            HideFlyout();
            CurrentInstance = null;
            _retainedInstance = this;
        }


        // === theme and backdrop ===

        // an in-app theme switch is a plain repaint through ApplyTheme, never a rebuild (that would tear down
        // the live instance mid-switch)
        private void OnThemeChanged(string newTheme)
        {
            this.DispatcherQueue.TryEnqueue(() => ApplyTheme(newTheme));
        }

        private void OnBackgroundMaterialChanged(BackdropMaterial material)
        {
            this.DispatcherQueue.TryEnqueue(() => SetBackdrop(material));
        }

        private void OnOpacityChanged(float tintOpacity, float luminosityOpacity)
        {
            this.DispatcherQueue.TryEnqueue(() => UpdateAcrylicProperties());
        }

        private void OnTintColorChanged(bool useAccentColor, Windows.UI.Color customColor)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                UpdateAcrylicProperties();
                UpdateSolidBackground();
            });
        }

        // re-anchors for the next open (the flyout is usually hidden when the setting changes)
        private void OnFlyoutAlignmentChanged(string newAlignment)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (TaskbarWidgetWindow.CurrentInstance == null) return;
                PositionNextToTaskbar(TaskbarWidgetWindow.CurrentInstance);
            });
        }

        // a new slot height resizes an open flyout in place
        private void OnFlyoutGraphHeightChanged(double newHeightDip)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (TaskbarWidgetWindow.CurrentInstance == null) return;
                PositionNextToTaskbar(TaskbarWidgetWindow.CurrentInstance);
            });
        }

        // also on a taskbar move, the active edge has its own range
        private void OnTaskbarGraphTimeSpanChanged(double newTimeSpanSeconds)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                TaskbarTimeRangePicker.SelectedSeconds = newTimeSpanSeconds;
            });
        }

        private void OnFlyoutGraphTimeSpanChanged(double newTimeSpanSeconds)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                FlyoutTimeRangePicker.SelectedSeconds = newTimeSpanSeconds;
            });
        }

        private void ApplyTheme(string themeTag)
        {
            var elementTheme = themeTag switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = elementTheme;
            }

            if (FlyoutRootBorder != null)
            {
                FlyoutRootBorder.RequestedTheme = elementTheme;
            }

            SetConfigurationSourceTheme();
            UpdateAcrylicProperties();
            UpdateSolidBackground();

            if (_appWindow != null && _appWindow.TitleBar != null)
            {
                _appWindow.TitleBar.PreferredTheme = themeTag switch
                {
                    "Light" => Microsoft.UI.Windowing.TitleBarTheme.Light,
                    "Dark" => Microsoft.UI.Windowing.TitleBarTheme.Dark,
                    _ => Microsoft.UI.Windowing.TitleBarTheme.UseDefaultAppMode
                };
            }
        }

        private bool IsCurrentThemeLight()
        {
            string themeTag = SettingsService.Instance.AppTheme;
            if (themeTag == "Light") return true;
            if (themeTag == "Dark") return false;

            try
            {
                return this.Content is FrameworkElement fe && fe.ActualTheme == ElementTheme.Light;
            }
            catch
            {
                return false;
            }
        }

        // the preset of the theme for system acrylic, the users values for custom acrylic
        private void UpdateAcrylicProperties()
        {
            if (_acrylicController == null) return;

            var settings = SettingsService.Instance;
            BackdropMaterials.Configure(
                _acrylicController,
                settings.TaskbarBackgroundMaterial,
                IsCurrentThemeLight(),
                BackdropMaterials.ResolveTintColor(settings.TaskbarUseAccentColor, settings.TaskbarCustomTintColor),
                settings.TaskbarTintOpacity,
                settings.TaskbarLuminosityOpacity);
        }

        // paints every flyout surface for the current backdrop mode, three exclusive cases in this order:
        // 1. a backdrop controller is attached; root and bar transparent, the graphs area keeps its translucent lift
        // 2. Solid, or Custom Acrylic with Windows transparency off; the root takes the users accent or custom color,
        //    the graphs area the same lift
        // 3. otherwise (Mica or System Acrylic with Windows transparency off); every surface its own flat opaque color,
        //    no overlay, so each one can be calibrated on its own
        // colors come from the App.xaml theme dictionary; (the XAML {ThemeResource} is only the first paint, a local
        // value from here outranks it)
        private void UpdateSolidBackground()
        {
            if (FlyoutRootBorder == null) return;

            bool isLight = IsCurrentThemeLight();
            var themeDictionary = (ResourceDictionary)Application.Current.Resources
                .ThemeDictionaries[isLight ? "Light" : "Default"];

            var transparent = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            var graphsOverlay = (Microsoft.UI.Xaml.Media.Brush)themeDictionary["FlyoutGraphsBackground"];

            bool onGlass = _acrylicController != null || _micaController != null;
            var material = SettingsService.Instance.TaskbarBackgroundMaterial;

            if (onGlass)
            {
                FlyoutRootBorder.Background = transparent;
                FlyoutBottomBarBorder.Background = transparent;
                GraphsContentGrid.Background = graphsOverlay;
            }
            else if (material is BackdropMaterial.Solid or BackdropMaterial.CustomAcrylic)
            {
                Windows.UI.Color targetColor = BackdropMaterials.ResolveTintColor(
                    SettingsService.Instance.TaskbarUseAccentColor, SettingsService.Instance.TaskbarCustomTintColor);

                FlyoutRootBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(targetColor);
                FlyoutBottomBarBorder.Background = transparent;
                GraphsContentGrid.Background = graphsOverlay;
            }
            else
            {
                FlyoutRootBorder.Background = (Microsoft.UI.Xaml.Media.Brush)themeDictionary["FlyoutWindowBackground"];
                FlyoutBottomBarBorder.Background = (Microsoft.UI.Xaml.Media.Brush)themeDictionary["FlyoutBottomBarBackground"];
                GraphsContentGrid.Background = transparent;
            }

            // both strokes are translucent: the window stroke the same in every mode, the separator with its own
            // alpha on glass, tuned against the material instead of the opaque surfaces
            FlyoutRootBorder.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)themeDictionary["FlyoutWindowBorderBrush"];
            FlyoutBottomBarBorder.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)themeDictionary[
                onGlass ? "FlyoutBottomBarSeparatorOnGlassBrush" : "FlyoutBottomBarSeparatorBrush"];

            // the grain belongs to the system acrylic look, so it shows there only (a controller means Windows
            // transparency is on)
            Grain.Show(_acrylicController != null && material == BackdropMaterial.SystemAcrylic,
                FlyoutRootBorder.ActualWidth, FlyoutRootBorder.ActualHeight);

            // the shadow opacities follow the theme
            RenderCardShadow();
        }

        // for a pure OS accent change; both calls resolve the accent fresh, no rebuild needed
        private void RefreshAccentSurfaces()
        {
            UpdateAcrylicProperties();
            UpdateSolidBackground();
        }

        // === card shadow ===

        // the Windows shell shadow (Quick Settings, which Battery Flyout matches to one level), fitted over every
        // margin pixel of a 1022 px card at 175 percent: three layers composed as 1 - (1 - a1)(1 - a2)(1 - a3), each
        // a box around the card with Gaussian blurred edges; same geometry in both themes, light about half as
        // strong; only with Windows transparency on, the shell draws none without it
        // the third layer fades the sides out toward the top, over a stretch that grows with the card height (Quick
        // Settings, 678 px, reaches the full side at 280 px, the 1022 px card at 380 px), so its top edge scales
        private const double ShadowFitScale = 1.75;
        private const double ShadowFitCardHeightPx = 1022;
        private static readonly ShadowLayer[] CardShadowLayers =
        [
            // tight
            new(SigmaXPx: 5.91, SpreadXPx: -5.76, TopPx: 8.80, TopSigmaPx: 5.91, BottomPx: -2.72, BottomSigmaPx: 5.91,
                TopScalesWithHeight: false, DarkOpacity: 0.685, LightOpacity: 0.277),
            // wide, further down
            new(SigmaXPx: 13.08, SpreadXPx: -9.60, TopPx: 30.32, TopSigmaPx: 13.08, BottomPx: 11.12, BottomSigmaPx: 13.08,
                TopScalesWithHeight: false, DarkOpacity: 0.314, LightOpacity: 0.151),
            // side ramp
            new(SigmaXPx: 10.35, SpreadXPx: 1.25, TopPx: 185.16, TopSigmaPx: 78.09, BottomPx: 8.40, BottomSigmaPx: 14.94,
                TopScalesWithHeight: true, DarkOpacity: 0.150, LightOpacity: 0.080),
        ];

        // one layer, in px at ShadowFitScale: the card rect grown sideways by SpreadXPx, its top edge moved down by
        // TopPx and its bottom edge by BottomPx, each edge blurred with its own sigma
        private readonly record struct ShadowLayer(
            double SigmaXPx, double SpreadXPx, double TopPx, double TopSigmaPx, double BottomPx, double BottomSigmaPx,
            bool TopScalesWithHeight, double DarkOpacity, double LightOpacity);

        // geometry from PositionNextToTaskbar, in physical px; the card rect is window local
        private SizeInt32 _shadowWindowSizePx;
        private RectInt32 _shadowCardRectPx;
        private double _shadowScale;

        // what the current bitmap was rendered for, so an unchanged open does not render again
        private (SizeInt32 Window, RectInt32 Card, double Scale, bool IsLight) _shadowRenderedFor;

        private void RenderCardShadow()
        {
            if (FlyoutShadowImage == null) return;

            bool isTransparencyEnabled = _uiSettings != null && _uiSettings.AdvancedEffectsEnabled;
            bool show = UsesCardWindow && isTransparencyEnabled && _shadowWindowSizePx.Width > 0 && _shadowScale > 0;
            FlyoutShadowImage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;

            bool isLight = IsCurrentThemeLight();
            var renderFor = (_shadowWindowSizePx, _shadowCardRectPx, _shadowScale, isLight);
            if (FlyoutShadowImage.Source != null && renderFor == _shadowRenderedFor) return;

            int width = _shadowWindowSizePx.Width;
            int height = _shadowWindowSizePx.Height;
            var card = _shadowCardRectPx;
            double fitToWindow = _shadowScale / ShadowFitScale;

            // transmission per pixel, multiplied up layer by layer; a blurred rect is separable, so each layer is one
            // profile across and one down
            var transmission = new double[width * height];
            Array.Fill(transmission, 1.0);

            double heightScale = card.Height / (ShadowFitCardHeightPx * fitToWindow);

            foreach (var layer in CardShadowLayers)
            {
                double topScale = layer.TopScalesWithHeight ? fitToWindow * heightScale : fitToWindow;
                double spreadX = layer.SpreadXPx * fitToWindow;
                double sigmaX = layer.SigmaXPx * fitToWindow;
                double opacity = isLight ? layer.LightOpacity : layer.DarkOpacity;

                var across = BlurredEdgeProfile(width,
                    card.X - spreadX, sigmaX,
                    card.X + card.Width + spreadX, sigmaX);
                var down = BlurredEdgeProfile(height,
                    card.Y + (layer.TopPx * topScale), layer.TopSigmaPx * topScale,
                    card.Y + card.Height + (layer.BottomPx * fitToWindow), layer.BottomSigmaPx * fitToWindow);

                for (int y = 0; y < height; y++)
                {
                    double row = opacity * down[y];
                    if (row <= 0) continue;

                    int offset = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        transmission[offset + x] *= 1.0 - (row * across[x]);
                    }
                }
            }

            // black with alpha, premultiplied; cut out under the material, inside the card stroke (the stroke lies
            // over the shadow, as on the native flyouts), antialiased along the rounded edge
            double inset = CardStrokePx(_shadowScale);
            double radius = (CardCornerRadiusDip * _shadowScale) - inset;
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double alpha = 1.0 - transmission[(y * width) + x];
                    alpha *= 1.0 - RoundedRectCoverage(x + 0.5, y + 0.5, card, inset, radius);
                    pixels[(((y * width) + x) * 4) + 3] = (byte)Math.Round(Math.Clamp(alpha, 0.0, 1.0) * 255.0);
                }
            }

            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap(width, height);
            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Write(pixels, 0, pixels.Length);
            }
            bitmap.Invalidate();

            // one bitmap pixel per physical pixel
            FlyoutShadowImage.Width = width / _shadowScale;
            FlyoutShadowImage.Height = height / _shadowScale;
            FlyoutShadowImage.Source = bitmap;
            _shadowRenderedFor = renderFor;
        }

        // a box from start to end, each edge blurred with its own Gaussian, sampled at the pixel centers
        private static double[] BlurredEdgeProfile(int length, double start, double startSigma, double end, double endSigma)
        {
            var profile = new double[length];
            for (int i = 0; i < length; i++)
            {
                double center = i + 0.5;
                profile[i] = Math.Max(0.0, NormalCdf((center - start) / startSigma) - NormalCdf((center - end) / endSigma));
            }
            return profile;
        }

        // how much of the pixel at the point the rect, shrunk by inset, covers, from the signed distance to its
        // rounded outline
        private static double RoundedRectCoverage(double px, double py, RectInt32 rect, double inset, double radius)
        {
            double centerX = rect.X + (rect.Width / 2.0);
            double centerY = rect.Y + (rect.Height / 2.0);
            double halfWidth = (rect.Width / 2.0) - inset;
            double halfHeight = (rect.Height / 2.0) - inset;
            double qx = Math.Abs(px - centerX) - (halfWidth - radius);
            double qy = Math.Abs(py - centerY) - (halfHeight - radius);
            double outside = Math.Sqrt(Math.Pow(Math.Max(qx, 0), 2) + Math.Pow(Math.Max(qy, 0), 2));
            double distance = outside + Math.Min(Math.Max(qx, qy), 0) - radius;
            return Math.Clamp(0.5 - distance, 0.0, 1.0);
        }

        // standard normal cumulative distribution; Abramowitz and Stegun 7.1.26 for erf, error below 1.5e-7
        private static double NormalCdf(double z)
        {
            double x = Math.Abs(z) / Math.Sqrt(2.0);
            double t = 1.0 / (1.0 + (0.3275911 * x));
            double polynomial = t * (0.254829592 + (t * (-0.284496736 + (t * (1.421413741 + (t * (-1.453152027 + (t * 1.061405429))))))));
            double erf = 1.0 - (polynomial * Math.Exp(-x * x));
            return z >= 0 ? 0.5 * (1.0 + erf) : 0.5 * (1.0 - erf);
        }


        // === acrylic grain ===

        // created on first use; the first SetBackdrop runs from the constructor
        private AcrylicGrain? _grain;
        private AcrylicGrain Grain => _grain ??= new AcrylicGrain(FlyoutNoiseHost, FlyoutNoiseOverlay, FlyoutRootBorder);

        private void FlyoutRootBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!Grain.IsVisible) return;

            Grain.Ensure(e.NewSize.Width, e.NewSize.Height);
        }

        // applies the backdrop for the material and the Windows transparency state; without transparency no controller,
        // UpdateSolidBackground paints the flat surfaces instead
        // Mica only samples the wallpaper and shows next to nothing on a small flyout; System Acrylic is the look of
        // the Windows flyouts
        public void SetBackdrop(BackdropMaterial material)
        {
            DispatcherQueue.EnsureSystemDispatcherQueue();

            bool isTransparencyEnabled = _uiSettings != null && _uiSettings.AdvancedEffectsEnabled;

            if (_configurationSource == null)
            {
                _configurationSource = new SystemBackdropConfiguration();
                this.Activated += (s, e) => { if (_configurationSource != null) _configurationSource.IsInputActive = true; };
                _configurationSource.IsInputActive = true;
            }

            SetConfigurationSourceTheme();

            _acrylicController?.Dispose();
            _acrylicController = null;
            _micaController?.Dispose();
            _micaController = null;
            this.SystemBackdrop = null;

            if (isTransparencyEnabled && BackdropMaterials.IsAcrylic(material) && DesktopAcrylicController.IsSupported())
            {
                _acrylicController = new DesktopAcrylicController();
                // Base is the variant the Windows 11 shell surfaces use:
                // https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.systembackdrops.desktopacrylickind
                _acrylicController.Kind = DesktopAcrylicKind.Base;
                AddBackdropTarget(_acrylicController);
                _acrylicController.SetSystemBackdropConfiguration(_configurationSource);

                UpdateAcrylicProperties();
            }
            else if (isTransparencyEnabled && material == BackdropMaterial.Mica && MicaController.IsSupported())
            {
                _micaController = new MicaController();
                AddBackdropTarget(_micaController);
                _micaController.SetSystemBackdropConfiguration(_configurationSource);
            }

            // the card window stays transparent under the link material, the window level one only without a
            // controller
            if (UsesCardWindow || (_acrylicController == null && _micaController == null))
            {
                this.SystemBackdrop = new TransparentTintBackdrop();
            }

            // the base color, read off the controller set above
            UpdateSolidBackground();

            // a retired link switches the construction, which moves the window around the card
            if (_isAnchored && _isCardWindow != UsesCardWindow && TaskbarWidgetWindow.CurrentInstance != null)
            {
                PositionNextToTaskbar(TaskbarWidgetWindow.CurrentInstance);
            }
            else
            {
                UpdateShadowPolicy();
            }
        }

        // the link when there is one, the window otherwise; a link the controller refuses is retired, so the flyout
        // falls back to the window level construction for good (SetBackdrop then re-places the window)
        private void AddBackdropTarget(ISystemBackdropControllerWithTargets controller)
        {
            if (_backdropLink != null)
            {
                try
                {
                    controller.AddSystemBackdropTarget(_backdropLink.Target);
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BackdropLink] target refused: 0x{ex.HResult:X8} {ex.Message}");
                    RetireBackdropLink();
                }
            }

            controller.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
        }

        private void SetConfigurationSourceTheme()
        {
            if (_configurationSource == null) return;

            _configurationSource.Theme = IsCurrentThemeLight()
                ? SystemBackdropTheme.Light
                : SystemBackdropTheme.Dark;
        }
    }
}
