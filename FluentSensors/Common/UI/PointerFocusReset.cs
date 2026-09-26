using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;


namespace FluentSensors.Common.UI
{
    // hides the keyboard focus rectangle again as soon as the mouse is used, the way Windows itself does it
    //
    // a click on something focusable moves focus there anyway, but a click on empty space leaves focus where it was,
    // still in keyboard state, so the rectangle would stay up; this drops that element to pointer focus instead, which
    // hides the rectangle but keeps the spot, so the next tab or arrow key carries on from the same element
    public static class PointerFocusReset
    {
        // call once per window or dialog, on the root of its content
        public static void Attach(UIElement root)
        {
            // handledEventsToo, a press that a control on the way has already handled still counts as a click
            root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Root_PointerPressed), true);
        }

        // runs last on the way up, so a control that takes focus on press has already done so and reads Pointer here
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
