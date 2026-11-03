namespace Essenthos.Core.Products.Esw;

/// <summary>
/// The binary format as a product format: a file of one table named <see cref="TableName"/>. The
/// machine products are written in it; a file of several tables is written with
/// <see cref="EswWriter"/> directly.
/// </summary>
public sealed class EswFormat : IProductFormat
{
    public const string TableName = "rows";

    public static readonly EswFormat Instance = new();

    public static readonly EswFormat Compressed = new(new EswOptions { Codec = EswLayout.CodecDeflate });

    private readonly EswOptions _options;

    public EswFormat(EswOptions? options = null)
    {
        _options = options ?? EswOptions.Default;
    }

    public string Extension => EswLayout.Extension;

    public IProductWriter<T> Create<T>(string path, ProductHeader header, ProductSchema<T> schema) =>
        new SingleTableWriter<T>(EswWriter.Create(path, header, _options), schema);

    public IProductReader<T> Open<T>(string path, ProductSchema<T> schema) =>
        new SingleTableReader<T>(EswReader.Open(path), row => schema.Read(row), schema);

    public IProductReader<string?[]> Open(string path) =>
        new SingleTableReader<string?[]>(EswReader.Open(path), row => row.ToTexts(), null);

    private sealed class SingleTableWriter<T> : IProductWriter<T>
    {
        private readonly EswWriter _file;
        private readonly EswTableWriter<T> _table;

        public SingleTableWriter(EswWriter file, ProductSchema<T> schema)
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
        private readonly EswReader _file;
        private readonly EswTable _table;
        private readonly Func<EswRow, T> _read;

        public SingleTableReader(EswReader file, Func<EswRow, T> read, ProductSchema<T>? schema)
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
