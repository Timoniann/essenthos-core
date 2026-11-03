namespace Essenthos.Core.Products.Essx;

/// <summary>
/// The binary format as a product format: a file of one table named <see cref="TableName"/>. The
/// machine products are written in it; a file of several tables is written with
/// <see cref="EssxWriter"/> directly.
/// </summary>
public sealed class EssxFormat : IProductFormat
{
    public const string TableName = "rows";

    public static readonly EssxFormat Instance = new();

    public static readonly EssxFormat Compressed = new(new EssxOptions { Codec = EssxLayout.CodecDeflate });

    private readonly EssxOptions _options;

    public EssxFormat(EssxOptions? options = null)
    {
        _options = options ?? EssxOptions.Default;
    }

    public string Extension => EssxLayout.Extension;

    public IProductWriter<T> Create<T>(string path, ProductHeader header, ProductSchema<T> schema) =>
        new SingleTableWriter<T>(EssxWriter.Create(path, header, _options), schema);

    public IProductReader<T> Open<T>(string path, ProductSchema<T> schema) =>
        new SingleTableReader<T>(EssxReader.Open(path), row => schema.Read(row), schema);

    public IProductReader<string?[]> Open(string path) =>
        new SingleTableReader<string?[]>(EssxReader.Open(path), row => row.ToTexts(), null);

    private sealed class SingleTableWriter<T> : IProductWriter<T>
    {
        private readonly EssxWriter _file;
        private readonly EssxTableWriter<T> _table;

        public SingleTableWriter(EssxWriter file, ProductSchema<T> schema)
        {
            _file = file;
            _table = file.Table(TableName, schema);
        }

        public string Path => _file.Path;

        public long Rows => _table.Rows;

        public void Write(T row) => _table.Write(row);

        public void Complete() => _file.Complete();

        public void Dispose() => _file.Dispose();
    }

    private sealed class SingleTableReader<T> : IProductReader<T>
    {
        private readonly EssxReader _file;
        private readonly EssxTable _table;
        private readonly Func<EssxRow, T> _read;

        public SingleTableReader(EssxReader file, Func<EssxRow, T> read, ProductSchema<T>? schema)
        {
            _file = file;
            _read = read;
            try
            {
                _table = file.Tables.Count == 1 ? file.Tables[0] : file.Table(TableName);
                schema?.Check([.. _table.Columns.Select(column => column.Name)], file.Path);
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }

        public string Path => _file.Path;

        public ProductHeader? Header => _file.Header;

        public IReadOnlyList<Column> Columns => _table.Columns;

        public IEnumerable<T> Rows() => _table.Rows().Select(_read);

        public void Dispose() => _file.Dispose();
    }
}
