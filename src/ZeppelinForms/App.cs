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

    /// <summary>Текущая тема. Смена применяется ко всем открытым формам.</summary>
    public static Theme Theme
    {
        get => _theme;
        set
        {
            if (ReferenceEquals(_theme, value)) return;

            _theme = value;

            // путь к файлу шрифта — свойство платформы, а не оформления:
            // в браузере системных шрифтов нет, и тема не должна отбирать
            // уже загруженный файл. Свой путь тема, конечно, вправе задать
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
        this.MainForm.Icon ??= Icon.FromStream(
            typeof(App).Assembly.GetManifestResourceStream(
                "ZeppelinForms.Resources.ZF.ico")!);

        IPlatformWindow window = _platform.CreateWindow(this.MainForm);

        // продолжения await должны возвращаться в поток UI: на этом держится
        // ShowDialogAsync и вообще весь async-код в обработчиках
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
                form.PlatformWindow?.Frames.Stop();
        };

        lifecycle.Resumed += (_, _) =>
        {
            foreach (Form form in Form.OpenForms)
                form.ResumeFrames();
        };
    }
}
