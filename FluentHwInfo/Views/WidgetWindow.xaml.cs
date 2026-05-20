using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;

namespace FluentHwInfo.Views
{
    public sealed partial class WidgetWindow : Window
    {
        private AppWindow _appWindow;

        public WidgetWindow()
        {
            this.InitializeComponent();

            // 1. AppWindow Instanz holen
            _appWindow = this.AppWindow;

            // 2. Inhalt in die Titlebar schieben
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar); // Definieren, wo man das Fenster greifen kann

            // 3. Compact Overlay Modus aktivieren (Always on Top + kein Standard-Rahmen)
            _appWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay);

            // 4. Positionierung: Oben Rechts anheften
            PositionWidgetTopRight();
        }

        private void PositionWidgetTopRight()
        {
            // Wir holen uns die Größe des primären Bildschirms
            var displayArea = DisplayArea.Primary;
            int screenWidth = displayArea.WorkArea.Width;

            int widgetWidth = 700;
            int widgetHeight = 300;

            // Verschieben des Fensters an den rechten Rand (mit 20px Abstand)
            _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                screenWidth - widgetWidth - 20,
                20,
                widgetWidth,
                widgetHeight));
        }

        private void BackToDashboard_Click(object sender, RoutedEventArgs e)
        {
            // Hier würdest du dein MainWindow wieder aufrufen/anzeigen
            // App.MainWindow.Activate();
            this.Close();
        }
    }
}