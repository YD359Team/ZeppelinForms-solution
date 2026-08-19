using System;
using System.Collections.Generic;
using System.Text;

namespace ZeppelinForms.Core.Collections;

internal class RingList<T>
{
    private T[] _arr;
    private int _index;
    private int _length;

    public RingList()
    {
        _arr = [];
        _length = 0;
    }

    public RingList(params T[] values)
    {
        _arr = [..values];
        _length = _arr.Length;
    }

    public void Add(T item)
    {
        Array.Resize(ref _arr, _length + 1);
        _arr[^1] = item;
        _length++;
    }

    public void Remove(T item)
    {
        int itemIndex = _arr.IndexOf(item);
        if (itemIndex == -1) throw new ArgumentException(nameof(item));

        if (itemIndex == 0)
        {
            _arr = _arr[1..];
        }
        else if (itemIndex == _length - 1)
        {
            _arr = _arr[..^1];
        }
        else
        {
            T[] newArr = new T[_length - 1];
            Array.Copy(_arr, 0, newArr, 0, itemIndex - 1);
            Array.Copy(_arr, itemIndex, newArr, itemIndex, _length - itemIndex);
        }
        _length--;
    }
}
