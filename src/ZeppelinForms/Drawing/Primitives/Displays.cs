namespace ZeppelinForms.Drawing.Primitives;

public static class Displays
{
    public static IDisplayProvider Current { get; set; } = new SingleDisplayProvider();

    /// <summary>All displays. Never empty: a provider that found nothing
    /// is replaced by the stand-in display, so callers can take [0] safely.</summary>
    public static IReadOnlyList<DisplayInfo> All
    {
        get
        {
            IReadOnlyList<DisplayInfo> displays = Current.GetDisplays();

            return displays.Count > 0 ? displays : SingleDisplayProvider.Displays;
        }
    }

    /// <summary>The primary display.</summary>
    /// <remarks>
    /// The list is taken once: this used to read All twice, and on Windows every
    /// read enumerates the monitors through P/Invoke — while gesture recognizers
    /// ask for the primary display at the start of every gesture.
    /// </remarks>
    public static DisplayInfo Primary
    {
        get
        {
            IReadOnlyList<DisplayInfo> displays = All;

            foreach (DisplayInfo display in displays)
                if (display.IsPrimary)
                    return display;

            return displays[0];
        }
    }

    /// <summary>The display the point is on. If none contains it —
    /// the nearest by distance to its center.</summary>
    public static DisplayInfo FromPoint(Point point)
    {
        IReadOnlyList<DisplayInfo> displays = All;

        foreach (DisplayInfo display in displays)
        {
            Rectangle b = display.Bounds;

            if (point.X >= b.X && point.X < b.X + b.Width &&
                point.Y >= b.Y && point.Y < b.Y + b.Height)
            {
                return display;
            }
        }

        DisplayInfo nearest = displays[0];
        float bestDistance = float.MaxValue;

        foreach (DisplayInfo display in displays)
        {
            Rectangle b = display.Bounds;
            float dx = point.X - (b.X + b.Width / 2f);
            float dy = point.Y - (b.Y + b.Height / 2f);
            float distance = dx * dx + dy * dy;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = display;
            }
        }

        return nearest;
    }

    /// <summary>A stand-in until a platform provider is registered.</summary>
    private sealed class SingleDisplayProvider : IDisplayProvider
    {
        public static readonly IReadOnlyList<DisplayInfo> Displays =
        [
            new DisplayInfo
            {
                Bounds = new Rectangle(Point.Empty, new Size(1920, 1080)),
                WorkingArea = new Rectangle(Point.Empty, new Size(1920, 1040)),
                Scale = 1f,
                Dpi = 96f,
                IsPrimary = true,
                Name = "Default",
            },
        ];

        public IReadOnlyList<DisplayInfo> GetDisplays() => Displays;
    }
}