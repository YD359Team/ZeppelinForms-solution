using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class DrawSmokeTests
{
    /// <summary>Контролы, которым для отрисовки нужно окружение,
    /// не создаваемое конструктором по умолчанию.</summary>
    private static readonly HashSet<string> Skipped =
    [
        "MapControl",      // полезет за тайлами в сеть
        "GridSplitter",    // осмыслен только внутри Grid
    ];

    public static TheoryData<Type> AllControls
    {
        get
        {
            var data = new TheoryData<Type>();

            foreach (Type type in typeof(UIElement).Assembly.GetTypes())
            {
                if (type.IsAbstract || !type.IsPublic) continue;
                if (!typeof(UIElement).IsAssignableFrom(type)) continue;
                if (type.GetConstructor(Type.EmptyTypes) is null) continue;
                if (Skipped.Contains(type.Name)) continue;

                data.Add(type);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllControls))]
    public void DrawDoesNotThrowTest(Type controlType)
    {
        var control = (UIElement)Activator.CreateInstance(controlType)!;

        var form = new Form { Size = new Size(200, 100), Content = control };
        new HeadlessPlatform(registerServices: true).CreateWindow(form);

        HeadlessElementRenderer.Register();

        // упадёт — значит в Draw/DrawContent этого контрола есть исключение
        ElementRenderer.Current.Render(control, 200, 100);
    }
}