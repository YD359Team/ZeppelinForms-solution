using System.Reflection;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;

namespace ZeppelinForms;

public class App
{
    public required Form MainForm { get; init; }
    public Icon? Icon { get; init; }

    private readonly IPlatform _platform;

    public App(IPlatform platform)
    {
        _platform = platform;
    }

    public void Run()
    {
        this.MainForm.Icon ??= Icon.FromStream(
            typeof(App).Assembly.GetManifestResourceStream(
                "ZeppelinForms.Resources.ZF.ico")!);

        IPlatformWindow mainWnd =
            _platform.CreateWindow(this.MainForm);

        mainWnd.Show();

        _platform.Run();
    }
}

public interface IPlatform
{
    IPlatformWindow CreateWindow(Form form);
    void Run();
    void Exit();
}

public interface IPlatformWindow
{
    void Show();
    void Close();
    void SetTitle(string? title);
    void SetBounds(Rectangle bounds);
}
