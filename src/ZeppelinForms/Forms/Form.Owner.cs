namespace ZeppelinForms.Forms;

/// <summary>A form owned by another: a dialog, a tool palette, a floating panel.</summary>
/// <remarks>
/// <para>
/// An owned window stays above its owner, minimizes with it and closes with it.
/// On Windows that is the owner window of Win32; on X11 the window is transient
/// for its owner, and a dialog says it is one, so the manager keeps it above and
/// centers it. In the browser and on Android every window is a layer over the
/// ones before, so ownership adds only the closing.
/// </para>
/// <para>
/// A closed form leaves its owner: the owner's list must not keep it alive, and a
/// form shown again without an owner is a window of its own.
/// </para>
/// </remarks>
public partial class Form
{
    private readonly List<Form> _ownedForms = [];

    /// <summary>The form this one belongs to, while it is open: set by
    /// <see cref="Show(Form)"/> and by the dialogs. null — a window of its own.</summary>
    public Form? Owner { get; private set; }

    /// <summary>The open forms this one owns.</summary>
    public IReadOnlyList<Form> OwnedForms => _ownedForms;

    /// <summary>Show the form as a window owned by <paramref name="owner"/>, without
    /// blocking: a tool palette, a floating hierarchy, a second view. The window is
    /// created on the owner's platform if the form has none yet.</summary>
    /// <exception cref="ArgumentException">The form would own itself, directly or
    /// through its owners.</exception>
    /// <exception cref="InvalidOperationException">The owner has no window yet.</exception>
    public void Show(Form owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        for (Form? current = owner; current is not null; current = current.Owner)
        {
            if (ReferenceEquals(current, this))
                throw new ArgumentException("A form can't own itself, directly or through its owners.", nameof(owner));
        }

        SetOwner(owner);

        if (PlatformWindow is null || _isClosed)
        {
            IPlatform platform = owner.Platform
                ?? throw new InvalidOperationException("The owner is not bound to a platform yet.");

            Platform = platform;

            // the owner is known before the window exists: Win32 gives it to
            // CreateWindowEx, X11 sets the transient hint before mapping
            platform.CreateWindow(this);
        }
        else
        {
            // an open window gets its new owner through its frame
            UpdateChrome();
        }

        Show();
    }

    private void SetOwner(Form? owner)
    {
        if (ReferenceEquals(Owner, owner)) return;

        Owner?._ownedForms.Remove(this);
        Owner = owner;
        owner?._ownedForms.Add(this);
    }

    /// <summary>The owner is gone: its forms go with it. Their windows close without
    /// asking — there is nobody left to keep them for. On Windows the system has
    /// already destroyed them by now, and the list is empty.</summary>
    private void CloseOwnedForms()
    {
        foreach (Form owned in _ownedForms.ToArray())
            owned.PlatformWindow?.Close();
    }
}