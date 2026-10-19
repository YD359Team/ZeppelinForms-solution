using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.17.0: an element is put over what is under it in a blend mode.</summary>
[Collection("Platform")]
public class BlendModeTests
{
    /// <summary>Records the layers the renderer opens.</summary>
    private sealed class LayerRecorder : HeadlessGraphics
    {
        public List<(float Opacity, BlendMode Mode)> BlendLayers { get; } = [];

        public List<float> OpacityLayers { get; } = [];

        public int DisabledLayers { get; private set; }

        public int Depth { get; private set; }

        public override void SaveLayer(float opacity, BlendMode blendMode)
        {
            if (blendMode == BlendMode.Normal)
            {
                SaveLayer(opacity);
                return;
            }

            BlendLayers.Add((opacity, blendMode));
            Depth++;
        }

        public override void SaveLayer(float opacity)
        {
            OpacityLayers.Add(opacity);
            Depth++;
        }

        public override void SaveDisabledLayer(float opacity, float desaturation)
        {
            DisabledLayers++;
            Depth++;
        }

        public override void Save() => Depth++;

        public override void Restore() => Depth--;
    }

    private static Panel Tile(BlendMode mode, float opacity = 1f) => new()
    {
        Size = new Size(50, 50),
        BlendMode = mode,
        Opacity = opacity,
    };

    private static LayerRecorder Render(Panel content)
    {
        var host = new StackPanel { Children = { content } };
        var form = new Form { Size = new Size(200, 200), Content = host };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        var g = new LayerRecorder();
        ElementTreeRenderer.Draw(host, g);

        // every layer and save the renderer opened, it closed
        Assert.Equal(0, g.Depth);

        return g;
    }

    [Fact]
    public void NormalOpensNoLayer()
    {
        LayerRecorder g = Render(Tile(BlendMode.Normal));

        Assert.Empty(g.BlendLayers);
        Assert.Empty(g.OpacityLayers);
    }

    [Fact]
    public void AModeOpensALayerInIt()
    {
        LayerRecorder g = Render(Tile(BlendMode.Multiply));

        Assert.Equal([(1f, BlendMode.Multiply)], g.BlendLayers);
    }

    [Fact]
    public void TheOpacityRidesOnTheBlendLayer()
    {
        LayerRecorder g = Render(Tile(BlendMode.Screen, opacity: 0.5f));

        Assert.Equal([(0.5f, BlendMode.Screen)], g.BlendLayers);
        Assert.Empty(g.OpacityLayers);
    }

    [Fact]
    public void ADisabledElementIsDimmedInsideItsBlendLayer()
    {
        Panel tile = Tile(BlendMode.Overlay);
        tile.IsEnabled = false;

        LayerRecorder g = Render(tile);

        Assert.Equal([(1f, BlendMode.Overlay)], g.BlendLayers);
        Assert.Equal(1, g.DisabledLayers);
    }

    [Fact]
    public void ABackendWithoutModesKeepsToTheOpacity()
    {
        var g = new PlainGraphics();

        g.SaveLayer(0.25f, BlendMode.Difference);

        Assert.Equal(0.25f, g.Opacity);
    }

    [Fact]
    public void APictureIsDrawnInALayerOfItsMode()
    {
        var g = new LayerRecorder();
        var image = new Image(1, 1, new byte[4]);

        g.DrawImage(new Rectangle(0, 0, 10, 10), image, BlendMode.Multiply);
        g.DrawImage(new Rectangle(0, 0, 10, 10), image, BlendMode.Normal);

        Assert.Equal([(1f, BlendMode.Multiply)], g.BlendLayers);
        Assert.Equal(0, g.Depth);
    }

    [Fact]
    public void AStyleSheetSetsTheMode()
    {
        // not through Tile: a value set in code outranks a style sheet's
        var tile = new Panel { Name = "tile", Size = new Size(50, 50) };

        var form = new Form { Size = new Size(200, 200), Content = tile };
        new HeadlessPlatform().CreateWindow(form);

        StyleSheet sheet = StyleSheet.Parse("#tile { BlendMode: color-burn; }", "blend.zss");
        Assert.Empty(sheet.Diagnostics);
        form.Styles.Add(sheet);

        Assert.Equal(BlendMode.ColorBurn, tile.BlendMode);
    }

    /// <summary>A backend that knows opacity layers only.</summary>
    private sealed class PlainGraphics : HeadlessGraphics
    {
        public float Opacity { get; private set; } = -1;

        public override void SaveLayer(float opacity) => Opacity = opacity;
    }
}
