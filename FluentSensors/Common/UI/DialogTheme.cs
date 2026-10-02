using Microsoft.UI.Xaml;


namespace FluentSensors.Common.UI
{
    // the dialog theme:
    // a ContentDialog lives in the popup root beside the window content, so MainWindow.ApplyTheme never reaches it
    // and it keeps the launch theme
    // ActualTheme, so a window on Default hands over the theme it shows
    public static class DialogTheme
    {
        public static ElementTheme For(XamlRoot? xamlRoot) =>
            xamlRoot?.Content is FrameworkElement root ? root.ActualTheme : ElementTheme.Default;
    }
}
