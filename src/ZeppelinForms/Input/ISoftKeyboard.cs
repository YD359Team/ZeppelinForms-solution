namespace ZeppelinForms;

/// <summary>Which layout to ask of the on-screen keyboard.</summary>
public enum SoftKeyboardKind
{
    Text,
    Number,
    Decimal,
    Email,
    Phone,
    Url,
    Password,
}

/// <summary>A window that can show the on-screen keyboard.</summary>
/// <remarks>
/// A separate interface rather than methods in IPlatformWindow, for the same
/// reason IDesktopWindow was split out in 0.9: four desktop backends would get
/// two methods with nothing to fill them with except an empty body — and an
/// empty body lies, because it is indistinguishable from "shown".
/// Checked by a cast: if (PlatformWindow is ISoftKeyboard keyboard).
/// </remarks>
public interface ISoftKeyboard
{
    void ShowSoftKeyboard(SoftKeyboardKind kind);

    void HideSoftKeyboard();
}