using FluentSensors.Common.Localization;
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
    // the info popup:
    // an optional title, an info button and a popup explaining one value or a group of them
    // only placement, title and button are configurable, never the popup itself, so every popup looks the same
    public sealed partial class InfoPopupControl : UserControl
    {
        // === fields ===

        // gap to the anchor (the title for TitleAnchored, the button otherwise):
        // horizontal - towards the side the popup opens to
        // vertical - above or below the anchor, per PlacementMode
        private const double PopupHorizontalGap = 10;
        private const double PopupVerticalGap = 8;

        // TitleAnchored only; lines the popup up with the title TextBlock
        private const double PopupVerticalManualAdjustment = 18;

        // between the title text and the info button
        private const double TitleButtonGap = 4;

        // minimum to every window edge
        private const double PopupWindowEdgeMargin = 8;

        // whether InfoPopup sits at the window root, see RelocatePopupToWindowRoot; (guards the next click and the
        // unload that hands it back, which really happens for cached detail views)
        private bool _popupRelocated;

        // the panel InfoPopup hangs in, which its offsets count from; ButtonHost, then the window root
        private Panel _popupHost;

        // from open until placed, see UpdatePopupPlacement
        private bool _needsPopupPlacement;

        // backs SourceLinks, a plain read-only IList so XAML can fill it with nested elements
        // (like NavigationView.MenuItems)
        private readonly ObservableCollection<SourceLink> _sourceLinks = new();


        // === constructor ===

        public InfoPopupControl()
        {
            InitializeComponent();

            _popupHost = ButtonHost;
        }


        // === dependency properties ===

        // collapsed with its layout space when empty
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

        // any content in the title slot, for a label a string cannot express (several bound values, tabular
        // figures); the alternative to Title
        // it follows the open/closed accent like Title, which a sibling TextBlock never could; so it must not set its
        // own Foreground, the rest colour is TitleForeground
        public object TitleContent
        {
            get => GetValue(TitleContentProperty);
            set => SetValue(TitleContentProperty, value);
        }
        public static readonly DependencyProperty TitleContentProperty =
            DependencyProperty.Register(
                nameof(TitleContent),
                typeof(object),
                typeof(InfoPopupControl),
                new PropertyMetadata(null));

        // wrap or overflow
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

        // with NoWrap
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

        // applied to the title TextBlock as is
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

        // only set when a consumer supplies one, so the ThemeResource default on TitleTextBlock stays theme-reactive
        // (Application.Current.Resources from code does not track a live theme change)
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
            if (d is not InfoPopupControl control || e.NewValue is not Brush brush) return;

            if (control.TitleTextBlock != null) control.TitleTextBlock.Foreground = brush;
            if (control.TitleContentPresenter != null) control.TitleContentPresenter.Foreground = brush;
        }

        // the info button, and with it the popup
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

        // the square info button
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

        // Segoe Fluent Icons glyph, see fluenticons.xyz
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

        // like TitleForeground; the default lives on ButtonGlyphIcon in XAML
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

        // where the content comes from ("Windows Management Instrumentation (WMI)"), a plain line above Description;
        // SourceLinks replaces it when filled
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

        // the clickable alternative to Source, one HyperlinkButton per entry, filled in XAML:
        // <fhInfoPopup:InfoPopupControl.SourceLinks>
        //     <fhInfoPopup:SourceLink Label="..." Url="..." />
        // </fhInfoPopup:InfoPopupControl.SourceLinks>
        public IList<SourceLink> SourceLinks => _sourceLinks;

        // a short intro above the links; (order: title, SourceIntro, SourceLinks or Source, Description)
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

        // the explanation; paragraphs split on \n (&#10; in XAML), one TextBlock each, see SplitParagraphs
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

        // anchor and direction, see PopupPlacementMode
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


        // === event handlers ===

        // a control that leaves the tree takes its relocated popup back, or it would strand in the window root
        private void InfoPopupControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_popupRelocated) return;

            InfoPopup.IsOpen = false;

            _popupHost.Children.Remove(InfoPopup);
            ButtonHost.Children.Add(InfoPopup);

            _popupHost = ButtonHost;
            _popupRelocated = false;
        }

        // PopupContentBorder is x:Load="False", FindName builds it on the first click; before the relocation, which
        // would move it out of reach of FindName
        private void InfoButton_Click(object sender, RoutedEventArgs e)
        {
            FindName(nameof(PopupContentBorder));

            RelocatePopupToWindowRoot();

            bool isOpening = !InfoPopup.IsOpen;

            // placed before opening, so it never shows at its last spot and jumps; every open places it again
            if (isOpening)
            {
                _needsPopupPlacement = true;
                UpdatePopupPlacement();
            }

            InfoPopup.IsOpen = isOpening;
        }

        // an open popup turns its title and glyph to the accent color; from Opened and Closed, since a light
        // dismiss skips the click handler
        private void InfoPopup_Opened(object sender, object e)
        {
            VisualStateManager.GoToState(this, "PopupOpen", false);
        }

        private void InfoPopup_Closed(object sender, object e)
        {
            VisualStateManager.GoToState(this, "PopupClosed", false);
        }

        // title and button share a cell; the title reserves the button room with a right Margin, the button sits at the
        // title ActualWidth (both title slots report here, the empty one measures zero)
        private void TitleSlot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double titleWidth = Math.Max(TitleTextBlock.ActualWidth, TitleContentPresenter.ActualWidth);

            ButtonHost.Margin = new Thickness(titleWidth + TitleButtonGap, 0, 0, 0);
        }

        // finishes the first placement once the content has a real size (it reads 0x0 at the first click)
        private void PopupContent_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePopupPlacement();
        }


        // the only interactive part; in the title bar it gets the passthrough rect, the title slot stays
        // draggable (see TitleBarPassthrough)
        public FrameworkElement InteractiveRegion => ButtonHost;


        // === private helpers ===

        // hands InfoPopup over to the window root: a Popup counts its offsets from its panel, so inside ButtonHost it
        // slides with every layout pass (several a second in the title bar), at the root a placement holds
        // a root that takes no children leaves it where authored; it then rides along with the control
        private void RelocatePopupToWindowRoot()
        {
            if (_popupRelocated || XamlRoot?.Content is not Panel rootPanel) return;

            ButtonHost.Children.Remove(InfoPopup);
            rootPanel.Children.Add(InfoPopup);

            _popupHost = rootPanel;
            _popupRelocated = true;
        }

        // places the popup once per open, in window coordinates; only the last step turns them into offsets against its
        // panel (nothing at the window root)
        private void UpdatePopupPlacement()
        {
            if (!_needsPopupPlacement || _popupHost == null) return;
            if (PopupContentBorder == null || XamlRoot?.Content == null) return;

            // a real size only exists after the first open; before that a Measure sees unbound, empty text and comes
            // out too small, so the first open stays flagged and SizeChanged corrects it
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

            target = ClampToWindow(target, contentSize);

            Point hostOrigin = _popupHost.TransformToVisual(XamlRoot.Content).TransformPoint(new Point(0, 0));

            InfoPopup.HorizontalOffset = target.X - hostOrigin.X;
            InfoPopup.VerticalOffset = target.Y - hostOrigin.Y;

            _needsPopupPlacement = !hasRealSize;
        }

        // pulls a placement back inside the window with PopupWindowEdgeMargin; every mode goes through here (a Popup
        // cannot render outside its XamlRoot, an overhang is cut off)
        private Point ClampToWindow(Point target, Size content)
        {
            double maxX = XamlRoot.Size.Width - content.Width - PopupWindowEdgeMargin;
            double maxY = XamlRoot.Size.Height - content.Height - PopupWindowEdgeMargin;

            // Min, then Max: an oversized popup keeps its top left corner visible
            return new Point(
                Math.Max(Math.Min(target.X, maxX), PopupWindowEdgeMargin),
                Math.Max(Math.Min(target.Y, maxY), PopupWindowEdgeMargin));
        }

        // left of the title text (TitleHost), the top a little above it; the edges are left to ClampToWindow
        private Point GetTitleAnchoredPosition(Size content)
        {
            Point origin = TitleHost.TransformToVisual(XamlRoot.Content).TransformPoint(new Point(0, 0));

            return new Point(
                origin.X - (content.Width + PopupHorizontalGap),
                origin.Y + PopupVerticalGap - PopupVerticalManualAdjustment);
        }

        // the four button-anchored modes, never flipped; ClampToWindow keeps them inside
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

                // Below, the default
                _ => new Point(
                    origin.X + (ButtonSize - content.Width) / 2,
                    origin.Y + ButtonSize + PopupVerticalGap),
            };
        }

        private Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        private Visibility GetTitleVisibility(string title, object titleContent) =>
            string.IsNullOrEmpty(title) && titleContent == null ? Visibility.Collapsed : Visibility.Visible;

        // room for a shown button right of the title; a Margin, so trimming and wrapping respect it
        private Thickness GetTitleMargin(double buttonSize, bool showInfoButton) =>
            showInfoButton ? new Thickness(0, 0, buttonSize + TitleButtonGap, 0) : new Thickness(0);

        // only without SourceLinks
        private Visibility GetSourceVisibility(string source, IList<SourceLink> sourceLinks) =>
            !string.IsNullOrEmpty(source) && sourceLinks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private Visibility GetSourceLinksVisibility(IList<SourceLink> sourceLinks) =>
            sourceLinks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        private Visibility GetSourceIntroVisibility(string sourceIntro) =>
            string.IsNullOrEmpty(sourceIntro) ? Visibility.Collapsed : Visibility.Visible;

        private string FormatSource(string source) => AppStrings.Format("InfoPopup_Source", source);

        // screen reader name, with the title, so a page full of these buttons does not read
        // the same label over and over
        private string GetInfoButtonName(string title) =>
            string.IsNullOrEmpty(title) ? AppStrings.Get("InfoPopup_MoreInfo") : AppStrings.Format("InfoPopup_MoreInfoAbout", title);

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
