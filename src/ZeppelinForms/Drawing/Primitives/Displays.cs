namespace ZeppelinForms.Drawing.Primitives;

public static class Displays
{
    public static IDisplayProvider Current { get; set; } = new SingleDisplayProvider();

    public static IReadOnlyList<DisplayInfo> All => Current.GetDisplays();

    public static DisplayInfo Primary =>
        All.FirstOrDefault(d => d.IsPrimary) ?? All[0];

    /// <summary>Экран, на котором находится точка. Если ни один не содержит —
    /// ближайший по расстоянию до центра.</summary>
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

    /// <summary>Заглушка до регистрации платформенного провайдера.</summary>
    private sealed class SingleDisplayProvider : IDisplayProvider
    {
        public IReadOnlyList<DisplayInfo> GetDisplays() =>
        [
            new DisplayInfo
            {
                Bounds = new Rectangle(Point.Empty, new Size(1920, 1080)),
                WorkingArea = new Rectangle(Point.Empty, new Size(1920, 1040)),
                Scale = 1f,
                IsPrimary = true,
                Name = "Default",
            },
        ];
    }
}