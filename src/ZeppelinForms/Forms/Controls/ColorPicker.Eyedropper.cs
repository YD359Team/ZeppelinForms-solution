using System.Globalization;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Theming;

namespace ZeppelinForms.Forms.Controls;

public partial class ColorPicker
{
    /// <summary>A filled pipette in a 24-unit square: the bulb at the top right,
    /// the tube down to the tip at the bottom left.</summary>
    private const string EyedropperIcon =
        "M17 3a3 3 0 0 1 4.24 4.24l-2.12 2.12 1.41 1.41-1.41 1.42-1.42-1.42L9.5 18.96 5.5 20.5l-2 .5.5-2 1.54-4 8.19-8.2-1.42-1.41 1.42-1.42 1.41 1.42z";

    private EyedropperOverlay? _eyedropper;

    /// <summary>The eyedropper is picking a color from the form now.</summary>
    internal bool IsPicking => _eyedropper is not null;

    /// <summary>Start picking: the editor closes, the form is captured once, and an
    /// overlay over all of it takes the color under the cursor.</summary>
    /// <remarks>A capture rather than reading the screen as the cursor moves: the
    /// magnifier would otherwise read itself, and there is no screen to read on
    /// every platform — in the browser there is only the canvas.</remarks>
    internal void StartPicking()
    {
        if (_eyedropper is not null) return;
        if (FindOwner() is not { } form || form.Content is not { } content) return;

        CloseEditor();

        // the layout of the frame the user sees, not a stale one
        form.UpdateLayout();

        int width = Math.Max(1, (int)MathF.Ceiling(content.ActualSize.Width));
        int height = Math.Max(1, (int)MathF.Ceiling(content.ActualSize.Height));

        var source = new EyedropperSource(
            ElementRenderer.Current.Render(content, width, height),
            content,
            form.EffectiveBackground);

        var overlay = new EyedropperOverlay(this, source)
        {
            Position = Point.Empty,
            Size = form.ClientSize,
        };

        overlay.Localize(nameof(AccessibleName), static (o, text) => o.AccessibleName = text, ZfText.PickColor);

        _eyedropper = overlay;

        // the capture is of the old size: a resized form would be picked from a
        // picture that no longer matches it
        form.ClientSizeChanged += OnPickingFormResized;

        form.AddOverlay(overlay);
        form.FocusElement(overlay);
    }

    /// <summary>End picking: take <paramref name="picked"/>, or nothing when it is null,
    /// and give the focus back to the picker.</summary>
    internal void EndPicking(Color? picked)
    {
        if (_eyedropper is not { } overlay) return;

        _eyedropper = null;

        if (overlay.FindOwner() is { } form)
        {
            form.ClientSizeChanged -= OnPickingFormResized;
            form.RemoveOverlay(overlay);
        }

        // the form's pixels are opaque: the alpha stays the picker's own
        if (picked is { } color)
            Value = new Color(_value.A, color.R, color.G, color.B);

        if (FindOwner() is { } own)
            own.FocusElement(this);
    }

    private void OnPickingFormResized(object? sender, EventArgs e) => EndPicking(null);

    /// <summary>The captured form and how to read it: a point of the form maps into
    /// the capture of its content, and what the content left transparent shows the
    /// form's background, as on screen.</summary>
    internal sealed class EyedropperSource(Image capture, UIElement content, Color background)
    {
        public Image Capture { get; } = capture;

        /// <summary>The color at a point of the form, in its client coordinates.</summary>
        public Color ColorAt(Point point)
        {
            Point local = content.TransformPointToLocal(point);

            int x = (int)MathF.Floor(local.X);
            int y = (int)MathF.Floor(local.Y);

            if (x < 0 || y < 0 || x >= Capture.Width || y >= Capture.Height)
                return background;

            // RGBA, premultiplied, as every Image is
            byte[] pixels = Capture.Pixels;
            int i = (y * Capture.Width + x) * 4;
            int alpha = pixels[i + 3];

            if (alpha == 255)
                return new Color(255, pixels[i], pixels[i + 1], pixels[i + 2]);

            // the pixel over the background, both premultiplied
            int rest = 255 - alpha;
            int backAlpha = background.A * rest / 255;
            int outAlpha = alpha + backAlpha;

            if (outAlpha == 0)
                return new Color(0, 0, 0, 0);

            int r = pixels[i] + background.R * backAlpha / 255;
            int g = pixels[i + 1] + background.G * backAlpha / 255;
            int b = pixels[i + 2] + background.B * backAlpha / 255;

            return new Color(
                (byte)outAlpha,
                (byte)Math.Min(255, r * 255 / outAlpha),
                (byte)Math.Min(255, g * 255 / outAlpha),
                (byte)Math.Min(255, b * 255 / outAlpha));
        }
    }

    /// <summary>The overlay over the whole form while picking: the cross cursor, the
    /// magnifier beside it, and the input that ends the picking.</summary>
    internal sealed class EyedropperOverlay : InteractiveControl
    {
        /// <summary>The magnifier shows this many pixels across, the picked one in
        /// the middle.</summary>
        private const int Cells = 11;
        private const float CellSize = 9f;
        private const float LabelHeight = 22f;
        private const float Offset = 20f;

