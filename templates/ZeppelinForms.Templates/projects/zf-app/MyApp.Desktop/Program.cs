using ZeppelinForms;
using ZeppelinForms.Linux;
using ZeppelinForms.Windows;

// ZfApp, not App: in a project named Contoso.App the namespace Contoso.App would be found
// first, and App.Theme would be looked for in it
using ZfApp = ZeppelinForms.App;

namespace MyApp.Desktop;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        Startup.Configure();

        IPlatform platform = OperatingSystem.IsWindows() ? new WindowsPlatform()
            : OperatingSystem.IsLinux() ? LinuxPlatform.Create()
            : throw new PlatformNotSupportedException("ZeppelinForms runs on Windows and Linux on a desktop.");

        ZfApp app = new(platform) { MainForm = new MainForm() };
        app.Run();
    }
}
