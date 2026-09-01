namespace Essenthos.Core.TextFabric;

public interface IDocument
{
    string Name { get; }

    DocumentMetadata Metadata { get; }

    int Count { get; }

    object[] Values { get; }

    object this[int index] { get; }

    object? GetNullable(int index);
}

public interface IDocument<T> : IDocument, IEnumerable<KeyValuePair<int, T>>
{
    new T[] Values { get; }

    object[] IDocument.Values => Values.Cast<object>().ToArray();

    new T this[int index] { get; }

    object IDocument.this[int index] => this[index]!;

    new T? GetNullable(int index);

    object? IDocument.GetNullable(int index) => GetNullable(index);

    bool ContainsKey(int index);
}