using System.Text;
using System.Xml;

namespace Essenthos.Core.Sword;

internal enum OsisPieceKind
{
    /// <summary>A <c>&lt;w&gt;</c> element: the words it holds are one rendering, whatever it tags.</summary>
    Word,

    /// <summary>Text standing between the elements, which no tag claims.</summary>
    Text,

    /// <summary>A note the edition prints beside the verse, never words of the verse.</summary>
    Note,
}

/// <param name="Text">What the piece prints. A <see cref="OsisPieceKind.Word"/> may print nothing.</param>
/// <param name="Lemma">The <c>lemma</c> attribute of a <c>&lt;w&gt;</c>, as written; null elsewhere.</param>
/// <param name="Title">The piece stands inside a <c>&lt;title&gt;</c> the edition prints within the verse.</param>
/// <param name="Split">
/// What a <c>&lt;w type="x-split-…"&gt;</c> names: the elements of one verse sharing it are a single
/// rendering the edition printed in pieces — <em>afin</em> … <em>que</em> under one number. Null elsewhere.
/// </param>
/// <param name="Source">The <c>src</c> attribute of a <c>&lt;w&gt;</c>, as written; null elsewhere.</param>
/// <param name="Added">
/// The piece stands inside a <c>&lt;transChange type="added"&gt;</c>: the words the translators supplied, which the
/// King James prints in italics.
/// </param>
/// <param name="Morph">The <c>morph</c> attribute of a <c>&lt;w&gt;</c>, as written; null elsewhere.</param>
internal readonly record struct OsisPiece(
    OsisPieceKind Kind, string Text, string? Lemma, bool Title, string? Split = null, string? Source = null, bool Added = false,
    string? Morph = null)
{
    /// <summary>
    /// The <c>type</c> of the innermost <c>&lt;title&gt;</c> the piece stands in (<c>chapter</c> for a section heading,
    /// <c>psalm</c>), or of the <c>&lt;note&gt;</c> it is (<c>study</c>, <c>x-crossreference</c>); null where none is written.
    /// </summary>
    public string? Type { get; init; }
}

/// <summary>
/// One verse of a SWORD module's OSIS, as the pieces a reader needs: the tagged words, the text
/// between them, and the notes. Milestones, paragraph and chapter markers carry no words and are
/// passed over; a note's content is gathered as its own text and never becomes words of the verse.
/// </summary>
internal static class OsisVerse
{
    private const string WordElement = "w";
    private const string NoteElement = "note";
    private const string TitleElement = "title";
    private const string ChangeElement = "transChange";
    private const string AddedType = "added";
    private const string LemmaAttribute = "lemma";
    private const string TypeAttribute = "type";
    private const string SourceAttribute = "src";
    private const string MorphAttribute = "morph";
    private const string SplitType = "x-split";

    public static List<OsisPiece> Parse(string markup)
    {
        using var xml = XmlReader.Create(
            new StringReader(markup),
            new XmlReaderSettings
            {
                ConformanceLevel = ConformanceLevel.Fragment,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                DtdProcessing = DtdProcessing.Ignore,
            });

        var pieces = new List<OsisPiece>();
        StringBuilder? word = null;
        string? lemma = null;
        string? split = null;
        string? source = null;
        string? morph = null;
        StringBuilder? note = null;
        var noteDepth = 0;
        var titles = 0;
        var added = 0;
        var changes = new Stack<bool>();
        var titleTypes = new Stack<string?>();
        string? noteType = null;

        while (xml.Read())
        {
            switch (xml.NodeType)
            {
                case XmlNodeType.Element when xml.LocalName == NoteElement && !xml.IsEmptyElement:
                    if (noteDepth++ == 0)
                    {
                        noteType = xml.GetAttribute(TypeAttribute);
                    }

                    note ??= new StringBuilder();
                    break;

                case XmlNodeType.EndElement when xml.LocalName == NoteElement:
                    if (--noteDepth == 0 && note is not null)
                    {
                        pieces.Add(new OsisPiece(OsisPieceKind.Note, note.ToString().Trim(), null, titles > 0) { Type = noteType });
                        note = null;
                    }

                    break;

                case XmlNodeType.Element when xml.LocalName == TitleElement && !xml.IsEmptyElement && noteDepth == 0:
                    titles++;
                    titleTypes.Push(xml.GetAttribute(TypeAttribute));
                    break;

                case XmlNodeType.EndElement when xml.LocalName == TitleElement && noteDepth == 0:
                    titles--;
                    titleTypes.TryPop(out _);
                    break;

                case XmlNodeType.Element when xml.LocalName == ChangeElement && !xml.IsEmptyElement:
                    var supplied = xml.GetAttribute(TypeAttribute) == AddedType;
                    changes.Push(supplied);
                    added += supplied ? 1 : 0;
                    break;

                case XmlNodeType.EndElement when xml.LocalName == ChangeElement && changes.Count > 0:
                    added -= changes.Pop() ? 1 : 0;
                    break;

                case XmlNodeType.Element when xml.LocalName == WordElement && noteDepth == 0:
                    lemma = xml.GetAttribute(LemmaAttribute);
                    split = xml.GetAttribute(TypeAttribute) is { } type && type.StartsWith(SplitType, StringComparison.Ordinal)
                        ? type
                        : null;
                    source = xml.GetAttribute(SourceAttribute);
                    morph = xml.GetAttribute(MorphAttribute);
                    if (xml.IsEmptyElement)
                    {
                        pieces.Add(new OsisPiece(OsisPieceKind.Word, string.Empty, lemma, titles > 0, split, source, added > 0, morph) { Type = TitleType(titleTypes) });
                    }
                    else
                    {
                        word = new StringBuilder();
                    }

                    break;

                case XmlNodeType.EndElement when xml.LocalName == WordElement && noteDepth == 0 && word is not null:
                    pieces.Add(new OsisPiece(OsisPieceKind.Word, word.ToString(), lemma, titles > 0, split, source, added > 0, morph) { Type = TitleType(titleTypes) });
                    word = null;
                    break;

                case XmlNodeType.Text or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace or XmlNodeType.CDATA:
                    if (noteDepth > 0)
                    {
                        note!.Append(xml.Value);
                    }
                    else if (word is not null)
                    {
                        word.Append(xml.Value);
                    }
                    else
                    {
                        pieces.Add(new OsisPiece(OsisPieceKind.Text, xml.Value, null, titles > 0, Added: added > 0) { Type = TitleType(titleTypes) });
                    }

                    break;
            }
        }

        return pieces;
    }

    private static string? TitleType(Stack<string?> open) => open.TryPeek(out var type) ? type : null;
}
