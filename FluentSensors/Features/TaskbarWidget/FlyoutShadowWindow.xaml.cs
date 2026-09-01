using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinUIEx;
using WinUIEx.Messaging;


namespace FluentSensors.Features.TaskbarWidget
{
    // transparent window that draws nothing but the flyout drop shadow, sitting directly behind TaskbarFlyoutWindow
    //
    // DWM offers no parameters for its own window shadow, so the flyout switches it off and this window paints one
    // that has knobs; a painted shadow needs transparent space outside the visible body, and that space cannot be
    // part of the flyout window:
    // 1. the acrylic there is a window-level system backdrop, so it fills the whole window rect and cannot be inset
    // 2. the only clip that reaches such a backdrop is SetWindowRgn, which is exactly what rounds the flyout today
    // 3. a window region clips everything the window presents, composition visuals included
    // 4. so a region tight enough to keep glass out of the margin erases the shadow from it in the same step
    //
    // the flyout window therefore stays exactly the body and this window carries the margin
    internal sealed partial class FlyoutShadowWindow : Window
    {
        // === shadow knobs ===

        // the margin has to stay wider than blur plus vertical offset, otherwise the shadow is cut off at the edge
        public const double ShadowMarginDip = 16;
        public const float ShadowBlurRadius = 14f; // spread of the shadow
        public const float ShadowOpacity = 0.30f; // strength of the shadow
        public static readonly Vector3 ShadowOffset = new Vector3(0, 5, 0); // shifts the shadow down


        // === win32 api imports ===

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
        private static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
        private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, uint cbAttribute);

        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int GCL_STYLE = -26;
        private const int CS_DROPSHADOW = 0x00020000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        private const uint DWMWA_NCRENDERING_POLICY = 2;
        private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const uint DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
        private const int DWMWCP_DONOTROUND = 1;
        private const int DWMNCRP_DISABLED = 1;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_HIDEWINDOW = 0x0080;

        // parked far off screen for the one activation the content island needs, see the constructor
        private const int ParkedOffset = -32000;


        private readonly AppWindow _appWindow;
        private readonly IntPtr _hwnd;
        private readonly FlyoutDropShadow _dropShadow;

        private WindowMessageMonitor? _messageMonitor;
        private bool _isClosed;

        internal IntPtr Hwnd => _hwnd;


        internal FlyoutShadowWindow()
        {
            this.InitializeComponent();

            _appWindow = this.AppWindow;
            _appWindow.IsShownInSwitchers = false;
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            var presenter = OverlappedPresenter.Create();
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            _appWindow.SetPresenter(presenter);

            int style = GetWindowLong(_hwnd, GWL_STYLE);
            SetWindowLong(_hwnd, GWL_STYLE, (style | WS_POPUP) & ~WS_THICKFRAME & ~WS_CAPTION);

            // NOACTIVATE keeps the focus on the flyout, which light dismisses the moment it loses it; TRANSPARENT
            // lets every click fall through the halo to whatever sits behind it
            int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);

            // strip CS_DROPSHADOW class style
            try
            {
                IntPtr classStyle = GetClassLongPtr(_hwnd, GCL_STYLE);
                SetClassLongPtr(_hwnd, GCL_STYLE, new IntPtr(classStyle.ToInt64() & ~CS_DROPSHADOW));
            }
            catch { }

            // hook win32 messages:
            // 1. WM_NCCALCSIZE (0x0083): eliminates the non-client stripe DWM would otherwise paint white around
            //    the whole window, the same one TaskbarFlyoutWindow removes; without it the halo is not transparent
            //    and shows through the rounded flyout corners in front of it
            _messageMonitor = new WindowMessageMonitor(_hwnd);
            _messageMonitor.WindowMessageReceived += OnWindowMessageReceived;

            // this window carries no material, so there is nothing here for DWM to round, and nothing it should
            // shade either, the ring is the only thing drawn
            int cornerPreference = DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

            int policy = DWMNCRP_DISABLED;
            DwmSetWindowAttribute(_hwnd, DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));

            int borderColor = DWMWA_COLOR_NONE;
            DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));

            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            this.SystemBackdrop = new TransparentTintBackdrop();

            _dropShadow = new FlyoutDropShadow(ShadowHost);

            // a WinUI window does not build its content island until it has been activated once, and this one is
            // never activated again afterwards, it is only shown and hidden through SetWindowPos; parking it off
            // screen for that one activation keeps it from flashing anywhere visible
            _appWindow.Move(new PointInt32(ParkedOffset, ParkedOffset));
            this.Activate();
            SetVisible(false);
        }


        private void OnWindowMessageReceived(object? sender, WindowMessageEventArgs e)
        {
            if (e.Message.MessageId == 0x0083) // WM_NCCALCSIZE
            {
                if (e.Message.WParam != 0)
                {
                    // 0 = client area covers 100% of the window rectangle, leaving no frame for DWM to paint
                    e.Result = IntPtr.Zero;
                    e.Handled = true;
                }
            }
        }


        // takes the flyout body rect in physical pixels and wraps itself around it by the shadow margin
        internal void UpdateGeometry(int bodyX, int bodyY, int bodyWidth, int bodyHeight, double scale, float cornerRadius)
        {
            if (_isClosed || bodyWidth <= 0 || bodyHeight <= 0 || scale <= 0) return;

            int margin = (int)Math.Round(ShadowMarginDip * scale);

            _appWindow.MoveAndResize(new RectInt32(
                bodyX - margin,
                bodyY - margin,
                bodyWidth + (2 * margin),
                bodyHeight + (2 * margin)));

            // the visuals live in XAML space, so the body goes in as dips while the window rect above is pixels
            _dropShadow.Update(
                (float)ShadowMarginDip,
                new Vector2((float)(bodyWidth / scale), (float)(bodyHeight / scale)),
                cornerRadius,
                ShadowBlurRadius,
                ShadowOpacity,
                ShadowOffset);
        }

        // never goes through Activate, this window must not take the focus away from the flyout
        internal void SetVisible(bool visible)
        {
            if (_isClosed) return;

            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | (visible ? SWP_SHOWWINDOW : SWP_HIDEWINDOW));
        }

        // slots this window directly below hwndInsertAfter in z-order
        internal void PlaceAfter(IntPtr hwndInsertAfter)
        {
            if (_isClosed || hwndInsertAfter == IntPtr.Zero) return;

            SetWindowPos(_hwnd, hwndInsertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        // same retain-and-destroy trade as TaskbarFlyoutWindow.SafeDestroy, this window is torn down with it
        internal void SafeDestroy()
        {
            if (_isClosed) return;
            _isClosed = true;

            try
            {
                _messageMonitor?.Dispose();
                _messageMonitor = null;
                this.Close();
            }
            catch { }
        }
    }
}
