namespace ZeppelinForms.Forms.Enums;

/// <summary>Why a form is closing — passed to <see cref="Form.Closing"/>.</summary>
public enum CloseReason
{
    /// <summary>The user closed the window: the close button of the title bar,
    /// Alt+F4, the window menu, the taskbar.</summary>
    UserClosing,

    /// <summary>The application closed it: <see cref="Form.Close"/>,
    /// <see cref="Form.Accept"/>, <see cref="Form.Cancel"/>.</summary>
    Code,
}