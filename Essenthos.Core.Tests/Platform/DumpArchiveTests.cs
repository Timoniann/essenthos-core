using System.Text;
using Essenthos.Core.Publishing;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>
/// A pg_dump archive written again without an entry that holds no rows: the rest of its table of contents, every
/// table's rows read from where the shorter table of contents now says they are, and no dependency on what went.
/// The archive here is laid out byte for byte as pg_dump's custom format lays it out (version 1.16, uncompressed).
/// </summary>
public sealed class DumpArchiveTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("dump-archive-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void AConstraintLeftOutMovesEveryTablesRowsWithTheTableOfContents()
    {
        var path = Path.Combine(_folder, "whole.dump");
        File.WriteAllBytes(path, Archive(
            new Item(1, "TABLE", "t", "CREATE TABLE public.t (id integer, note text);", 2, []),
            new Item(2, "TABLE DATA", "t", "", 3, ["1"], "COPY public.t (id, note) FROM stdin;\n", "1\tone\n2\t\\N\n\\.\n\n\n"),
            new Item(3, "CONSTRAINT", "t pk_t", "ALTER TABLE ONLY public.t\n    ADD CONSTRAINT pk_t PRIMARY KEY (id);", 4, ["1"]),
            new Item(4, "CONSTRAINT", "t ux_t_note", "ALTER TABLE ONLY public.t\n    ADD CONSTRAINT ux_t_note UNIQUE (note);", 4, ["1"]),
            new Item(5, "INDEX", "ix_t_after", "CREATE INDEX ix_t_after ON public.t USING btree (id);", 4, ["1", "4"]),
            new Item(6, "TABLE", "u", "CREATE TABLE public.u (id integer);", 2, []),
            new Item(7, "TABLE DATA", "u", "", 3, ["6"], "COPY public.u (id) FROM stdin;\n", "7\n\\.\n\n\n")));
        var lean = Path.Combine(_folder, "lean.dump");

        var omitted = DumpArchive.Omit(path, lean, entry => entry.Kind == "CONSTRAINT" && entry.Name.EndsWith(" ux_t_note", StringComparison.Ordinal));

        omitted.Should().Equal("t ux_t_note");
        var archive = DumpArchive.Open(lean);
        archive.Entries.Select(entry => entry.DumpId).Should().Equal(1, 2, 3, 5, 6, 7);
        archive.Entries.Should().NotContain(entry => entry.Definition.Contains("ux_t_note"));
        Rows(archive, "t").Should().Equal("1\tone", "2\t\\N");
        Rows(archive, "u").Should().Equal("7");
        new FileInfo(lean).Length.Should().BeLessThan(new FileInfo(path).Length);
        Dependencies(File.ReadAllBytes(lean), 5).Should().Equal("1");
    }

    [Fact]
    public void AnEntryThatHoldsRowsIsNotLeftOut()
    {
        var path = Path.Combine(_folder, "whole.dump");
        File.WriteAllBytes(path, Archive(
            new Item(1, "TABLE", "t", "CREATE TABLE public.t (id integer);", 2, []),
            new Item(2, "TABLE DATA", "t", "", 3, ["1"], "COPY public.t (id) FROM stdin;\n", "1\n\\.\n\n\n")));

        var leave = () => DumpArchive.Omit(path, Path.Combine(_folder, "lean.dump"), entry => entry.Kind == "TABLE DATA");

        leave.Should().Throw<InvalidOperationException>();
    }

    private static List<string> Rows(DumpArchive archive, string table)
    {
        var rows = new List<string>();
        archive.Rows(table, (_, _) => { }, line => rows.Add(Encoding.UTF8.GetString(line)));
        return rows;
    }

    /// <param name="Copy">The COPY statement of a TABLE DATA entry; its rows follow in <paramref name="Rows"/>.</param>
    private sealed record Item(int Id, string Kind, string Tag, string Definition, int Section, string[] Dependencies, string Copy = "", string? Rows = null);

    private static byte[] Archive(params Item[] items)
    {
        byte[] Toc(IReadOnlyDictionary<int, long> offsets)
        {
            using var toc = new MemoryStream();
            Int(toc, items.Length);
            foreach (var item in items)
            {
                Int(toc, item.Id);
                Int(toc, item.Rows is null ? 0 : 1);
                Str(toc, "0");
                Str(toc, "0");
                Str(toc, item.Tag);
                Str(toc, item.Kind);
                Int(toc, item.Section);
                Str(toc, item.Definition);
                Str(toc, "");
                Str(toc, item.Copy);
                Str(toc, "public");
                Str(toc, "");
                Str(toc, "heap");
                Int(toc, 'r');
                Str(toc, "essenthos");
                Str(toc, "false");
                foreach (var dependency in item.Dependencies)
                {
                    Str(toc, dependency);
                }

                Str(toc, null);
                toc.WriteByte(offsets.TryGetValue(item.Id, out var at) ? (byte)2 : (byte)3);
                toc.Write(BitConverter.GetBytes(at));
            }

            return toc.ToArray();
        }

        using var head = new MemoryStream();
        head.Write("PGDMP"u8);
        head.Write([1, 16, 0, 4, 8, 1, 0]);
        for (var i = 0; i < 7; i++)
        {
            Int(head, 0);
        }

        Str(head, "essenthos_core");
        Str(head, "18.0");
        Str(head, "18.0");

        var offsets = items.Where(item => item.Rows is not null).ToDictionary(item => item.Id, _ => 0L);
        var start = head.Length + Toc(offsets).Length;
        using var data = new MemoryStream();
        foreach (var item in items.Where(item => item.Rows is not null))
        {
            offsets[item.Id] = start + data.Length;
            data.WriteByte(1);
            Int(data, item.Id);
            var bytes = Encoding.UTF8.GetBytes(item.Rows!);
            Int(data, bytes.Length);
            data.Write(bytes);
            Int(data, 0);
        }

        return [.. head.ToArray(), .. Toc(offsets), .. data.ToArray()];
    }

    /// <summary>The dependencies the table of contents names for one entry, read back from the bytes.</summary>
    private static List<string> Dependencies(byte[] archive, int id)
    {
        var at = 5 + 7;
        int ReadInt()
        {
            var negative = archive[at] != 0;
            var value = BitConverter.ToInt32(archive, at + 1);
            at += 5;
            return negative ? -value : value;
        }

        string? ReadStr()
        {
            var length = ReadInt();
            if (length < 0)
            {
                return null;
            }

            var text = Encoding.UTF8.GetString(archive, at, length);
            at += length;
            return text;
        }

        for (var i = 0; i < 7; i++)
        {
            ReadInt();
        }

        ReadStr();
        ReadStr();
        ReadStr();
        var count = ReadInt();
        for (var i = 0; i < count; i++)
        {
            var dumpId = ReadInt();
            ReadInt();
            for (var s = 0; s < 4; s++)
            {
                ReadStr();
            }

            ReadInt();
            for (var s = 0; s < 6; s++)
            {
                ReadStr();
            }

            ReadInt();
            ReadStr();
            ReadStr();
            var dependencies = new List<string>();
            while (ReadStr() is { } dependency)
            {
                dependencies.Add(dependency);
            }

            at += 9;
            if (dumpId == id)
            {
                return dependencies;
            }
        }

        throw new InvalidOperationException($"No entry {id}.");
    }

    private static void Int(Stream stream, int value)
    {
        stream.WriteByte(value < 0 ? (byte)1 : (byte)0);
        stream.Write(BitConverter.GetBytes(Math.Abs(value)));
    }

    private static void Str(Stream stream, string? value)
    {
        if (value is null)
        {
            Int(stream, -1);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        Int(stream, bytes.Length);
        stream.Write(bytes);
    }
}
