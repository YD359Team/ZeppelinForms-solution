using ZeppelinForms.Animation;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Theming;

namespace ZeppelinForms.Forms;

/// <summary>The theme of the form: applied to its tree on a change, and switched with a
/// ripple from a point.</summary>
public partial class Form
{
    private Point _themeRippleOrigin;
    private float _themeRippleRadius;
    private Color _themeRippleColor;
    private bool _themeRippleActive;

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (Content is not null)
            ApplyTheme(Content);

        // overlays live separately from Content: without this an open inspector,
        // menu or flyout stays in the old theme until it is closed
        foreach (UIElement overlay in _overlays.ToArray())
            ApplyTheme(overlay);

        // the theme changes Font.Default as well, and it affects the size of
        // everything that didn't set a font itself. Such an element doesn't go
        // through its properties, so the measure cache is reset for the whole tree
        InvalidateMeasureTree();

        // a title bar that follows the app follows its theme: the window used to
        // subscribe to the theme itself, on Windows only
        ApplyTitleBar();

        Invalidate();
    }

    /// <summary>Style a subtree according to the current theme. Called on
    /// attaching to the form and on a theme change.</summary>
    internal static void ApplyTheme(UIElement root)
    {
        Walk(root, App.Theme.Apply);
    }

    /// <summary>Switch the theme with a ripple spreading from a point.</summary>
    public void SwitchTheme(Theme theme, Point origin)
    {
        // the ripple is decoration: with reduced motion the theme changes at once
        if (Motion.IsReduced)
        {
            App.Theme = theme;
            return;
        }

        _themeRippleOrigin = origin;
        _themeRippleColor = theme.Colors.Background;
        _themeRippleActive = true;

        float maxRadius = MathF.Sqrt(
            ClientSize.Width * ClientSize.Width + ClientSize.Height * ClientSize.Height);

        // the theme is applied at once, and the ripple covers the moment of repainting
        App.Theme = theme;

        var animation = new Animation<float>(
            this, "theme-ripple", 0f, maxRadius, TimeSpan.FromMilliseconds(450),
            Interpolators.Float,
            value => { _themeRippleRadius = value; InvalidateVisual(); },
            Easing.EaseOut,
            completed: () => { _themeRippleActive = false; InvalidateVisual(); });

        AddAnimation(animation);
    }

    internal (bool Active, Point Origin, float Radius, Color Color) ThemeRipple =>
        (_themeRippleActive, _themeRippleOrigin, _themeRippleRadius, _themeRippleColor);
}
