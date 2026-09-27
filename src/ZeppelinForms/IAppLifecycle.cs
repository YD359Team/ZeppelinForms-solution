namespace ZeppelinForms;

public interface IAppLifecycle
{
    /// <summary>The application goes to the background. This is where animations
    /// are stopped and what is expensive to hold is released.</summary>
    event EventHandler? Paused;

    event EventHandler? Resumed;

    /// <summary>The last chance to save the state.
    /// On Android the application may be killed after this without warning.</summary>
    event EventHandler? Saving;
}