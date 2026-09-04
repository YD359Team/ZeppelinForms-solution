using System.Collections.ObjectModel;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Core.Collections;

public static class ObservableCollectionUIElementEx
{
    extension(ObservableCollection<UIElement> oc)
    {
        public void Add(UIElement item, int row, int column)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(row, 0, nameof(row));
            ArgumentOutOfRangeException.ThrowIfLessThan(column, 0, nameof(column));

            item.Row = row;
            item.Column = column;
            oc.Add(item);
        }
    }
}
