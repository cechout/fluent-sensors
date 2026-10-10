using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT;


namespace FluentSensors.Common.UI
{
    // one settings group of a window backdrop; the widget and the csv logger window each have their own
    public readonly record struct WindowBackdropSettings(
        BackdropMaterial Material, float TintOpacity, float LuminosityOpacity, bool UseAccentColor, Color CustomTintColor);

    // the background material of the widget and the csv logger window:
    // the material of the windows settings group on the whole window, the root panel painted to match; the taskbar
    // flyout paints its card on its own and only shares BackdropMaterials
    public sealed class WindowBackdrop
    {
        // === fields ===

        private readonly Window _window;
        private readonly Panel _rootPanel;
        private readonly Func<WindowBackdropSettings> _readSettings;
        private readonly AcrylicGrain _grain;

        private DesktopAcrylicController? _acrylicController;
        private MicaController? _micaController;
        private SystemBackdropConfiguration? _configurationSource;
        private bool _isDisposed;


        // === constructor ===

        // the grain host and its rectangle sit in the root panel, under every other child
        public WindowBackdrop(Window window, Panel rootPanel, Func<WindowBackdropSettings> readSettings,
            UIElement grainHost, Rectangle grainOverlay)
        {
            _window = window;
            _rootPanel = rootPanel;
            _readSettings = readSettings;
            _grain = new AcrylicGrain(grainHost, grainOverlay, rootPanel);
            _rootPanel.SizeChanged += RootPanel_SizeChanged;
        }


        // === public methods ===

        // applies the material, per the Microsoft guide:
        // https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops
        public void Apply(BackdropMaterial material)
        {
            if (_isDisposed) return;

            _window.DispatcherQueue.EnsureSystemDispatcherQueue();

            if (_configurationSource == null)
            {
                _configurationSource = new SystemBackdropConfiguration();
                _window.Activated += Window_Activated;
                ((FrameworkElement)_window.Content).ActualThemeChanged += Content_ActualThemeChanged;

                _configurationSource.IsInputActive = true;
                SetConfigurationSourceTheme();
            }

            DisposeControllers();

            if (BackdropMaterials.IsAcrylic(material) && DesktopAcrylicController.IsSupported())
            {
                _acrylicController = new DesktopAcrylicController();

                // Base is the variant the Windows 11 shell surfaces use, the one the system preset is measured on
                if (material == BackdropMaterial.SystemAcrylic)
                {
                    _acrylicController.Kind = DesktopAcrylicKind.Base;
                }

                _acrylicController.AddSystemBackdropTarget(_window.As<ICompositionSupportsSystemBackdrop>());
                _acrylicController.SetSystemBackdropConfiguration(_configurationSource);

                UpdateProperties();
                UpdateGlassSurface();
            }
            else if (material == BackdropMaterial.Mica && MicaController.IsSupported())
            {
                _micaController = new MicaController();
                _micaController.AddSystemBackdropTarget(_window.As<ICompositionSupportsSystemBackdrop>());
                _micaController.SetSystemBackdropConfiguration(_configurationSource);

                // transparent, so the material shows
                _rootPanel.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                UpdateGlassSurface();
            }
            else
            {
                UpdateSolidBackground();
                UpdateGlassSurface();
            }
        }

        // tint, opacities and the solid color from the settings; for a slider, a color change and a pure OS accent
        // change, which needs no rebuild (both resolve the accent fresh)
        public void Refresh()
        {
            UpdateProperties();
            UpdateSolidBackground();
        }

        // --- workaround: DWM backdrop swapchain kick ---
        // problem: after a Windows transparency or theme change, DesktopAcrylicController needs a rebind to attach its
        // blur to the new DWM swapchain
        // fix: after a rebuild, kick the backdrop once (Solid, then the current one), with parameters only
        public void KickRefresh()
        {
            if (_isDisposed) return;

            BackdropMaterial current = _readSettings().Material;
            if (current == BackdropMaterial.Solid) return;

            var timer = _window.DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(80);
            timer.IsRepeating = false;
            timer.Tick += (s, e) =>
            {
                if (_isDisposed) return;
                Apply(BackdropMaterial.Solid);
                Apply(current);
            };
            timer.Start();
        }

