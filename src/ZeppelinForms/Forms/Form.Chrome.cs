using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms;

/// <summary>The window as a desktop object: its frame, its title bar buttons, its
/// place in the taskbar. The browser and Android have none of these and ignore them.</summary>
/// <remarks>Each of them can be changed while the window is open: the platform
/// rebuilds the frame at once.</remarks>
public partial class Form
{
    /// <summary>The frame and the title bar, as WinForms has them.
    /// <see cref="FormBorderStyle.None"/> removes both.</summary>
    public FormBorderStyle FormBorderStyle
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            UpdateChrome();
        }
    } = FormBorderStyle.Sizable;

    /// <summary>Whether the title bar has its buttons — minimize, maximize and
    /// close — and the window menu. false leaves only the title, as WinForms'
    /// ControlBox; Alt+F4 still closes the window, and <see cref="Closing"/>
    /// is the way to refuse that.</summary>
    public bool ControlBox
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            UpdateChrome();
        }
    } = true;

    /// <summary>Whether the window has its button in the taskbar.</summary>
    public bool ShowInTaskbar
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            UpdateChrome();
        }
    } = true;

    /// <summary>Whether the user can resize the window: <see cref="CanResize"/>
    /// on a frame that has edges to drag.</summary>
    public bool IsResizable =>
        CanResize && FormBorderStyle is FormBorderStyle.Sizable or FormBorderStyle.SizableToolWindow;

    /// <summary>Whether the title bar shows the minimize button. A tool window and a
    /// window without the control box have none, whatever CanMinimize says.</summary>
    internal bool HasMinimizeBox => CanMinimize && ControlBox && !IsToolWindow;

    internal bool HasMaximizeBox => CanMaximize && ControlBox && !IsToolWindow;

    internal bool IsToolWindow =>
        FormBorderStyle is FormBorderStyle.FixedToolWindow or FormBorderStyle.SizableToolWindow;

    /// <summary>The frame of an open window follows the properties at once.</summary>
    private void UpdateChrome() => DesktopWindow?.UpdateChrome();
}