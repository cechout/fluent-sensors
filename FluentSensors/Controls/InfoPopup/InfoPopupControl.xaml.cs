using FluentSensors.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;


namespace FluentSensors.Controls.InfoPopup
{
    // optional title, an info button, and a popup explaining a group of related values or a single value in more
    // detail; used both for the Performance page info panel headers and for any other place
    //
    // the popup itself (its border, background, source/description layout) is deliberately not configurable, only
    // its placement and the title/button around it are; keeps every popup in the app visually identical
    public sealed partial class InfoPopupControl : UserControl
    {
        // === Fields ===

        // pixel gap between the popup and its anchor (the title for TitleAnchored, the button otherwise)
        // horizontal - space to the side the popup opens towards
        // vertical - space above/below the anchor, direction depends on PlacementMode
        private const double PopupHorizontalGap = 10;
        private const double PopupVerticalGap = 8;

        // manual correction for PlacementMode.TitleAnchored only, adjusts vertical popup position to match the
        // title TextBlock; not used by the button-anchored placements
        private const double PopupVerticalManualAdjustment = 18;

        // InfoPopupControl_Loaded can fire more than once per instance, since DetailViews are cached and reattached to
        // the live tree on repeat navigation instead of recreated
        // Without this the second run tries to re-add InfoPopup to a host it is already a child of
        private bool _popupRelocated;


        // === Constructor ===

        public InfoPopupControl()
        {
            InitializeComponent();
        }


        // === DependencyProperties ===

        // left out entirely, including its layout space, when empty
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(InfoPopupControl),
                new PropertyMetadata(string.Empty));

        // whether the title wraps onto multiple lines or overflows on one; no effect when Title is empty
        public TextWrapping TitleTextWrapping
        {
            get => (TextWrapping)GetValue(TitleTextWrappingProperty);
            set => SetValue(TitleTextWrappingProperty, value);
        }
        public static readonly DependencyProperty TitleTextWrappingProperty =
            DependencyProperty.Register(
                nameof(TitleTextWrapping),
                typeof(TextWrapping),
                typeof(InfoPopupControl),
                new PropertyMetadata(TextWrapping.NoWrap));

        // how the title truncates when it does not fit and TitleTextWrapping is NoWrap
        public TextTrimming TitleTextTrimming
        {
            get => (TextTrimming)GetValue(TitleTextTrimmingProperty);
            set => SetValue(TitleTextTrimmingProperty, value);
        }
        public static readonly DependencyProperty TitleTextTrimmingProperty =
            DependencyProperty.Register(
                nameof(TitleTextTrimming),
                typeof(TextTrimming),
                typeof(InfoPopupControl),
                new PropertyMetadata(TextTrimming.None));

        // applied to the title TextBlock unchanged; leave unset for a plain default look
        public Style TitleStyle
        {
            get => (Style)GetValue(TitleStyleProperty);
            set => SetValue(TitleStyleProperty, value);
        }
        public static readonly DependencyProperty TitleStyleProperty =
            DependencyProperty.Register(
                nameof(TitleStyle),
                typeof(Style),
                typeof(InfoPopupControl),
                new PropertyMetadata(null));

        // falls back to the ThemeResource default set directly on TitleTextBlock in XAML
        // Only overridden here when a consumer actually supplies a value, so the default case stays fully
        // theme-reactive without ever touching Application.Current.Resources from C, which does not reliably
        // track live theme changes
        public Brush TitleForeground
        {
            get => (Brush)GetValue(TitleForegroundProperty);
            set => SetValue(TitleForegroundProperty, value);
        }
        public static readonly DependencyProperty TitleForegroundProperty =
            DependencyProperty.Register(
                nameof(TitleForeground),
                typeof(Brush),
                typeof(InfoPopupControl),
                new PropertyMetadata(null, OnTitleForegroundChanged));

