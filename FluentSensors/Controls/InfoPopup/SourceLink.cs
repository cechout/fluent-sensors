using System;


namespace FluentSensors.Controls.InfoPopup
{
    // one clickable entry in InfoPopupControl.SourceLinks
    // Url is typed as Uri (not string) so it binds straight into HyperlinkButton.NavigateUri with no converter
    public class SourceLink
    {
        public string Label { get; set; }
        public Uri Url { get; set; }
    }
}
