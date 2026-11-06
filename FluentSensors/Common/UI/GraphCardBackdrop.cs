using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FluentSensors.Common.UI
{
    // Taskbar graphs never take focus; keep the material active without changing window activation.
    public class GraphCardBackdrop : SystemBackdrop
    {
        public GraphCardMaterial Material { get; private set; }
        private ICompositionSupportsSystemBackdrop? _target;
        private DesktopAcrylicController? _acrylic;
        private MicaController? _mica;
        private SystemBackdropConfiguration? _configuration;

        public GraphCardBackdrop(GraphCardMaterial material)
        {
            Material = material;
        }

        public static bool IsSupported(GraphCardMaterial material) => material switch
        {
            GraphCardMaterial.Acrylic => DesktopAcrylicController.IsSupported(),
            GraphCardMaterial.Mica => MicaController.IsSupported(),
            _ => false
        };

        protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
        {
            base.OnTargetConnected(target, xamlRoot);
            _configuration = new SystemBackdropConfiguration { IsInputActive = true };
            UpdateConfiguration(target, xamlRoot);
            _target = target;
            SetMaterial(Material);
        }

        public void SetMaterial(GraphCardMaterial material)
        {
            if (Material == material && (_acrylic is not null || _mica is not null)) return;
            ReleaseControllers();
            Material = material;
            if (_target is null || _configuration is null) return;
            if (Material == GraphCardMaterial.Acrylic && DesktopAcrylicController.IsSupported())
            {
                _acrylic = new DesktopAcrylicController();
                _acrylic.SetSystemBackdropConfiguration(_configuration);
                _acrylic.AddSystemBackdropTarget(_target);
            }
            else if (Material == GraphCardMaterial.Mica && MicaController.IsSupported())
            {
                _mica = new MicaController();
                _mica.SetSystemBackdropConfiguration(_configuration);
                _mica.AddSystemBackdropTarget(_target);
            }
        }

        protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
        {
            base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
            UpdateConfiguration(target, xamlRoot);
        }

        private void UpdateConfiguration(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
        {
            if (_configuration is null) return;
            var defaults = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
            _configuration.Theme = defaults.Theme;
            _configuration.IsHighContrast = defaults.IsHighContrast;
        }

        protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
        {
            ReleaseControllers();
            _target = null;
            _configuration = null;
            base.OnTargetDisconnected(target);
        }

        private void ReleaseControllers()
        {
            if (_target is not null)
            {
                _acrylic?.RemoveSystemBackdropTarget(_target);
                _mica?.RemoveSystemBackdropTarget(_target);
            }
            _acrylic?.Dispose();
            _acrylic = null;
            _mica?.Dispose();
            _mica = null;
        }
    }
}
