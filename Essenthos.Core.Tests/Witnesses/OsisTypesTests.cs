using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Sword;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// A SWORD verse's titles and notes say what they are: a section heading is a title of type <c>chapter</c>, a cross
/// reference a note of type <c>x-crossreference</c>. The Czech Study Translation is read by those types (SwordText
/// <c>SectionHeadings</c>).
/// </summary>
public class OsisTypesTests
{
    private const string Matthew11 =
        """<title type="chapter">Kniha původu Ježíše Krista</title> Rodokmen<note n="v1" type="study">ř.: kniha původu</note> Ježíše Krista,<note n="o2" type="x-crossreference">Ga 3,16</note> syna Abrahamova.<note n="81" type="x-index">Pozn. 81 v tabulce na str. 1499</note>""";

    [Fact]
    public void ATitleAndANoteCarryTheirType()
    {
        var pieces = OsisVerse.Parse(Matthew11);

        pieces.Where(piece => piece.Title).Should().OnlyContain(piece => piece.Type == "chapter").And.NotBeEmpty();
        pieces.Where(piece => piece.Kind == OsisPieceKind.Note).Select(piece => piece.Type)
            .Should().Equal("study", "x-crossreference", "x-index");
        pieces.Where(piece => !piece.Title && piece.Kind != OsisPieceKind.Note).Should().OnlyContain(piece => piece.Type == null);
    }

    [Fact]
    public void TypedNotesLoadACrossReferenceAsOne()
    {
        var pieces = OsisVerse.Parse(Matthew11).Where(piece => !piece.Title).ToList();
        var unit = 0;
        SwordTextSource.Segment(pieces, SwordSegmentation.Spaced, ref unit, out var typed, typedNotes: true);
        SwordTextSource.Segment(pieces, SwordSegmentation.Spaced, ref unit, out var plain);

        typed.Select(note => note.Kind).Should().Equal(VerseNoteKind.Footnote, VerseNoteKind.CrossReference, VerseNoteKind.Footnote);
        plain.Should().OnlyContain(note => note.Kind == VerseNoteKind.Footnote);
    }
}
