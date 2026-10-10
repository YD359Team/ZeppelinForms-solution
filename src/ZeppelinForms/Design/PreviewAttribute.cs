namespace ZeppelinForms.Design;

/// <summary>
/// Marks what the previewer shows: a static method without parameters that returns
/// a <see cref="Forms.Controls.Base.UIElement"/> or a <see cref="Forms.Form"/>, or a
/// form class with a constructor without parameters.
/// </summary>
/// <example>
/// <code>
/// public static class ButtonPreviews
/// {
///     [Preview("Primary", Width = 240, Height = 80)]
///     public static UIElement Primary() =&gt; Buttons.Primary("Save");
///
///     [Preview("Primary, dark", Theme = "FluentDark")]
///     public static UIElement PrimaryDark() =&gt; Buttons.Primary("Save");
/// }
/// </code>
/// </example>
/// <remarks>
/// <para>
/// Forms need no attribute: every form of the project with a constructor without
/// parameters is in the previewer's list. The attribute on a form only sets its
/// size, theme and the rest.
/// </para>
/// <para>
/// The attribute is part of the framework itself, so an application marks its
/// previews with the ZeppelinForms package alone. The previewer — the IDE extension
/// and its host process — is never a dependency of the application.
/// </para>
/// <para>
/// The method runs in the previewer's process, not in the application: it should
/// build the view and nothing else — no network, no files the design machine may
/// not have. A preview that throws is shown as its exception.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PreviewAttribute : Attribute
{
    public PreviewAttribute()
    {
    }

    public PreviewAttribute(string name)
    {
        Name = name;
    }

    /// <summary>The name in the previewer's list; the method's or the class's name if null.</summary>
    public string? Name { get; }

    /// <summary>Previews of a group are listed together; the declaring class's name if null.</summary>
    public string? Group { get; set; }

    /// <summary>The width of the preview in device-independent pixels; 0 — the previewer's choice.</summary>
    public float Width { get; set; }

    /// <summary>The height of the preview in device-independent pixels; 0 — the previewer's choice.</summary>
    public float Height { get; set; }

    /// <summary>A theme of <see cref="Theming.Themes"/> by name — "Light", "Dark",
    /// "FluentLight", "FluentDark", "HighContrastBlack", "HighContrastWhite"; null —
    /// the previewer's choice.</summary>
    public string? Theme { get; set; }

    /// <summary>A culture name for <see cref="Core.Globalization.Localization.Culture"/>:
    /// "ru-RU", "ar-SA"; null — the previewer's choice.</summary>
    public string? Culture { get; set; }

    /// <summary>Lay the preview out right to left.</summary>
    public bool RightToLeft { get; set; }

    /// <summary>The text scale, as the system's text size setting gives it; 0 — 1.</summary>
    public float TextScale { get; set; }
}

/// <summary>
/// Marks a static method without parameters the previewer runs once, before the
/// first preview: what the application's <c>Main</c> does before it shows a form —
/// loading style sheets, choosing a theme, registering localized texts. <c>Main</c>
/// itself never runs in the previewer.
/// </summary>
/// <example>
/// <code>
/// public static class Startup
/// {
///     [PreviewSetup]
///     public static void Configure() =&gt; App.Styles.Load("Assets/app.zss", watch: true);
/// }
/// </code>
/// </example>
/// <remarks>A sheet loaded with <c>watch: true</c> keeps following its file in the
/// previewer too: edits of a <c>.zss</c> show up there without a build.</remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class PreviewSetupAttribute : Attribute
{
}