using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Headless;

/// <summary>A tray without a screen: it remembers what the icons show and plays the
/// user's clicks — checked in tests.</summary>
public sealed partial class HeadlessPlatform : ITrayPlatform
{
    internal List<HeadlessTrayIcon> TrayIcons { get; } = [];

    ITrayIconImpl? ITrayPlatform.CreateTrayIcon(TrayIcon owner)
    {
        var icon = new HeadlessTrayIcon(this, owner);
        TrayIcons.Add(icon);
        return icon;
    }
}

/// <summary>A headless tray icon: what the native one would show, and the user's
/// clicks and menu choices played back.</summary>
internal sealed class HeadlessTrayIcon(HeadlessPlatform platform, TrayIcon owner) : ITrayIconImpl
{
    public Icon Icon { get; private set; } = owner.EffectiveIcon;
    public string? ToolTip { get; private set; } = owner.ToolTip;
    public int UpdateCount { get; private set; }
    public bool IsDisposed { get; private set; }

    public void Update()
    {
        Icon = owner.EffectiveIcon;
        ToolTip = owner.ToolTip;
        UpdateCount++;
    }

    public void Dispose()
    {
        IsDisposed = true;
        platform.TrayIcons.Remove(this);
    }

    public void Click() => owner.RaiseClick();

    public void DoubleClick() => owner.RaiseDoubleClick();

    /// <summary>Open the menu and choose the item at the path of indices — a submenu's
    /// item is (2, 0). False — disabled, a separator, or no such item.</summary>
    public bool Choose(params int[] path)
    {
        owner.RaiseMenuOpening();

        List<MenuItem> items = owner.Menu;
        MenuItem? item = null;

        foreach (int index in path)
        {
            if (index < 0 || index >= items.Count) return false;

            // a submenu opens on the way, with its Opening, as in the native menus
            item?.RaiseOpening();

            item = items[index];
            items = item.Items;
        }

        return item is not null && item.Invoke();
    }
}