using System;


namespace FluentSensors.Controls.InfoPopup
{
    // one InfoPopupControl.SourceLinks entry; a Uri binds to HyperlinkButton.NavigateUri without a converter
    public class SourceLink
    {
        public string Label { get; set; }
        public Uri Url { get; set; }
    }
}
