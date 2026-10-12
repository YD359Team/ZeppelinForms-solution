using System.Collections.ObjectModel;

namespace ZeppelinForms.Core.Collections;

public static class ObservableCollectionExtensions
{
    extension<T>(ObservableCollection<T> oc)
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
