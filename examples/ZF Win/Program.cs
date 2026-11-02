using ZeppelinForms;
using ZeppelinForms.Theming;
using ZeppelinForms.Windows;
using ZF_SharedLib;
using ZF_SharedLib.Showcase;

namespace ZF_Win;

public class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // the showcase's native control here: a Win32 multi-line edit
        // (WS_BORDER | WS_VSCROLL | ES_MULTILINE | ES_AUTOVSCROLL | ES_WANTRETURN)
        NativePage.Sample = () => new Win32ControlHost
        {
            ClassName = "EDIT",
            Style = 0x00800000 | 0x00200000 | 0x0004 | 0x0040 | 0x1000,
            WindowText = "A Win32 EDIT control, hosted by an HwndHost.\r\nType here: the keyboard is the control's.",
        };
        // the form is made after the platform, as it always was: the platform's graphics
        // backend registers the image decoder, and the form decodes its pictures as it is made
        WindowsPlatform windowsPlatform = new();
        App myApp = new(windowsPlatform)
        {
            MainForm = new ExampleMainForm(),
            StartupTheme = Themes.FluentLight,
        };

        // started from a task of the jump list: the page it names
        if (args is ["--page", string page, ..] && myApp.MainForm is ExampleMainForm showcase)
            showcase.ShowPage(page);

        // the taskbar button's jump list: a page of the showcase opened straight away
        foreach (string name in new[] { "Docking", "Charts", "Settings" })
            App.JumpList.Tasks.Add(new JumpTask(name, $"--page \"{name}\"") { Description = $"Open the showcase on {name}" });

        App.JumpList.Apply();

        myApp.Run();
    }
}