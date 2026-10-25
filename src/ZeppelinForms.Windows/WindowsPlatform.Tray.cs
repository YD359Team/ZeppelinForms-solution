using ZeppelinForms.Forms;

namespace ZeppelinForms.Windows;

/// <summary>The notification area of the taskbar: every <see cref="TrayIcon"/> is a
/// Shell_NotifyIcon icon with a hidden window of its own.</summary>
public partial class WindowsPlatform : ITrayPlatform
{
    ITrayIconImpl? ITrayPlatform.CreateTrayIcon(TrayIcon owner) => new Win32TrayIcon(owner);
}