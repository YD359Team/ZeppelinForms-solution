using ZeppelinForms;
using ZeppelinForms.Linux;
using ZeppelinForms.Windows;

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

        App app = new(platform) { MainForm = new MainForm() };
        app.Run();
    }
}
