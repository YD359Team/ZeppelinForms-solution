using System.Reflection;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Forms;
using ZeppelinForms.Theming;

namespace ZeppelinForms;

public class App
{
    public static event EventHandler? ThemeChanged;

    public required Form MainForm { get; init; }
    public Icon? Icon { get; init; }

    private readonly IPlatform _platform;
    private static Theme _theme = Themes.Light;

    /// <summary>The current theme. A change applies to all open forms.</summary>
    public static Theme Theme
    {
        get => _theme;
        set
        {
            if (ReferenceEquals(_theme, value)) return;

            _theme = value;

            // the path to the font file is a property of the platform, not of the
            // styling: in the browser there are no system fonts, and a theme must
            // not take away an already loaded file. A theme is of course entitled
            // to set a path of its own
            Font.Default = value.BaseFont.FilePath is null && Font.Default.FilePath is { } path
                ? value.BaseFont.WithFile(path)
                : value.BaseFont;

            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }
    }


    public App(IPlatform platform)
    {
        _platform = platform;
    }

    public void Run()
    {
        this.MainForm.Icon ??= Assets.Logo;

        IPlatformWindow window = _platform.CreateWindow(this.MainForm);

        // await continuations must come back to the UI thread:
        // ShowDialogAsync and all async code in handlers rely on this
        SynchronizationContext.SetSynchronizationContext(
        new ZfSynchronizationContext(window));

        if (_platform is IAppLifecycle lifecycle)
            AttachLifecycle(lifecycle);

        this.MainForm.Show();

        _platform.Start();
    }

    private static void AttachLifecycle(IAppLifecycle lifecycle)
    {
        lifecycle.Paused += (_, _) =>
        {
            foreach (Form form in Form.OpenForms)
                form.SuspendFrames();
        };

        lifecycle.Resumed += (_, _) =>
        {
            foreach (Form form in Form.OpenForms)
                form.ResumeFrames();
        };
    }
}