using System.Buffers.Binary;
using System.Text;

namespace Essenthos.Core.Sword;

/// <param name="Name">The node's own name, as a SWORD front end lists it: <c>Chapter 91a</c>, <c>Jubilees 4</c>.</param>
/// <param name="Text">What the node holds, as the markup the module stores; empty for a node that only groups others.</param>
internal sealed record SwordNode(string Name, string Text);

/// <summary>
/// A book CrossWire stores as a tree of named nodes rather than in a versification: the RawGenBook
/// driver, which a work that is not a Bible, or a Bible book no versification lists, is published with.
///
/// <para>
/// Three files beside each other: <c>.idx</c> holds four bytes per node, where the node's record starts
/// in <c>.dat</c>; the record is the parent's, the next sibling's and the first child's offsets, the
/// name ending in a zero byte, and a two-byte length of what follows, which for a node with text is
/// where its text starts in <c>.bdt</c> and how long it is. Every number is little-endian.
/// </para>
/// </summary>
internal static class SwordGeneralBook
{
    private const string Driver = "RawGenBook";

    /// <summary>Every node of the module in the order its index lists them, which is the order the book prints them.</summary>
    /// <param name="folder">The module's root: the folder holding <c>mods.d</c> and <c>modules</c>.</param>
    public static IReadOnlyList<SwordNode> Nodes(string folder)
    {
        var configuration = SwordModule.Configuration(folder);
        if (configuration.Value("ModDrv") != Driver)
        {
            throw new NotSupportedException(
                $"{configuration.Name} is stored with the {configuration.Value("ModDrv")} driver, and this reader knows only "
                + $"{Driver}. A Bible in a versification is read by {nameof(SwordModule)}.");
        }

        var data = Path.Combine(folder, configuration.Value("DataPath").TrimStart('.', '/'));
        var index = File.ReadAllBytes(data + ".idx");
        var records = File.ReadAllBytes(data + ".dat");
        var texts = File.ReadAllBytes(data + ".bdt");

        var nodes = new List<SwordNode>(index.Length / 4);
        for (var at = 0; at + 4 <= index.Length; at += 4)
        {
            var record = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(at)));
            var nameStart = record + 12;
            var nameEnd = Array.IndexOf(records, (byte)0, nameStart);
            if (nameEnd < 0)
            {
                throw new InvalidDataException($"{configuration.Name}: the node at {record} in {data}.dat has no end to its name.");
            }

            var name = Encoding.UTF8.GetString(records, nameStart, nameEnd - nameStart);
            var length = BinaryPrimitives.ReadUInt16LittleEndian(records.AsSpan(nameEnd + 1));
            var text = string.Empty;
            if (length >= 8)
            {
                var start = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(records.AsSpan(nameEnd + 3)));
                var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(records.AsSpan(nameEnd + 7)));
                text = Encoding.UTF8.GetString(texts, start, size);
            }

            nodes.Add(new SwordNode(name, text));
        }

        return nodes;
    }
}
