using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public partial class ColorPicker
{
    /// <summary>The editor's spectrum: a field of saturation (across) and brightness
    /// (down) for the current hue, and under it a strip of hues.</summary>
    private sealed class SpectrumView : DecoratedControl
    {
        private const float HueHeight = 14f;
        private const float Gap = 8f;
        private const float MarkerRadius = 6f;

        private enum DragPart : byte { None, Field, Hue }

        private readonly ColorPicker _owner;

        // kept here rather than computed from the value each time: a gray has no hue
        // and black no saturation, and the spectrum mustn't jump to red when the
        // field is dragged into a corner
        private float _hue;
        private float _saturation;
        private float _brightness;

        private DragPart _drag;

        /// <summary>The spectrum is setting the value itself: the sync that follows
        /// mustn't take the hue back from the color, rounded to bytes.</summary>
        private bool _applying;

        public SpectrumView(ColorPicker owner)
        {
            _owner = owner;
            Cursor = CursorKind.Cross;
            Sync();
        }

        /// <summary>The value changed elsewhere — the sliders, the eyedropper, the
        /// application.</summary>
        public void Sync()
        {
            if (_applying) return;

            (float h, float s, float v) = HsvColor.FromColor(_owner.Value);

            if (s > 0 && v > 0) _hue = h;
            if (v > 0) _saturation = s;
            _brightness = v;

            InvalidateVisual();
        }

        private Rectangle FieldBounds
        {
            get
            {
                Rectangle content = ContentBounds;

                return new Rectangle(
                    content.Position,
                    new Size(content.Width, Math.Max(0, content.Height - HueHeight - Gap)));
            }
        }

        private Rectangle HueBounds
        {
            get
            {
                Rectangle content = ContentBounds;

                return new Rectangle(
                    new Point(content.X, content.Y + content.Height - HueHeight),
                    new Size(content.Width, HueHeight));
            }
        }

        protected override void DrawContent(Graphics g)
        {
            Rectangle field = FieldBounds;
            Rectangle hue = HueBounds;
            var radius = new CornerRadius(3f);

            // the field: white to the pure hue across, then transparent to black down
            Color pure = HsvColor.ToColor(_hue, 1f, 1f, 255);

            g.FillGradient(field, radius, [new GradientStop(Colors.White, 0f), new GradientStop(pure, 1f)], 0f);
            g.FillGradient(field, radius, [new GradientStop(new Color(0, 0, 0, 0), 0f), new GradientStop(Colors.Black, 1f)], 90f);
            g.DrawRoundRectangle(field, radius, _owner.SwatchBorderColor, 1f);

            var marker = new Point(
                field.X + _saturation * field.Width,
                field.Y + (1f - _brightness) * field.Height);

            DrawMarker(g, new Rectangle(
                new Point(marker.X - MarkerRadius, marker.Y - MarkerRadius),
                new Size(MarkerRadius * 2, MarkerRadius * 2)), circle: true);

            // the hues: red, yellow, green, cyan, blue, magenta and back to red
            g.FillGradient(hue, radius,
            [
                new GradientStop(new Color(255, 0, 0), 0f),
                new GradientStop(new Color(255, 255, 0), 1f / 6f),
                new GradientStop(new Color(0, 255, 0), 2f / 6f),
                new GradientStop(new Color(0, 255, 255), 3f / 6f),
                new GradientStop(new Color(0, 0, 255), 4f / 6f),
                new GradientStop(new Color(255, 0, 255), 5f / 6f),
                new GradientStop(new Color(255, 0, 0), 1f),
            ], 0f);

            g.DrawRoundRectangle(hue, radius, _owner.SwatchBorderColor, 1f);

            float hueX = hue.X + _hue / 360f * hue.Width;

            DrawMarker(g, new Rectangle(
                new Point(hueX - 3f, hue.Y - 2f),
                new Size(6f, hue.Height + 4f)), circle: false);
        }

        /// <summary>White inside black: seen on light colors and dark alike.</summary>
        private static void DrawMarker(Graphics g, Rectangle rect, bool circle)
        {
            var inner = new Rectangle(
                new Point(rect.X + 1f, rect.Y + 1f),
                new Size(Math.Max(0, rect.Width - 2f), Math.Max(0, rect.Height - 2f)));

            if (circle)
            {
                g.DrawEllipse(rect, Colors.Black, 1f);
                g.DrawEllipse(inner, Colors.White, 1.5f);
            }
            else
            {
                var radius = new CornerRadius(2f);

                g.DrawRoundRectangle(rect, radius, Colors.Black, 1f);
                g.DrawRoundRectangle(inner, radius, Colors.White, 1.5f);
            }
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            // only the left button picks: the right one belongs to the context menu
            if (e.Button != MouseButton.Left) return;

            Point local = ToLocal(e.Location);

            // the gap between the two parts goes to the nearer one
            _drag = local.Y >= HueBounds.Y - Gap / 2f ? DragPart.Hue : DragPart.Field;

            // without capture the drag breaks off as soon as the cursor leaves the editor
            CaptureMouse();

            Apply(local);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            if (_drag != DragPart.None)
                Apply(ToLocal(e.Location));
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (_drag == DragPart.None) return;

            _drag = DragPart.None;
            ReleaseMouseCapture();
        }

        private Point ToLocal(Point location)
        {
            Point abs = GetAbsolutePosition();
            return new Point(location.X - abs.X, location.Y - abs.Y);
        }

        private void Apply(Point local)
        {
            if (_drag == DragPart.Hue)
            {
                Rectangle hue = HueBounds;
                if (hue.Width <= 0) return;

                _hue = Math.Clamp((local.X - hue.X) / hue.Width, 0f, 1f) * 360f;
            }
            else
            {
                Rectangle field = FieldBounds;
                if (field.Width <= 0 || field.Height <= 0) return;

                _saturation = Math.Clamp((local.X - field.X) / field.Width, 0f, 1f);
                _brightness = 1f - Math.Clamp((local.Y - field.Y) / field.Height, 0f, 1f);
            }

            _applying = true;

            try
            {
                // the alpha is not the spectrum's: it stays as the slider or the
                // application left it
                _owner.Value = HsvColor.ToColor(_hue, _saturation, _brightness, _owner.Value.A);
            }
            finally
            {
                _applying = false;
            }

            InvalidateVisual();
        }
    }
}