        private static void OnTitleForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is InfoPopupControl control && control.TitleTextBlock != null && e.NewValue is Brush brush)
            {
                control.TitleTextBlock.Foreground = brush;
            }
        }

        // whether the info button, and therefore the whole popup, is shown at all
        public bool ShowInfoButton
        {
            get => (bool)GetValue(ShowInfoButtonProperty);
            set => SetValue(ShowInfoButtonProperty, value);
        }
        public static readonly DependencyProperty ShowInfoButtonProperty =
            DependencyProperty.Register(
                nameof(ShowInfoButton),
                typeof(bool),
                typeof(InfoPopupControl),
                new PropertyMetadata(true));

        // width and height of the (always square) info button
        public double ButtonSize
        {
            get => (double)GetValue(ButtonSizeProperty);
            set => SetValue(ButtonSizeProperty, value);
        }
        public static readonly DependencyProperty ButtonSizeProperty =
            DependencyProperty.Register(
                nameof(ButtonSize),
                typeof(double),
                typeof(InfoPopupControl),
                new PropertyMetadata(22.0));

        public CornerRadius ButtonCornerRadius
        {
            get => (CornerRadius)GetValue(ButtonCornerRadiusProperty);
            set => SetValue(ButtonCornerRadiusProperty, value);
        }
        public static readonly DependencyProperty ButtonCornerRadiusProperty =
            DependencyProperty.Register(
                nameof(ButtonCornerRadius),
                typeof(CornerRadius),
                typeof(InfoPopupControl),
                new PropertyMetadata(new CornerRadius(4)));

        public Brush ButtonBackground
        {
            get => (Brush)GetValue(ButtonBackgroundProperty);
            set => SetValue(ButtonBackgroundProperty, value);
        }
        public static readonly DependencyProperty ButtonBackgroundProperty =
            DependencyProperty.Register(
                nameof(ButtonBackground),
                typeof(Brush),
                typeof(InfoPopupControl),
                new PropertyMetadata(new SolidColorBrush(Colors.Transparent)));

        // Segoe Fluent Icons glyph shown inside the button, see fluenticons.xyz
        public string ButtonGlyph
        {
            get => (string)GetValue(ButtonGlyphProperty);
            set => SetValue(ButtonGlyphProperty, value);
        }
        public static readonly DependencyProperty ButtonGlyphProperty =
            DependencyProperty.Register(
                nameof(ButtonGlyph),
                typeof(string),
                typeof(InfoPopupControl),
                new PropertyMetadata("\uE946"));

        public double ButtonGlyphSize
        {
            get => (double)GetValue(ButtonGlyphSizeProperty);
            set => SetValue(ButtonGlyphSizeProperty, value);
        }
        public static readonly DependencyProperty ButtonGlyphSizeProperty =
            DependencyProperty.Register(
                nameof(ButtonGlyphSize),
                typeof(double),
                typeof(InfoPopupControl),
                new PropertyMetadata(12.0));

        // same pattern as TitleForeground above, default lives on ButtonGlyphIcon in XAML
        public Brush ButtonGlyphForeground
        {
            get => (Brush)GetValue(ButtonGlyphForegroundProperty);
            set => SetValue(ButtonGlyphForegroundProperty, value);
        }
        public static readonly DependencyProperty ButtonGlyphForegroundProperty =
            DependencyProperty.Register(
                nameof(ButtonGlyphForeground),
                typeof(Brush),
                typeof(InfoPopupControl),
                new PropertyMetadata(null, OnButtonGlyphForegroundChanged));

        private static void OnButtonGlyphForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is InfoPopupControl control && control.ButtonGlyphIcon != null && e.NewValue is Brush brush)
            {
                control.ButtonGlyphIcon.Foreground = brush;
            }
        }

        // short label for where this content comes from, e.g. "Windows Management Instrumentation (WMI)"; shown
        // as its own line above Description; the whole line, not just the text, collapses when empty
        public string Source
        {
            get => (string)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }
        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register(
                nameof(Source),
                typeof(string),
                typeof(InfoPopupControl),
                new PropertyMetadata(string.Empty));

        // the explanation itself: one or more paragraphs, each separated by a single \n (authored in XAML as
        // &#10;); split into individual TextBlocks by SplitParagraphs below instead of relying on a single
        // TextBlock to render embedded line breaks
        public string Description
        {
            get => (string)GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }
        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register(
                nameof(Description),
                typeof(string),
                typeof(InfoPopupControl),
                new PropertyMetadata(string.Empty));

        // which element the popup anchors to and which direction it opens in, see PopupPlacementMode
        public PopupPlacementMode PlacementMode
        {
            get => (PopupPlacementMode)GetValue(PlacementModeProperty);
            set => SetValue(PlacementModeProperty, value);
        }
        public static readonly DependencyProperty PlacementModeProperty =
            DependencyProperty.Register(
                nameof(PlacementMode),
                typeof(PopupPlacementMode),
                typeof(InfoPopupControl),
                new PropertyMetadata(PopupPlacementMode.Below));


        // === Event Handlers ===

        // InfoPopup is authored inside ButtonHost and only actually moved for PlacementMode.TitleAnchored;
        // simpler than duplicating the popup markup once per host, and PlacementMode is not expected to change
        // after load
        private void InfoPopupControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_popupRelocated || PlacementMode != PopupPlacementMode.TitleAnchored) return;

            ButtonHost.Children.Remove(InfoPopup);
            TitleHost.Children.Add(InfoPopup);
            _popupRelocated = true;
        }

        // PopupContentBorder is x:Load="False"; FindName forces it into the tree on first click and is a cheap no-op
        // every click after that, avoids building the popup content at all for buttons that never get clicked
        private void InfoButton_Click(object sender, RoutedEventArgs e)
        {
            FindName(nameof(PopupContentBorder));
            InfoPopup.IsOpen = !InfoPopup.IsOpen;
        }

        // recomputed on SizeChanged rather than Popup.Opened:
        // Opened can fire before the popups content has actually been measured, which intermittently read a 0x0
        // size and left the popup at its XAML-default offset instead of the calculated one; SizeChanged only ever
        // fires after a real layout pass, so it cannot race like that
        private void PopupContent_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var content = (FrameworkElement)sender;

            if (PlacementMode == PopupPlacementMode.TitleAnchored)
                PositionPopupTitleAnchored(content);
            else
                PositionPopupRelativeToButton(content);
        }


        // === Private Helpers ===

        // positions the popup relative to the title text (TitleHost), not the button; unchanged from the original
        // InfoGroupHeaderControl logic, this is the one case that still needs it
        private void PositionPopupTitleAnchored(FrameworkElement content)
        {
            InfoPopup.HorizontalOffset = -(content.ActualWidth + PopupHorizontalGap);

            Point origin = TitleHost.TransformToVisual(XamlRoot.Content).TransformPoint(new Point(0, 0));
            double availableHeightBelow = XamlRoot.Size.Height - origin.Y;

            double verticalOffset = content.ActualHeight + PopupVerticalGap > availableHeightBelow
                ? -(content.ActualHeight - availableHeightBelow) - PopupVerticalGap
                : PopupVerticalGap;

            InfoPopup.VerticalOffset = Math.Max(verticalOffset, -origin.Y) - PopupVerticalManualAdjustment;
        }

        // simple fixed-direction placement for the four button-anchored modes; no flip or collision handling,
        // unlike TitleAnchored the caller is expected to only pick a direction where there is actually room
        private void PositionPopupRelativeToButton(FrameworkElement content)
        {
            switch (PlacementMode)
            {
                case PopupPlacementMode.Below:
                    InfoPopup.HorizontalOffset = (ButtonSize - content.ActualWidth) / 2;
                    InfoPopup.VerticalOffset = ButtonSize + PopupVerticalGap;
                    break;

                case PopupPlacementMode.Above:
                    InfoPopup.HorizontalOffset = (ButtonSize - content.ActualWidth) / 2;
                    InfoPopup.VerticalOffset = -(content.ActualHeight + PopupVerticalGap);
                    break;

                case PopupPlacementMode.Left:
                    InfoPopup.HorizontalOffset = -(content.ActualWidth + PopupHorizontalGap);
                    InfoPopup.VerticalOffset = (ButtonSize - content.ActualHeight) / 2;
                    break;

                case PopupPlacementMode.Right:
                    InfoPopup.HorizontalOffset = ButtonSize + PopupHorizontalGap;
                    InfoPopup.VerticalOffset = (ButtonSize - content.ActualHeight) / 2;
                    break;
            }
        }

        private Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        private Visibility GetTitleVisibility(string title) =>
            string.IsNullOrEmpty(title) ? Visibility.Collapsed : Visibility.Visible;

        private Visibility GetSourceVisibility(string source) =>
            string.IsNullOrEmpty(source) ? Visibility.Collapsed : Visibility.Visible;

        private string FormatSource(string source) => $"Source: {source}";

        private List<string> SplitParagraphs(string description)
        {
            if (string.IsNullOrEmpty(description)) return new List<string>();

            return description
                .Split('\n')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();
        }
    }
}