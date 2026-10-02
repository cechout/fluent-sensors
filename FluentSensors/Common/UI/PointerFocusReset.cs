using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;


namespace FluentSensors.Common.UI
{
    // the pointer focus reset:
    // hides the focus rectangle on mouse use like Windows; a click on empty space leaves keyboard focus, so this turns
    // it into pointer focus, the next key carries on from there
    public static class PointerFocusReset
    {
        // once per window or dialog, on its content root
        public static void Attach(UIElement root)
        {
            // handledEventsToo; a press a control already handled still counts
            root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Root_PointerPressed), true);
        }

        // last on the way up, after a control took focus on press
        private static void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not UIElement root || root.XamlRoot == null) return;

            if (FocusManager.GetFocusedElement(root.XamlRoot) is Control focused
                && focused.FocusState != FocusState.Pointer
                && focused.FocusState != FocusState.Unfocused)
            {
                focused.Focus(FocusState.Pointer);
            }
        }
    }
}
