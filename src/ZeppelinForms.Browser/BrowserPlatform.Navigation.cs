using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;

namespace ZeppelinForms.Browser;

/// <summary>The browser's back button and the page's safe area.</summary>
/// <remarks>
/// <para>
/// A <see cref="Forms.Controls.Navigation.NavigationHost"/> pushes an entry into the page's
/// history for each page it pushes, with its route after the <c>#</c>: the back button
/// gives a <c>popstate</c>, and the form on top goes back — <see cref="Form.RequestBack"/>
/// with <see cref="BackSource.Browser"/>. The stack's own backs go back in the history too,
/// and zf.js keeps the <c>popstate</c> they cause to itself.
/// </para>
/// <para>
/// The address the page was opened at is read once: a stack with routes builds itself by
/// it, so a link to a page deep in the application opens that page.
/// </para>
/// </remarks>
public sealed partial class BrowserPlatform
{
    private bool _initialRouteTaken;

    /// <summary>The route of the address the page was opened at, once.</summary>
    internal string? TakeInitialRoute()
    {
        if (_initialRouteTaken) return null;

        _initialRouteTaken = true;

        string route = Interop.InitialRoute();
        return route.Length > 0 ? route : null;
    }

    /// <summary>The browser's back button: the history went one entry back.</summary>
    internal void HandleHistoryBack() => InputTarget()?.Form.RequestBack(BackSource.Browser);

    /// <summary>The page's <c>env(safe-area-inset-*)</c>, in CSS pixels — the form's own.</summary>
    internal void HandleSafeArea(double left, double top, double right, double bottom) =>
        Root?.Form.SetSafeAreaInsets(new Thickness((float)left, (float)top, (float)right, (float)bottom));
}
