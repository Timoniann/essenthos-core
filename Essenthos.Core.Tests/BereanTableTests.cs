using Essenthos.Core.Berean;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Reading the translation tables, where two things are quietly load-bearing: which rows belong to
/// one verse, and what order they stand in.
/// </summary>
public sealed class BereanTableTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"berean-{Guid.NewGuid():N}.tsv");

    public void Dispose() => File.Delete(_path);

    /// <summary>
    /// Twenty-three columns, of which this reads eight. The rest are the English's own punctuation
    /// and spacing, which the published edition carries and this file does not need to.
    /// </summary>
    private void Write(params (string Heb, string Grk, string Eng, string Verse, string Language,
        string Word, string Sigla, string Strong, string Reference, string Rendering)[] rows)
    {
        var lines = new List<string> { string.Join('\t', Enumerable.Repeat("column", 23)) };
        foreach (var row in rows)
        {
            var cells = new string[23];
            Array.Fill(cells, string.Empty);
            cells[0] = row.Heb;
            cells[1] = row.Grk;
            cells[2] = row.Eng;
            cells[3] = row.Verse;
            cells[4] = row.Language;
            cells[5] = row.Word;
            cells[6] = row.Sigla;
            cells[row.Language == "Greek" ? 11 : 10] = row.Strong;
            cells[12] = row.Reference;
            cells[18] = row.Rendering;
            lines.Add(string.Join('\t', cells));
        }

        File.WriteAllLines(_path, lines);
    }

    /// <summary>
    /// The bug this pins. Forty Hebrew rows carry a half — the file's way of inserting a word
    /// between two it had already numbered — and read as integers they all become zero and sort to
    /// the front of the verse. Every link in such a verse would then name the wrong word, and a
    /// misordered verse has exactly the shape of a correct one.
    /// </summary>
    [Fact]
    public void KeepsTheOrderOfAWordTheFileNumberedWithAHalf()
    {
        Write(
            ("8132", "", "1", "7", "Hebrew", "first", "first", "0001", "Genesis 1:1", " first "),
            ("8133", "", "3", "7", "Hebrew", "third", "third", "0003", "", " third "),
            ("8132.5", "", "2", "7", "Hebrew", "second", "second", "0002", "", " second "));

        var verse = BereanTable.Verses(_path).Single();
        verse.Rows.Select(row => row.Original).Should().Equal("first", "second", "third");
    }

    /// <summary>
    /// Only a verse's first row carries the reference, so the reference has to be remembered as the
    /// file is walked. A verse that lost it would be dropped silently.
    /// </summary>
    [Fact]
    public void CarriesTheReferenceForwardAcrossAVerse()
    {
        Write(
            ("", "1", "1", "23146", "Greek", "Βίβλος", "Βίβλος", "0976", "Matthew 1:1", " record "),
            ("", "2", "2", "23146", "Greek", "γενέσεως", "γενέσεως", "1078", "", " of genealogy "),
            ("", "3", "1", "23147", "Greek", "Ἀβραὰμ", "Ἀβραὰμ", "0011", "Matthew 1:2", " Abraham "));

        var verses = BereanTable.Verses(_path).ToList();
        verses.Select(v => v.Reference).Should().Equal("Matthew 1:1", "Matthew 1:2");
        verses[0].Rows.Should().HaveCount(2);
    }

    /// <summary>
    /// A verse is padded out to a fixed width with rows that have no word. They say nothing about
    /// any word and would be counted against the Greek if they were kept, which is what decides
    /// whether a verse is refused for the two texts dividing it differently.
    /// </summary>
    [Fact]
    public void LeavesOutThePaddingRows()
    {
        Write(
            ("", "1", "1", "23146", "Greek", "Βίβλος", "Βίβλος", "0976", "Matthew 1:1", " record "),
            ("", "2", "2", "23146", "Greek", "", "", "", "", ""),
            ("", "3", "3", "23146", "Greek", "", "", "", "", ""));

        BereanTable.Verses(_path).Single().Rows.Should().ContainSingle();
    }

    /// <summary>
    /// The file writes the number bare and zero-padded; this corpus writes a language letter and no
    /// leading zeros, which is what <c>strong_entry</c> and every other text use.
    /// </summary>
    [Fact]
    public void WritesTheStrongNumberTheWayTheCorpusDoes()
    {
        Write(
            ("1", "", "1", "1", "Hebrew", "בְּרֵאשִׁית", "בְּרֵאשִׁית", "7225", "Genesis 1:1", " In the beginning "),
            ("", "1", "1", "23146", "Greek", "Βίβλος", "Βίβλος", "0976", "Matthew 1:1", " record "));

        BereanTable.Verses(_path).Select(v => v.Rows[0].StrongNumber).Should().Equal("H7225", "G976");
    }

    /// <summary>
    /// A word one edition reads and another does not is wrapped in that edition's siglum. It is the
    /// difference between the Berean's Greek and the Nestle base, and therefore between a verse that
    /// can be joined by order and one that cannot.
    /// </summary>
    [Fact]
    public void KnowsAVariantReadingFromTheBaseText()
    {
        Write(
            ("", "1", "1", "1", "Greek", "καὶ", "καὶ", "2532", "Matthew 3:2", " and "),
            ("", "2", "2", "1", "Greek", "τὸ", "[τὸ]", "3588", "", " the "));

        var rows = BereanTable.Verses(_path).Single().Rows;
        rows[0].InTheBaseText.Should().BeTrue();
        rows[1].InTheBaseText.Should().BeFalse();
    }
}
