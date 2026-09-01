using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Numerics;


namespace FluentSensors.Features.TaskbarWidget
{
    // hand painted drop shadow for the flyout body, drawn into the transparent margin around it
    //
    // DWM offers no parameters for its window shadow, it is on or off; the flyout keeps it off by never asking for
    // DWMWCP_ROUND and this shadow takes its place, so blur, opacity and offset become knobs (the Shadow constants
    // in FlyoutShadowWindow)
    //
    // it is built as eight clipped strips forming a ring around the body rather than one filled sprite: in the glass
    // modes the body is translucent, and a shadow that keeps its interior would darken the material through it
    // four of the strips are the sides, the other four are the corner segments the sides leave out, see Update
    internal sealed class FlyoutDropShadow
    {
        // four sides plus the four corner notches between the rounded outline and the body rectangle
        private const int StripCount = 8;

        private readonly Compositor _compositor;
        private readonly ContainerVisual _container;
        private readonly SpriteVisual[] _strips = new SpriteVisual[StripCount];
        private readonly DropShadow[] _shadows = new DropShadow[StripCount];

        // the silhouette the shadow is cast from: a rounded rect the size of the body, rendered into a surface and
        // handed to the shadows as their mask
        private readonly CompositionRoundedRectangleGeometry _maskGeometry;
        private readonly ShapeVisual _maskVisual;
        private readonly CompositionVisualSurface _maskSurface;

        internal FlyoutDropShadow(UIElement host)
        {
            _compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;

            _maskGeometry = _compositor.CreateRoundedRectangleGeometry();

            var maskShape = _compositor.CreateSpriteShape(_maskGeometry);
            maskShape.FillBrush = _compositor.CreateColorBrush(Colors.Black);

            _maskVisual = _compositor.CreateShapeVisual();
            _maskVisual.Shapes.Add(maskShape);

            _maskSurface = _compositor.CreateVisualSurface();
            _maskSurface.SourceVisual = _maskVisual;

            var maskBrush = _compositor.CreateSurfaceBrush(_maskSurface);

            _container = _compositor.CreateContainerVisual();

            for (int i = 0; i < StripCount; i++)
            {
                var shadow = _compositor.CreateDropShadow();
                shadow.Mask = maskBrush;

                // no brush on the sprite on purpose: it contributes nothing of its own, only its shadow is drawn
                var strip = _compositor.CreateSpriteVisual();
                strip.Shadow = shadow;

                _shadows[i] = shadow;
                _strips[i] = strip;
                _container.Children.InsertAtTop(strip);
            }

            ElementCompositionPreview.SetElementChildVisual(host, _container);
        }


        internal void SetVisible(bool visible)
        {
            _container.IsVisible = visible;
        }

        // margin is the transparent ring the shadow may reach into, body is the visible flyout rect inside it
        internal void Update(float margin, Vector2 body, float cornerRadius, float blurRadius, float opacity, Vector3 offset)
        {
            if (body.X <= 0 || body.Y <= 0) return;

            _maskGeometry.Size = body;
            _maskGeometry.CornerRadius = new Vector2(cornerRadius);
            _maskVisual.Size = body;
            _maskSurface.SourceSize = body;

            _container.Size = body;

            for (int i = 0; i < StripCount; i++)
            {
                _strips[i].Size = body;
                _strips[i].Offset = new Vector3(margin, margin, 0);

                _shadows[i].BlurRadius = blurRadius;
                _shadows[i].Opacity = opacity;
                _shadows[i].Offset = offset;
                _shadows[i].Color = Colors.Black;
            }

            // each strip shows one side of the ring; an inset clip is measured from the strips own bounds, so a
            // negative value reaches outward into the margin and a full side length collapses the opposite edge
            _strips[0].Clip = CreateClip(-margin, -margin, -margin, body.Y);
            _strips[1].Clip = CreateClip(-margin, body.Y, -margin, -margin);
            _strips[2].Clip = CreateClip(-margin, 0, body.X, 0);
            _strips[3].Clip = CreateClip(body.X, 0, -margin, 0);

            // the four sides stop at the body rectangle, which leaves the notches between the rounded outline and
            // the square corners unshaded; against the shadow around them those read as a hard square corner, so
            // they get their own segments
            // a segment covers the whole corner square, the part of it that falls inside the rounded body is
            // hidden behind the flyout and only reaches the material as a faint darkening
            float corner = Math.Min(cornerRadius, Math.Min(body.X, body.Y) / 2f);

            _strips[4].Clip = CreateClip(0, 0, body.X - corner, body.Y - corner);
            _strips[5].Clip = CreateClip(body.X - corner, 0, 0, body.Y - corner);
            _strips[6].Clip = CreateClip(0, body.Y - corner, body.X - corner, 0);
            _strips[7].Clip = CreateClip(body.X - corner, body.Y - corner, 0, 0);
        }

        private InsetClip CreateClip(float left, float top, float right, float bottom)
        {
            var clip = _compositor.CreateInsetClip();
            clip.LeftInset = left;
            clip.TopInset = top;
            clip.RightInset = right;
            clip.BottomInset = bottom;
            return clip;
        }
    }
}
