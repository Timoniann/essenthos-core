using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A verse that covers an address beyond its own is read there too, beside the verse the other text
/// stands at it. Where each text has such a verse at the same address, the two meet only because
/// both are there: the verse links join each to the verse standing there and not to one another, so
/// a word pair between them crosses a verse pair nothing joins.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CoveredAddressAlignmentTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AlignmentPipeline _aligner;
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "essenthos-align-test", Guid.NewGuid().ToString("N"));

    public CoveredAddressAlignmentTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _aligner = new AlignmentPipeline(_db, NullLogger<AlignmentPipeline>.Instance);

        var source = Corpus.Add(_db, "BSB", TextKind.Translation, "eng",
            (1, 1, ["void"]), (1, 2, ["earth"]), (1, 3, ["earth"]));
        var target = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (1, 1, ["earth"]), (1, 2, ["earth"]), (1, 3, ["void"]));
        _db.SaveChanges();

        Covers(source, 3);
        Covers(target, 1);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Fact]
    public async Task TwoVersesThatOnlyCoverAnAddressAreNotPairedThere()
    {
        Prepare(
            source: ["void", "earth earth", "earth"],
            target: ["earth", "earth earth", "void"],
            answers: ["0-0:0.9:0.9", "0-0:0.9:0.9 0-1:0.9:0.9 1-0:0.9:0.9 1-1:0.9:0.9", "0-0:0.9:0.9"]);

        var proposals = await _aligner.Proposals("BSB", "KJV", _workspace, 0.1, selection: Selection.All);

        var verseOf = await _db.Words.ToDictionaryAsync(w => w.Id, w => w.Verse!.Number);
        proposals.Select(pair => (verseOf[pair.From], verseOf[pair.To])).Should().BeEquivalentTo(
        [
            (1, 1),
            (2, 2),
            (2, 1),
            (3, 2),
            (3, 3),
        ]);
    }

    /// <summary>The verse at 1:<paramref name="verse"/> of a text covers 1:2 as well.</summary>
    private void Covers(Text text, int verse) =>
        _db.VerseReferences.Add(new VerseReference
        {
            Verse = _db.VerseAt(text, 1, verse),
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 2,
            IsPrimary = false,
        });

    /// <summary>
    /// A workspace as a finished run leaves it. Each verse is one word, and the two at 1:2 on each
    /// side are the same word, so the lines read the same in whichever order the verses come.
    /// </summary>
    private void Prepare(string[] source, string[] target, string[] answers)
    {
        static string[] Tokens(string[] lines) =>
        [
            .. lines.Select(line => string.Join(' ',
                line.Split(' ').Select(word => AlignmentTokens.One(EnglishStemmer.Stem(word))))),
        ];

        Directory.CreateDirectory(Path.Combine(_workspace, "alignment"));
        File.WriteAllLines(Path.Combine(_workspace, "source.txt"), Tokens(source));
        File.WriteAllLines(Path.Combine(_workspace, "target.txt"), Tokens(target));
        File.WriteAllLines(Path.Combine(_workspace, "alignment", "pharaoh.txt"), answers);
    }
}
