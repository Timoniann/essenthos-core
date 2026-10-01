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
internal readonly record struct OsisPiece(OsisPieceKind Kind, string Text, string? Lemma, bool Title, string? Split = null);

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
    private const string LemmaAttribute = "lemma";
    private const string TypeAttribute = "type";
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
        StringBuilder? note = null;
        var noteDepth = 0;
        var titles = 0;

        while (xml.Read())
        {
            switch (xml.NodeType)
            {
                case XmlNodeType.Element when xml.LocalName == NoteElement && !xml.IsEmptyElement:
                    noteDepth++;
                    note ??= new StringBuilder();
                    break;

                case XmlNodeType.EndElement when xml.LocalName == NoteElement:
                    if (--noteDepth == 0 && note is not null)
                    {
                        pieces.Add(new OsisPiece(OsisPieceKind.Note, note.ToString().Trim(), null, titles > 0));
                        note = null;
                    }

                    break;

                case XmlNodeType.Element when xml.LocalName == TitleElement && !xml.IsEmptyElement && noteDepth == 0:
                    titles++;
                    break;

                case XmlNodeType.EndElement when xml.LocalName == TitleElement && noteDepth == 0:
                    titles--;
                    break;

                case XmlNodeType.Element when xml.LocalName == WordElement && noteDepth == 0:
                    lemma = xml.GetAttribute(LemmaAttribute);
                    split = xml.GetAttribute(TypeAttribute) is { } type && type.StartsWith(SplitType, StringComparison.Ordinal)
                        ? type
                        : null;
                    if (xml.IsEmptyElement)
                    {
                        pieces.Add(new OsisPiece(OsisPieceKind.Word, string.Empty, lemma, titles > 0, split));
                    }
                    else
                    {
                        word = new StringBuilder();
                    }

                    break;

                case XmlNodeType.EndElement when xml.LocalName == WordElement && noteDepth == 0 && word is not null:
                    pieces.Add(new OsisPiece(OsisPieceKind.Word, word.ToString(), lemma, titles > 0, split));
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
                        pieces.Add(new OsisPiece(OsisPieceKind.Text, xml.Value, null, titles > 0));
                    }

                    break;
            }
        }

        return pieces;
    }
}
