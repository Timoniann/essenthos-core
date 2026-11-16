using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Essenthos.Core.Commentary;

/// <summary>
/// A comment as the reader is given it: blocks (a heading <c>h</c>, a paragraph <c>p</c>, a line of verse <c>l</c>, a
/// quotation <c>q</c>), each a run of pieces of text. A piece is a plain string, or an object holding the text as
/// <c>t</c> with what marks it: <c>i</c> italic, <c>b</c> bold, <c>sc</c> small capitals, <c>sup</c> raised (a verse
/// number in the printed text), <c>u</c> underlined, <c>n</c> a note of the author's, and <c>r</c> the verse it cites
/// as [book, chapter] or [book, chapter, verse] or [book, chapter, verse, last chapter, last verse].
///
/// <para>
/// The markup readers drive it: they say where a block begins and ends and what marks the text they hand over, and
/// it keeps every word. Runs of white space become one space, as the markup means them; nothing else is changed.
/// </para>
/// </summary>
internal sealed class CommentBuilder
{
    [Flags]
    public enum Mark
    {
        None = 0,
        Italic = 1,
        Bold = 2,
        SmallCaps = 4,
        Raised = 8,
        Underlined = 16,
        Note = 32,
    }

    private sealed record Piece(string Text, Mark Marks, int[]? Reference);

    private sealed record Block(string Kind, List<Piece> Pieces);

    private static readonly JsonWriterOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly List<Block> blocks = [];
    private Block? open;
    private readonly List<Mark> marks = [];
    private int[]? reference;

    public int Blocks => blocks.Count + (open is { Pieces.Count: > 0 } ? 1 : 0);

    /// <summary>Ends the block being written and starts one of this kind.</summary>
    public void Begin(string kind)
    {
        End();
        open = new Block(kind, []);
    }

    /// <summary>Ends the block being written; text after it starts a paragraph.</summary>
    public void End()
    {
        if (open is null)
        {
            return;
        }

        var pieces = open.Pieces;
        while (pieces.Count > 0 && pieces[^1].Text.TrimEnd().Length == 0)
        {
            pieces.RemoveAt(pieces.Count - 1);
        }

        if (pieces.Count > 0)
        {
            pieces[^1] = pieces[^1] with { Text = pieces[^1].Text.TrimEnd() };
            blocks.Add(open);
        }

        open = null;
    }

    public void Push(Mark mark) => marks.Add(mark);

    /// <summary>Takes off the mark pushed last; a closing tag with nothing open is ignored.</summary>
    public void Pop()
    {
        if (marks.Count > 0)
        {
            marks.RemoveAt(marks.Count - 1);
        }
    }

    /// <summary>The verse the text from here cites, until <see cref="Cite"/> is called with null.</summary>
    public void Cite(int[]? verse) => reference = verse;

    public void Text(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        var collapsed = Collapse(text);
        open ??= new Block("p", []);
        var pieces = open.Pieces;
        var atStart = pieces.Count == 0 || pieces[^1].Text.EndsWith(' ');
        if (atStart)
        {
            collapsed = collapsed.TrimStart();
        }

        if (collapsed.Length == 0)
        {
            return;
        }

        var current = marks.Aggregate(Mark.None, (all, mark) => all | mark);
        if (pieces.Count > 0 && pieces[^1].Marks == current && SameReference(pieces[^1].Reference, reference))
        {
            pieces[^1] = pieces[^1] with { Text = pieces[^1].Text + collapsed };
        }
        else
        {
            pieces.Add(new Piece(collapsed, current, reference));
        }
    }

    /// <summary>The plain text of the last block written, for a reader that drops a block by what it says.</summary>
    public string? LastBlockText(string kind)
    {
        End();
        return blocks.Count > 0 && blocks[^1].Kind == kind ? string.Concat(blocks[^1].Pieces.Select(p => p.Text)) : null;
    }

    public void DropLastBlock() => blocks.RemoveAt(blocks.Count - 1);

    /// <summary>The blocks as JSON; null when no block holds any text.</summary>
    public string? Json()
    {
        End();
        if (blocks.Count == 0)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, Options))
        {
            json.WriteStartArray();
            foreach (var block in blocks)
            {
                json.WriteStartObject();
                json.WriteStartArray(block.Kind);
                foreach (var piece in block.Pieces)
                {
                    if (piece.Marks == Mark.None && piece.Reference is null)
                    {
                        json.WriteStringValue(piece.Text);
                        continue;
                    }

                    json.WriteStartObject();
                    json.WriteString("t", piece.Text);
                    Flag(json, piece.Marks, Mark.Italic, "i");
                    Flag(json, piece.Marks, Mark.Bold, "b");
                    Flag(json, piece.Marks, Mark.SmallCaps, "sc");
                    Flag(json, piece.Marks, Mark.Raised, "sup");
                    Flag(json, piece.Marks, Mark.Underlined, "u");
                    Flag(json, piece.Marks, Mark.Note, "n");
                    if (piece.Reference is { } verse)
                    {
                        json.WriteStartArray("r");
                        foreach (var number in verse)
                        {
                            json.WriteNumberValue(number);
                        }

                        json.WriteEndArray();
                    }

                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Flag(Utf8JsonWriter json, Mark marks, Mark mark, string name)
    {
        if ((marks & mark) != 0)
        {
            json.WriteNumber(name, 1);
        }
    }

    private static bool SameReference(int[]? a, int[]? b) => a == b || a is not null && b is not null && a.SequenceEqual(b);

    private static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                space = true;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(character);
        }

        if (space)
        {
            builder.Append(' ');
        }

        return builder.ToString();
    }
}
