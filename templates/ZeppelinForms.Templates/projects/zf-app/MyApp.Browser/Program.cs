using ZeppelinForms.Browser;

namespace MyApp.Browser;

public static class Program
{
    public static Task Main()
    {
        Startup.Configure();

        // a browser has no fonts of its own for Skia: the text is drawn with this one,
        // downloaded before the form is made. Pictures and other files the application
        // reads synchronously go into preload: ["/Assets/logo.png"]
        return BrowserApp.RunAsync(() => new MainForm(), font: "/fonts/Inter-Regular.ttf");
    }
}
