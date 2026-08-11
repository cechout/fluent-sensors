namespace FluentSensors.Controls.InfoPopup
{
    // where an InfoPopupControl opens, and which of its own elements (title or button) it anchors to
    public enum PopupPlacementMode
    {
        // anchors on the title, not the button; opens to the left and flips upward when there is not enough room
        // below
        // Used for the Performance page info panel headers, where the title can sit anywhere along the
        // right edge of the window
        TitleAnchored,

        // anchors on the button; fixed direction, no flip, no collision handling
        Above,
        Below,
        Left,
        Right
    }
}