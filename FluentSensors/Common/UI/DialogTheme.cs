using Microsoft.UI.Xaml;


namespace FluentSensors.Common.UI
{
    // the theme a ContentDialog has to be told about, because it cannot inherit it
    //
    // a dialog is hosted in the popup root of its XamlRoot, a sibling of the window content rather than a child of
    // it, so the RequestedTheme the app writes onto that content (see MainWindow.ApplyTheme) never reaches a
    // dialog; left alone it resolves against the application theme, which is fixed at process start, and the
    // dialog then keeps the theme the app launched in no matter what the user switches to afterwards
    //
    // ActualTheme rather than RequestedTheme, so a window still on Default hands over the theme it actually shows
    public static class DialogTheme
    {
        public static ElementTheme For(XamlRoot? xamlRoot) =>
            xamlRoot?.Content is FrameworkElement root ? root.ActualTheme : ElementTheme.Default;
    }
}
