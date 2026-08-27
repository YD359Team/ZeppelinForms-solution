using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Control with any count of children
/// </summary>
public abstract class PanelControl : UIElement
{
    public ObservableCollection<UIElement> Children { get; } = [];

    public PanelControl()
    {
        Children.CollectionChanged += Children_CollectionChanged;
    }

    private void Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Form? owner = FindOwner();

        if (e.OldItems is not null)
            foreach (UIElement item in e.OldItems)
            {
                owner?.DetachTree(item);
                item.Parent = null;
            }

        if (e.NewItems is not null)
            foreach (UIElement item in e.NewItems)
            {
                item.Parent = this;
                owner?.AttachTree(item);
            }

        Invalidate();
    }
}
