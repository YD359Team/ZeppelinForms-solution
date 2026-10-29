namespace ZeppelinForms;

public partial class App
{
    /// <summary>The code runs in the previewer's host process, not in an application:
    /// a <see cref="Design.PreviewSetupAttribute"/> method, a preview, a form built for
    /// the IDE. Set by the host before it loads the project; never changes after.</summary>
    /// <remarks>
    /// <para>
    /// What the framework does differently there: a mistake the application wants to
    /// hear about loudly — a style sheet path that leads nowhere — is reported in place
    /// instead of thrown. The previewer's process has a working folder of its own, so
    /// a relative path that is right for the application may be wrong for it, and a
    /// preview that throws shows nothing but its exception.
    /// </para>
    /// <para>
    /// The application may read it too, as WinForms' DesignMode: to skip in the
    /// preview what a design machine can't do — open a database, start a service in
    /// a form's constructor.
    /// </para>
    /// </remarks>
    public static bool IsDesignMode { get; internal set; }
}