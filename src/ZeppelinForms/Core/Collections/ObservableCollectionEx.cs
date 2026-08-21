using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace ZeppelinForms.Core.Collections;

internal static class ObservableCollectionEx
{
    extension<T> (ObservableCollection<T> oc)
    {
        public void AddRange(IEnumerable<T> items)
        {
            foreach (T item in items)
            {
                oc.Add(item);
            }
        }
    }
}
