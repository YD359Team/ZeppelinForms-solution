using System.Collections.ObjectModel;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Core.Collections;

public static class ObservableCollectionUIElementEx
{
    extension(ObservableCollection<UIElement> oc)
    {
        public void Add(UIElement item, int row, int column)
        {
            item.Row = row;
            item.Column = column;
            oc.Add(item);
        }
    }
}
