using System.Collections;
using Essenthos.Core.Bhsa;

namespace Essenthos.Core.TextFabric;

public class Document<T> : IDocument<T>
{
    private readonly IDictionary<int, T> _items;
    private readonly IList<(RangeInt, T)> _rangeItems;

    internal Document(string name, DocumentMetadata metadata, IDictionary<int, T> items,
        IList<(RangeInt, T)> rangeItems)
    {
        _items = items;
        _rangeItems = rangeItems;
        Name = name;
        Metadata = metadata;
    }

    public string Name { get; }

    public DocumentMetadata Metadata { get; }

    public int Count => _items.Count;

    public T[] Values
    {
        get
        {
            var values = new T[_items.Count + _rangeItems.Count];
            var index = 0;
            foreach (var item in _items.Values)
            {
                values[index++] = item;
            }

            foreach (var item in _rangeItems)
            {
                values[index++] = item.Item2;
            }

            return values;
        }
    }

    public T this[int index]
    {
        get
        {
            if (_items.TryGetValue(index, out var value))
            {
                return value;
            }

            for (int i = 0, count = _rangeItems.Count; i < count; i++)
            {
                (RangeInt Range, T Value) item = _rangeItems[i];
                if (item.Range.Contains(index))
                {
                    return item.Value;
                }
            }

            throw new KeyNotFoundException($"Key {index} not found in document '{Name}'.");
        }
    }

    public T? GetNullable(int index)
    {
        if (_items.TryGetValue(index, out var value))
        {
            return value;
        }

        for (int i = 0, count = _rangeItems.Count; i < count; i++)
        {
            (RangeInt Range, T Value) item = _rangeItems[i];
            if (item.Range.Contains(index))
            {
                return item.Value;
            }
        }

        return default;
    }

    public RangeInt? GetValueRange(T value)
    {
        foreach (var (range, rValue) in _rangeItems)
        {
            if (EqualityComparer<T>.Default.Equals(value, rValue))
            {
                return range;
            }
        }

        foreach (var kvp in _items)
        {
            if (EqualityComparer<T>.Default.Equals(kvp.Value, value))
            {
                return new RangeInt(kvp.Key, kvp.Key);
            }
        }

        return null;
    }

    public bool ContainsKey(int key)
    {
        if (_items.ContainsKey(key))
        {
            return true;
        }

        for (int i = 0, count = _rangeItems.Count; i < count; i++)
        {
            (RangeInt Range, T Value) item = _rangeItems[i];
            if (item.Range.Contains(key))
            {
                return true;
            }
        }

        return false;
    }

    IEnumerator<KeyValuePair<int, T>> IEnumerable<KeyValuePair<int, T>>.GetEnumerator()
    {
        foreach (var kvp in _items)
        {
            yield return kvp;
        }

        for (int i = 0, count = _rangeItems.Count; i < count; i++)
        {
            var (range, value) = _rangeItems[i];
            for (var j = range.Start; j <= range.End; j++)
            {
                yield return new KeyValuePair<int, T>(j, value);
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<KeyValuePair<int, T>>)this).GetEnumerator();
    }
}