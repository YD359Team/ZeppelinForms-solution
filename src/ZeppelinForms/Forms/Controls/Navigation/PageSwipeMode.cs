namespace ZeppelinForms.Forms.Controls.Navigation;

public enum PageSwipeMode
{
    None,

    /// <summary>A swipe to the right returns to the previous page of the history.
    /// Left does nothing: there is nowhere to go forward in the stack.</summary>
    Back,

    /// <summary>The swipe flips pages in the order they were added.
    /// Each flip is an ordinary transition, that is, the history grows.</summary>
    Sequential,
}