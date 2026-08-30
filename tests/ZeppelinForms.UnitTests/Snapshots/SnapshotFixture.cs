using ZeppelinForms.Drawing;
using ZeppelinForms.Headless;
using ZeppelinForms.Skia;

namespace ZeppelinForms.UnitTests.Snapshots;

/// <summary>Headless-платформа, но с настоящим рендером и измерением Skia.</summary>
public sealed class SnapshotFixture
{
    public SnapshotFixture()
    {
        SkiaTextMeasurer.Register();
        SkiaImageDecoder.Register();
        SkiaOffscreenRenderer.Register();

        // общий шрифт для всех снимков — иначе результат зависит от машины
        Font.Default = SnapshotAssert.TestFont;
    }

    public HeadlessPlatform CreatePlatform() => new();
}