using ZeppelinForms.Diagnostics;
using ZeppelinForms.Drawing;
using ZeppelinForms.Headless;
using ZeppelinForms.Skia;
using ZeppelinForms.Theming;

namespace ZeppelinForms.UnitTests.Snapshots;

/// <summary>A headless platform, but with real Skia rendering and measuring.</summary>
public sealed class SnapshotFixture
{
    public SnapshotFixture()
    {
        ZfContract.Behavior = ContractViolationBehavior.Throw;

        SkiaTextMeasurer.Register();
        SkiaImageDecoder.Register();
        SkiaOffscreenRenderer.Register();

        App.Theme = Themes.Light;
        Font.Default = SnapshotAssert.TestFont;
    }

    public HeadlessPlatform CreatePlatform() => new();
}