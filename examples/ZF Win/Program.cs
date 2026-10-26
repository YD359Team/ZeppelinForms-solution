using ZeppelinForms;
using ZeppelinForms.Theming;
using ZeppelinForms.Windows;
using ZF_SharedLib;
using ZF_SharedLib.Showcase;

namespace ZF_Win;

public class Program
{
    [STAThread]
    static void Main()
    {
        // the showcase's native control here: a Win32 multi-line edit
        // (WS_BORDER | WS_VSCROLL | ES_MULTILINE | ES_AUTOVSCROLL | ES_WANTRETURN)
        NativePage.Sample = () => new Win32ControlHost
        {
            ClassName = "EDIT",
            Style = 0x00800000 | 0x00200000 | 0x0004 | 0x0040 | 0x1000,
            WindowText = "A Win32 EDIT control, hosted by an HwndHost.\r\nType here: the keyboard is the control's.",
        };
        WindowsPlatform windowsPlatform = new();
        App myApp = new(windowsPlatform)
        {
            MainForm = new ExampleMainForm(),
            StartupTheme = Themes.FluentLight,
        };
        myApp.Run();
    }
}