        private readonly ColorPicker _owner;
        private readonly EyedropperSource _source;

        /// <summary>The point being picked, in the form's coordinates; null until the
        /// cursor or a key first gives one.</summary>
        private Point? _point;

        public EyedropperOverlay(ColorPicker owner, EyedropperSource source)
        {
            _owner = owner;
            _source = source;

            Cursor = CursorKind.Cross;
            SetControlDefault(BackgroundProperty, Colors.Transparent);
            SetControlDefault(BorderWidthProperty, 0f);
        }

        internal Point? PickPoint => _point;

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            _point = e.Location;
            InvalidateVisual();
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            // taken on the release: the press mustn't be followed by a release over
            // the control under the overlay once it is gone
            _point = e.Location;
            e.Handled = true;
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            switch (e.Button)
            {
                case MouseButton.Left:
                    _owner.EndPicking(_source.ColorAt(e.Location));
                    break;

                case MouseButton.Right:
                    _owner.EndPicking(null);
                    break;
            }

            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            float step = (e.Modifiers & KeyModifiers.Shift) != 0 ? 10f : 1f;

            switch (e.Key)
            {
                case Key.Escape:
                    _owner.EndPicking(null);
                    break;

                case Key.Enter:
                    if (_point is { } point)
                        _owner.EndPicking(_source.ColorAt(point));
                    break;

                case Key.Left: Move(-step, 0); break;
                case Key.Right: Move(step, 0); break;
                case Key.Up: Move(0, -step); break;
                case Key.Down: Move(0, step); break;

                default:
                    base.OnKeyDown(e);
                    return;
            }

            e.Handled = true;
        }

        /// <summary>Move the point by the keyboard; without a point yet it starts in
        /// the middle of the form.</summary>
        private void Move(float dx, float dy)
        {
            Point from = _point ?? new Point(ActualSize.Width / 2f, ActualSize.Height / 2f);

            _point = new Point(
                Math.Clamp(from.X + dx, 0f, Math.Max(0f, ActualSize.Width - 1f)),
                Math.Clamp(from.Y + dy, 0f, Math.Max(0f, ActualSize.Height - 1f)));

            InvalidateVisual();
        }

        protected override void DrawContent(Graphics g)
        {
            if (_point is not { } point) return;

            float side = Cells * CellSize;
            var size = new Size(side, side + LabelHeight);

            // beside the cursor, and to the other side of it near an edge
            float x = point.X + Offset + size.Width > ActualSize.Width ? point.X - Offset - size.Width : point.X + Offset;
            float y = point.Y + Offset + size.Height > ActualSize.Height ? point.Y - Offset - size.Height : point.Y + Offset;

            var loupe = new Rectangle(new Point(MathF.Max(0, x), MathF.Max(0, y)), new Size(side, side));
            var radius = new CornerRadius(4f);

            Theme theme = App.Theme;

            g.DrawShadow(new Rectangle(loupe.Position, size), new BoxShadow(0, 2, 8, 0, new Color(64, 0, 0, 0)));

            g.Save();
            g.ClipRoundRect(loupe, radius);

            int half = Cells / 2;

            for (int row = 0; row < Cells; row++)
            {
                for (int column = 0; column < Cells; column++)
                {
                    Color color = _source.ColorAt(new Point(point.X + column - half, point.Y + row - half));

                    g.FillRectangle(
                        new Rectangle(
                            new Point(loupe.X + column * CellSize, loupe.Y + row * CellSize),
                            new Size(CellSize, CellSize)),
                        color);
                }
            }

            g.Restore();

            // the picked pixel: black around white, seen on any color
            var center = new Rectangle(
                new Point(loupe.X + half * CellSize, loupe.Y + half * CellSize),
                new Size(CellSize, CellSize));

            g.DrawRectangle(center, Colors.Black, 1f);
            g.DrawRectangle(
                new Rectangle(new Point(center.X + 1, center.Y + 1), new Size(CellSize - 2, CellSize - 2)),
                Colors.White, 1f);

            g.DrawRoundRectangle(loupe, radius, theme.Colors.Border, 1f);

            // under the magnifier: the color and its hex
            Color picked = _source.ColorAt(point);

            var label = new Rectangle(new Point(loupe.X, loupe.Y + side + 2), new Size(side, LabelHeight - 2));

            g.FillRoundRectangle(label, radius, theme.Colors.Surface);
            g.DrawRoundRectangle(label, radius, theme.Colors.Border, 1f);

            var swatch = new Rectangle(
                new Point(label.X + 4, label.Y + 4),
                new Size(label.Height - 8, label.Height - 8));

            g.FillRoundRectangle(swatch, new CornerRadius(2f), picked);
            g.DrawRoundRectangle(swatch, new CornerRadius(2f), _owner.SwatchBorderColor, 1f);

            string hex = string.Create(CultureInfo.InvariantCulture, $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}");

            g.DrawTextLine(hex,
                new Rectangle(
                    new Point(swatch.Right + 6, label.Y),
                    new Size(Math.Max(0, label.Right - swatch.Right - 10), label.Height)),
                theme.Colors.Text, EffectiveFont,
                HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
        }
    }
}