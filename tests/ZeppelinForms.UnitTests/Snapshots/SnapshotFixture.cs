using ZeppelinForms.Drawing;
using ZeppelinForms.Headless;
using ZeppelinForms.Skia;
using ZeppelinForms.Theming;

namespace ZeppelinForms.UnitTests.Snapshots;

/// <summary>Headless-платформа, но с настоящим рендером и измерением Skia.</summary>
public sealed class SnapshotFixture
{
    public SnapshotFixture()
    {
        SkiaTextMeasurer.Register();
        SkiaImageDecoder.Register();
        SkiaOffscreenRenderer.Register();

        App.Theme = Themes.Light;
        Font.Default = SnapshotAssert.TestFont;
    }

    public HeadlessPlatform CreatePlatform() => new();
}