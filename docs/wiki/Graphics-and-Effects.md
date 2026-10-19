# Graphics and effects

ZeppelinForms draws through an abstract `Graphics`; `ZeppelinForms.Skia` implements it with
SkiaSharp on every platform, hardware-accelerated where it can be. The framework itself knows
nothing of Skia, so another graphics stack can be plugged in.

## Drawing in a control

```csharp
protected override void DrawContent(Graphics g)
{
    Rectangle box = ContentBounds;

    g.FillRoundRectangle(box, new CornerRadius(6), Fill);
    g.DrawText(Caption, box, TextColor, EffectiveFont,
        HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
}
```

`Graphics` has rectangles, rounded rectangles, ellipses, arcs, lines, polylines, polygons,
pies, SVG path data (`DrawSvgPath`), images, gradients, shadows, text and text runs, clips,
transforms and layers. A control that needs more draws with Skia itself: `g is SkiaGraphics
skia` gives `skia.Canvas`.

## Element-wide looks

Every element, with its descendants:

| Property | |
| -------- | - |
| `Opacity` | 0 — invisible, 1 — opaque |
| `BoxShadow` | A CSS-like shadow |
| `CornerRadius`, `Background`, `BorderColor`, `BorderWidth` | The decoration of `Decorated*` controls |
| `TranslateX/Y`, `ScaleX/Y`, `Rotation` | Transforms around the center; clicks follow them |
| `Effects` | A chain of visual effects |
| `BlendMode` | How the element is put over what is under it |

All of them are styled: a style sheet or a transition can set them.

## Effects

```csharp
glass.Effects.Add(new AcrylicEffect { /* blur, tint, noise */ });
caption.Effects.Add(new BlurEffect(3f));
picture.Effects.Add(new ReflectionEffect { /* height, gap, fade */ });
logo.Glitch();          // and logo.StopGlitch()
```

`AcrylicEffect` blurs what is under the element, `BlurEffect` the element itself,
`ReflectionEffect` mirrors it under itself, `TransformEffect` skews and turns it.

## Blend modes

`BlendMode` puts an element over what is drawn under it as Avalonia's `BitmapBlendingMode`
and CSS `mix-blend-mode` do:

```csharp
var tint = new Panel
{
    Background = new Color(255, 0xE5, 0x39, 0x35),
    BlendMode = BlendMode.Multiply,
};
```

| Group | Modes |
| ----- | ----- |
| Separable | `Multiply`, `Screen`, `Overlay`, `Darken`, `Lighten`, `ColorDodge`, `ColorBurn`, `HardLight`, `SoftLight`, `Difference`, `Exclusion`, `Plus` |
| Non-separable | `Hue`, `Saturation`, `Color`, `Luminosity` |
| Porter–Duff | `Source`, `Destination`, `DestinationOver`, `SourceIn`, `DestinationIn`, `SourceOut`, `DestinationOut`, `SourceAtop`, `DestinationAtop`, `Xor` |

An element with a mode other than `Normal` is drawn into a layer of its own, which costs what
an opacity below 1 costs. What it blends with ends at the nearest ancestor with a layer of its
own — an opacity below 1, a mode, an effect — as CSS isolates a stacking context.
`DestinationOut` cuts the element's shape out of what is under it; `DestinationIn` makes it a
mask.

In a control's own drawing, `g.DrawBlendedImage(rect, image, BlendMode.Screen)` draws one
picture in a mode, and `g.SaveLayer(opacity, mode)` … `g.Restore()` anything. A backend
without blend modes draws as `Normal` rather than not at all.

## Images

```csharp
var logo = new PictureBox { Layout = ImageLayout.Zoom };
logo.LoadAsset("logo.png");                  // Assets/logo.png next to the executable

Image photo = Image.LoadFromFile(path);
Image remote = await Image.LoadFromUriAsync(uri);
```

PNG, JPEG, WebP, GIF and BMP are read. SVG files are not: vector icons are SVG path data —
`SvgIcon { PathData = "…" }`, `IconSource.FromPath("…")`.

## Fonts

```csharp
Font.Default = new Font("Inter", 14).WithFile("Assets/Inter-Regular.ttf");
label.Font = Font.Default.WithSize(20).WithWeight(FontWeight.SemiBold);
```

`Font.Default` is the font of everything that sets none; `Form.Font` overrides it for one
form. `TextStyle` (`Title`, `Subtitle`, `Body`, `Caption`…) picks a size and weight from the
theme's type ramp.

### Characters the font lacks

When a font has no glyph for a character — Hebrew, Arabic, CJK, emoji in a Latin font — it is
looked for in **`FontFallbacks`** first, in the order added, then in the system's fonts:

```csharp
FontFallbacks.Add("Assets/NotoSansHebrew-Regular.ttf");
FontFallbacks.Add("Assets/NotoSansJP-Regular.otf");
```

On a desktop this makes text look the same on every machine. In the browser there are no
system fonts at all, so the fallbacks are the only way to show such text — see
[The browser](Browser#fonts).
