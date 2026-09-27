namespace ZeppelinForms.Input.Pointer;

/// <summary>Why the interaction was cut off.</summary>
public enum PointerCancelReason
{
    /// <summary>The system capture was taken away: another window, Alt+Tab,
    /// screen lock. Before 0.11 a fake MouseUp was sent here.</summary>
    CaptureLost,

    /// <summary>The element that held the contact left the tree.</summary>
    Detached,

    /// <summary>Another participant won the contact — a gesture recognizer
    /// on an ancestor. The compatibility mouse events of the contact are
    /// cancelled, the contact itself goes on under the winner.
    /// Sent by GestureArena when a gesture accepts the contact.</summary>
    GestureWon,

    /// <summary>The platform sent the cancel: pointercancel in the browser,
    /// ACTION_CANCEL on Android.</summary>
    Platform,
}