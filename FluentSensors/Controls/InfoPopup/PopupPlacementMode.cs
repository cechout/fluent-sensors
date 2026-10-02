namespace FluentSensors.Controls.InfoPopup
{
    // where an InfoPopupControl opens and whether it anchors on the title or the button
    public enum PopupPlacementMode
    {
        // on the title, to the left, flipping up without room below; for the info panel headers of the performance page
        TitleAnchored,

        // on the button, no flip, no collision handling
        Above,
        Below,
        Left,
        Right
    }
}