/// <summary>Hue (0–360), saturation and brightness (0–1) — the model of the
/// spectrum's field and strip.</summary>
internal static class HsvColor
{
    public static (float Hue, float Saturation, float Brightness) FromColor(Color color)
    {
        float r = color.R / 255f;
        float g = color.G / 255f;
        float b = color.B / 255f;

        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float delta = max - min;

        float hue = 0f;

        if (delta > 0f)
        {
            if (max == r)
                hue = 60f * ((g - b) / delta);
            else if (max == g)
                hue = 60f * ((b - r) / delta + 2f);
            else
                hue = 60f * ((r - g) / delta + 4f);

            if (hue < 0f) hue += 360f;
        }

        float saturation = max > 0f ? delta / max : 0f;

        return (hue, saturation, max);
    }

    public static Color ToColor(float hue, float saturation, float brightness, byte alpha)
    {
        // 360 is red again, as 0 is
        hue = (hue % 360f + 360f) % 360f;

        float chroma = brightness * saturation;
        float x = chroma * (1f - MathF.Abs(hue / 60f % 2f - 1f));
        float m = brightness - chroma;

        (float r, float g, float b) = (int)(hue / 60f) switch
        {
            0 => (chroma, x, 0f),
            1 => (x, chroma, 0f),
            2 => (0f, chroma, x),
            3 => (0f, x, chroma),
            4 => (x, 0f, chroma),
            _ => (chroma, 0f, x),
        };

        return new Color(alpha, ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static byte ToByte(float channel) => (byte)Math.Clamp(MathF.Round(channel * 255f), 0f, 255f);
}