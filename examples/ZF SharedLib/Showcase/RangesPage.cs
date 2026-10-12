using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>Values on a scale: sliders, a range, progress and indicators of waiting.</summary>
internal static class RangesPage
{
    public static UIElement Create()
    {
        // ===== sliders drive the progress =====

        var bar = new ProgressBar { Maximum = 100, Value = 40, ShowPercentage = true, Size = new(320, float.NaN) };
        var ring = new CircularProgressBar { Maximum = 100, Value = 40, ShowPercentage = true, Size = new(96, 96) };

        var volume = new TrackBar { Maximum = 100, Value = 40, Step = 1, Size = new(320, float.NaN) };
        volume.ValueChanged += (_, _) =>
        {
            bar.Value = volume.Value;
            ring.Value = volume.Value;
        };

        var vertical = new TrackBar { Maximum = 10, Value = 7, Step = 1, Orientation = Orientation.Vertical, Size = new(float.NaN, 140) };

        var sliders = Demo.Row(
            Demo.Column(Demo.Labeled("TrackBar drives both progress bars", volume), bar),
            ring,
            Demo.Labeled("Vertical", vertical));

        // ===== a range =====

        Label price = Demo.Note("From 200 to 700");
        var range = new RangeSlider { Minimum = 0, Maximum = 1000, Step = 50, SnapToStep = true, MinimumGap = 100, Size = new(360, float.NaN) };
        range.SetRange(200, 700);
        range.RangeChanged += (_, _) => price.Text = $"From {range.LowerValue:0} to {range.UpperValue:0}";

        // ===== waiting =====

        var loaders = new[]
        {
            new Loader { Style = LoaderStyle.Ring },
            new Loader { Style = LoaderStyle.Spinner },
            new Loader { Style = LoaderStyle.Dots, IndicatorSize = 48f },
            new Loader { Style = LoaderStyle.Bar, Size = new(200, float.NaN) },
        };

        var toggle = new Button { Text = "Stop all" };
        toggle.Click += (_, _) =>
        {
            bool running = !loaders[0].IsRunning;

            foreach (Loader loader in loaders)
                loader.IsRunning = running;

            toggle.Text = running ? "Stop all" : "Start all";
        };

        var waiting = Demo.Column(
            Demo.Row(
                Demo.Labeled("Ring", loaders[0]),
                Demo.Labeled("Spinner", loaders[1]),
                Demo.Labeled("Dots", loaders[2]),
                Demo.Labeled("Bar", loaders[3])),
            toggle);

        return Demo.Page("Ranges and progress", "Sliders, a two-thumb range, progress bars and loaders.",
            Demo.Section("Sliders and progress", sliders, "Arrows, Page Up/Down, Home and End move a focused slider"),
            Demo.Section("Range", Demo.Column(range, price), "Two thumbs that keep MinimumGap between them and snap to Step"),
            Demo.Section("Waiting", waiting, "A loader that is not on screen does not animate: it costs nothing while hidden"));
    }
}
