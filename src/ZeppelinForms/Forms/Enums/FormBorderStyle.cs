namespace ZeppelinForms.Forms.Enums;

/// <summary>The window's frame and title bar, as WinForms has them.</summary>
/// <remarks>Only desktop platforms have a frame; the browser and Android ignore it.</remarks>
public enum FormBorderStyle
{
    /// <summary>No frame and no title bar: a splash screen, a window that draws
    /// its own chrome.</summary>
    None,

    /// <summary>A thin frame that can't be dragged to resize.</summary>
    FixedSingle,

    /// <summary>The usual window: a frame that resizes it.</summary>
    Sizable,

    /// <summary>A dialog frame without the icon, not resizable.</summary>
    FixedDialog,

    /// <summary>A tool window: a narrow title bar with only the close button,
    /// not resizable and not shown in the taskbar.</summary>
    FixedToolWindow,

    /// <summary>A resizable tool window.</summary>
    SizableToolWindow,
}