namespace ZeppelinForms.Input.Gestures;

public enum GestureState
{
    /// <summary>Hasn't decided yet: watches the contact and waits.</summary>
    Possible,

    /// <summary>Won the contact. The other arena participants dropped out,
    /// the compatibility mouse events of this contact are cancelled.</summary>
    Accepted,

    /// <summary>Dropped out — refused by itself or lost to another.</summary>
    Rejected,
}