        // the controllers, per the Microsoft docs; the window closes for real after this
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            DisposeControllers();
            _rootPanel.SizeChanged -= RootPanel_SizeChanged;

            if (_configurationSource != null)
            {
                _window.Activated -= Window_Activated;
                if (_window.Content is FrameworkElement content)
                {
                    content.ActualThemeChanged -= Content_ActualThemeChanged;
                }
                _configurationSource = null;
            }
        }


        // === private helpers ===

        private void UpdateProperties()
        {
            if (_isDisposed || _acrylicController == null) return;

            var settings = _readSettings();
            BackdropMaterials.Configure(
                _acrylicController,
                settings.Material,
                IsLightTheme(),
                BackdropMaterials.ResolveTintColor(settings.UseAccentColor, settings.CustomTintColor),
                settings.TintOpacity,
                settings.LuminosityOpacity);
        }

        // only for the solid material
        private void UpdateSolidBackground()
        {
            if (_isDisposed) return;
            var settings = _readSettings();
            if (settings.Material != BackdropMaterial.Solid) return;

            var targetColor = BackdropMaterials.ResolveTintColor(settings.UseAccentColor, settings.CustomTintColor);
            _rootPanel.Background = new SolidColorBrush(targetColor);
        }

        // transparent, so the material shows; system acrylic with Windows transparency on takes the lift of the flyout
        // graphs area (FlyoutGraphsBackground), the surface its preset is matched to the Windows flyouts under
        // the grain belongs to the acrylic, so it shows whenever the blur does, like on the taskbar flyout
        private void UpdateGlassSurface()
        {
            if (_isDisposed) return;

            bool isTransparencyEnabled = IsTransparencyEnabled();
            _grain.Show(_acrylicController != null && isTransparencyEnabled, _rootPanel.ActualWidth, _rootPanel.ActualHeight);

            if (_acrylicController == null) return;

            if (_readSettings().Material == BackdropMaterial.SystemAcrylic && isTransparencyEnabled)
            {
                var themeDictionary = (ResourceDictionary)Application.Current.Resources
                    .ThemeDictionaries[IsLightTheme() ? "Light" : "Default"];
                _rootPanel.Background = (Brush)themeDictionary["FlyoutGraphsBackground"];
            }
            else
            {
                _rootPanel.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        // read on every apply; a change of the Windows setting rebuilds the window anyway
        private static bool IsTransparencyEnabled()
        {
            try
            {
                return new UISettings().AdvancedEffectsEnabled;
            }
            catch
            {
                return false;
            }
        }

        private void RootPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isDisposed || !_grain.IsVisible) return;

            _grain.Ensure(e.NewSize.Width, e.NewSize.Height);
        }

        private void DisposeControllers()
        {
            _acrylicController?.Dispose();
            _acrylicController = null;
            _micaController?.Dispose();
            _micaController = null;
        }

        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            // always active, or a click outside drops the blur while the window stays on screen
            if (_configurationSource != null)
            {
                _configurationSource.IsInputActive = true;
            }
        }

        // the system preset and its lift differ per theme
        private void Content_ActualThemeChanged(FrameworkElement sender, object args)
        {
            SetConfigurationSourceTheme();
            UpdateProperties();
            UpdateGlassSurface();
        }

        private void SetConfigurationSourceTheme()
        {
            if (_configurationSource != null && _window.Content is FrameworkElement frameworkElement)
            {
                _configurationSource.Theme = frameworkElement.ActualTheme switch
                {
                    ElementTheme.Dark => SystemBackdropTheme.Dark,
                    ElementTheme.Light => SystemBackdropTheme.Light,
                    _ => SystemBackdropTheme.Default
                };
            }
        }

        private bool IsLightTheme() =>
            _window.Content is FrameworkElement frameworkElement && frameworkElement.ActualTheme == ElementTheme.Light;
    }
}
