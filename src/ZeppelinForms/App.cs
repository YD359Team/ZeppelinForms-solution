using System.Reflection;
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
            Font.Default = value.BaseFont;

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

        _platform.CreateWindow(this.MainForm);

        this.MainForm.Show();

        _platform.Run();
    }
}
