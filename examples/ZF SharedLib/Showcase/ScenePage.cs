using System.Globalization;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>A 3D viewport: a cube on a grid, turned with the mouse as in a 3D editor.</summary>
internal static class ScenePage
{
    public static UIElement Create()
    {
        var scene = new SceneView { Size = new(float.NaN, 460) };

        Label status = Demo.Note(Describe(scene));

        var perspective = new CheckBox { Text = "Perspective", IsChecked = true };
        perspective.CheckedChanged += (_, _) => scene.IsPerspective = perspective.IsChecked;

        var turntable = new CheckBox { Text = "Turntable" };
        turntable.CheckedChanged += (_, _) => scene.IsTurntable = turntable.IsChecked;

        // numpad 5 switches the projection in the view itself: the check box follows it
        scene.ViewChanged += (_, _) =>
        {
            status.Text = Describe(scene);
            perspective.IsChecked = scene.IsPerspective;
        };

        Button View(string text, float yaw, float pitch)
        {
            var button = new Button { Text = text };
            button.Click += (_, _) => scene.SnapTo(yaw, pitch);
            return button;
        }

        var reset = new Button { Text = "Reset view" };
        reset.Click += (_, _) => scene.ResetView();

        var content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
        };

        content.Children.AddRange([
            scene,
            Demo.Row(View("Front", 0, 0), View("Right", 90, 0), View("Top", 0, 90), reset, perspective, turntable),
            status,
        ]);

        return Demo.Page(
            "3D scene",
            "A cube on a ground grid with axes, drawn with the 2D graphics alone: projected on the " +
            "CPU and filled as polygons, without a GPU or a depth buffer.",
            Demo.Section("Orbit view", content,
                "Drag to orbit; Shift+drag or the right button pans, the wheel zooms. Let go while " +
                "moving and the view keeps turning. Click the cube to select it, double-click to frame it. " +
                "Keys as in Blender: 1, 3, 7 — front, right, top (with Ctrl — the opposite side), " +
                "5 — perspective or orthographic, the arrows orbit, F frames, Home resets. " +
                "A click on an axis in the corner looks along it."));
    }

    private static string Describe(SceneView scene) => string.Format(
        CultureInfo.CurrentCulture,
        "{0} · yaw {1:0}° · pitch {2:0}° · distance {3:0.0}",
        scene.ViewName, scene.Yaw, scene.Pitch, scene.Distance);
}