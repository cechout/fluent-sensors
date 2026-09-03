using FluentSensors.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        // horizontal: space to the side the popup opens towards
        // vertical: space above/below the anchor, direction depends on PlacementMode
        private const double PopupHorizontalGap = 10;
        private const double PopupVerticalGap = 8;

        // manual correction for PlacementMode.TitleAnchored only, adjusts vertical popup position to match the
        // title TextBlock; not used by the button-anchored placements
        private const double PopupVerticalManualAdjustment = 18;

        // pixel gap between the end of the title text and the info button; adjust this to change that spacing
        private const double TitleButtonGap = 4;

        // whether InfoPopup currently sits at the window root instead of inside this control, see
        // RelocatePopupToWindowRoot
        // without it every click after the first would try to add the popup to a host it is already a child of, and
        // the unload that hands it back would run for instances that never moved it in the first place
        // that unload/reload cycle is real: DetailViews are cached and reattached to the live tree on repeat
        // navigation instead of recreated
        private bool _popupRelocated;

        // the panel InfoPopup currently hangs in, which is what its offsets are measured against
        // starts out as the authored ButtonHost and becomes the window root once the relocation below succeeds
        private Panel _popupHost;

        // set when the popup is opened, cleared once it has been placed for that open, see UpdatePopupPlacement
        private bool _needsPopupPlacement;

        // backing store for SourceLinks below; a plain read-only IList property (not a DependencyProperty) so XAML
        // can populate it via nested <InfoPopupControl.SourceLinks> elements, the same pattern NavigationView uses
        // for MenuItems
        private readonly ObservableCollection<SourceLink> _sourceLinks = new();


        // === Constructor ===

        public InfoPopupControl()
        {
            InitializeComponent();

            _popupHost = ButtonHost;
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
        // as its own plain text line above Description
        // only rendered when SourceLinks below is empty, a populated SourceLinks list replaces this line with
        // clickable buttons instead; the whole line collapses when empty either way
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

        // clickable alternative to the plain Source line above, one HyperlinkButton per entry, opens in the system
        // default browser; populated via nested XAML content:
        // <fhInfoPopup:InfoPopupControl.SourceLinks>
        //     <fhInfoPopup:SourceLink Label="..." Url="..." />
        // </fhInfoPopup:InfoPopupControl.SourceLinks>
        // empty by default, so every caller still on the plain Source string keeps working unchanged
        public IList<SourceLink> SourceLinks => _sourceLinks;

        // optional short text shown above SourceLinks, introduces what the links below are
        // (Title top, then SourceIntro, then SourceLinks/Source, then Description, see PopupContentBorder)
        public string SourceIntro
        {
            get => (string)GetValue(SourceIntroProperty);
            set => SetValue(SourceIntroProperty, value);
        }
        public static readonly DependencyProperty SourceIntroProperty =
            DependencyProperty.Register(
                nameof(SourceIntro),
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

        // the popup lives outside this control once it has been opened, so a control that leaves the tree has to
        // take it back with it; without this every discarded instance would strand its popup in the window root
        // for good
        private void InfoPopupControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_popupRelocated) return;

            InfoPopup.IsOpen = false;

            _popupHost.Children.Remove(InfoPopup);
            ButtonHost.Children.Add(InfoPopup);

            _popupHost = ButtonHost;
            _popupRelocated = false;
        }

        // PopupContentBorder is x:Load="False"; FindName forces it into the tree on first click and is a cheap no-op
        // every click after that, avoids building the popup content at all for buttons that never get clicked
        // it has to run before the relocation below, since it resolves against this controls own tree and would come
        // up empty once the popup has been moved out of it, leaving a popup with no content at all
        private void InfoButton_Click(object sender, RoutedEventArgs e)
        {
            FindName(nameof(PopupContentBorder));

            RelocatePopupToWindowRoot();

            bool isOpening = !InfoPopup.IsOpen;

            // placing before opening rather than after, so the popup never shows up at the spot it was left at last
            // time and then jumps
            // the popup no longer follows its button around, so every open has to place it again
            if (isOpening)
            {
                _needsPopupPlacement = true;
                UpdatePopupPlacement();
            }

            InfoPopup.IsOpen = isOpening;
        }

        // title and button overlap in the same cell instead of separate grid columns; the title reserves room for
        // the button via a plain right Margin (a real measure-time constraint, so TextTrimming/TextWrapping still
        // work correctly), and the button is positioned directly off the titles own ActualWidth here, no
        // cross-element width subtraction involved
        private void TitleTextBlock_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ButtonHost.Margin = new Thickness(TitleTextBlock.ActualWidth + TitleButtonGap, 0, 0, 0);
        }

        // the click above does the placing, but on the very first open the content has not been measured yet and
        // reads 0x0, which the placement cannot work with; this is the callback right after that first real layout
        // pass, and it finishes the placement the click could not
        // it does nothing once the popup has been placed for the current open
        private void PopupContent_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePopupPlacement();
        }


        // === Private Helpers ===

        // hands InfoPopup over to the window root, which is the whole point of this file:
        // a Popup measures its offsets against whatever panel it hangs in, so left inside ButtonHost it slides along
        // with every layout pass that moves this control, and in the title bar that happens several times a second as
        // the live readouts next to it change width
        // at the window root nothing moves it, so a placement holds until the popup is opened again
        //
        // a root that cannot take children leaves the popup where it was authored; the placement works in window
        // coordinates either way, it just goes back to riding along with the control
        private void RelocatePopupToWindowRoot()
        {
            if (_popupRelocated || XamlRoot?.Content is not Panel rootPanel) return;

            ButtonHost.Children.Remove(InfoPopup);
            rootPanel.Children.Add(InfoPopup);

            _popupHost = rootPanel;
            _popupRelocated = true;
        }

        // places the popup once per open, in window coordinates
        //
        // the anchor position and the wanted popup position are both worked out against the window, and only the last
        // step converts them into offsets against the panel the popup hangs in; with the popup sitting at the window
        // root that conversion subtracts nothing, which is exactly what makes the placement outlive later layout
        // passes
        private void UpdatePopupPlacement()
        {
            if (!_needsPopupPlacement || _popupHost == null) return;
            if (PopupContentBorder == null || XamlRoot?.Content == null) return;

            // ActualWidth/ActualHeight only hold a real size once the popup has been open and laid out at least
            // once; before that the content is not live, its bindings have not run, and a Measure here reports a
            // text block that is still empty, so the size comes out too small
            //
            // the first open is therefore placed from that provisional size and stays flagged, so the SizeChanged
            // right after the real layout pass corrects it; every later open has a real size to work with straight
            // away and is final immediately
            bool hasRealSize = PopupContentBorder.ActualWidth > 0 && PopupContentBorder.ActualHeight > 0;
            Size contentSize;

            if (hasRealSize)
            {
                contentSize = new Size(PopupContentBorder.ActualWidth, PopupContentBorder.ActualHeight);
            }
            else
            {
                PopupContentBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                contentSize = PopupContentBorder.DesiredSize;
            }

            if (contentSize.Width <= 0 || contentSize.Height <= 0) return;

            Point target = PlacementMode == PopupPlacementMode.TitleAnchored
                ? GetTitleAnchoredPosition(contentSize)
                : GetButtonAnchoredPosition(contentSize);

            Point hostOrigin = _popupHost.TransformToVisual(XamlRoot.Content).TransformPoint(new Point(0, 0));

            InfoPopup.HorizontalOffset = target.X - hostOrigin.X;
            InfoPopup.VerticalOffset = target.Y - hostOrigin.Y;

            _needsPopupPlacement = !hasRealSize;
        }

        // positions the popup relative to the title text (TitleHost), not the button; unchanged from the original
        // InfoGroupHeaderControl logic, this is the one case that still needs it
        private Point GetTitleAnchoredPosition(Size content)
        {
            Point origin = TitleHost.TransformToVisual(XamlRoot.Content).TransformPoint(new Point(0, 0));
            double availableHeightBelow = XamlRoot.Size.Height - origin.Y;

            double verticalOffset = content.Height + PopupVerticalGap > availableHeightBelow
                ? -(content.Height - availableHeightBelow) - PopupVerticalGap
                : PopupVerticalGap;

            // the Max keeps the popup from being pushed off the top edge of the window
            return new Point(
                origin.X - (content.Width + PopupHorizontalGap),
                Math.Max(origin.Y + verticalOffset, 0) - PopupVerticalManualAdjustment);
        }

        // simple fixed-direction placement for the four button-anchored modes; no flip or collision handling,
        // unlike TitleAnchored the caller is expected to only pick a direction where there is actually room
        private Point GetButtonAnchoredPosition(Size content)
        {
            Point origin = ButtonHost.TransformToVisual(XamlRoot.Content).TransformPoint(new Point(0, 0));

            return PlacementMode switch
            {
                PopupPlacementMode.Above => new Point(
                    origin.X + (ButtonSize - content.Width) / 2,
                    origin.Y - (content.Height + PopupVerticalGap)),

                PopupPlacementMode.Left => new Point(
                    origin.X - (content.Width + PopupHorizontalGap),
                    origin.Y + (ButtonSize - content.Height) / 2),

                PopupPlacementMode.Right => new Point(
                    origin.X + ButtonSize + PopupHorizontalGap,
                    origin.Y + (ButtonSize - content.Height) / 2),

                // Below, which is also the default the DependencyProperty falls back to
                _ => new Point(
                    origin.X + (ButtonSize - content.Width) / 2,
                    origin.Y + ButtonSize + PopupVerticalGap),
            };
        }

        private Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        private Visibility GetTitleVisibility(string title) =>
            string.IsNullOrEmpty(title) ? Visibility.Collapsed : Visibility.Visible;

        // reserves room for the button on the right of the title text, but only when the button is actually shown;
        // a real Margin, so it is a genuine measure-time constraint and TextTrimming/TextWrapping correctly leave
        // this much space alone
        private Thickness GetTitleMargin(double buttonSize, bool showInfoButton) =>
            showInfoButton ? new Thickness(0, 0, buttonSize + TitleButtonGap, 0) : new Thickness(0);

        // plain Source line: only shown when there is no SourceLinks entry to show instead
        private Visibility GetSourceVisibility(string source, IList<SourceLink> sourceLinks) =>
            !string.IsNullOrEmpty(source) && sourceLinks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private Visibility GetSourceLinksVisibility(IList<SourceLink> sourceLinks) =>
            sourceLinks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // same empty check as GetTitleVisibility above, own name since it is used for SourceIntro specifically
        private Visibility GetSourceIntroVisibility(string sourceIntro) =>
            string.IsNullOrEmpty(sourceIntro) ? Visibility.Collapsed : Visibility.Visible;

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
