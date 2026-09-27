using Essenthos.Core.Corpus;
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
/// The Synodal is joined to the King James in the books it gains only in the chapters the two print
/// alike, and in a chapter they print otherwise its verses belong to no verse link. The aligner does
/// not meet such a verse at an address it only covers: the King James's Tobit 7:15 stands at 7:13 and
/// covers 7:14, where the Synodal's 7:14 stands, and a word paired between the two crossed a verse pair
/// nothing joins.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class RefusedChapterAlignmentTests : IDisposable
{
    private const int Tobit = 70;

    private readonly AppDbContext _db;
    private readonly AlignmentPipeline _aligner;
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "essenthos-align-test", Guid.NewGuid().ToString("N"));

    public RefusedChapterAlignmentTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _aligner = new AlignmentPipeline(_db, NullLogger<AlignmentPipeline>.Instance);

        var synodal = Corpus.Add(_db, Sources.SynodalSlug, TextKind.Translation, "eng");
        _db.AddBook(synodal, Tobit, "Tobit", (7, 1, ["ate"]), (7, 2, ["called"]), (7, 3, ["began"]));
        var kingJames = Corpus.Add(_db, Sources.KingJamesSlug, TextKind.Translation, "eng");
        _db.AddBook(kingJames, Tobit, "Tobit", (7, 1, ["ate"]), (7, 2, ["called"]), (7, 3, ["called"]),
            (7, 4, ["wept"]));
        _db.SaveChanges();

        // The King James's 7:3 stands at 7:2 and covers 7:3, and it prints a verse the Synodal does not.
        var covering = _db.VerseReferences.Single(reference =>
            reference.Verse!.TextId == kingJames.Id && reference.Verse.Number == 3);
        covering.CanonicalVerse = 2;
        _db.VerseReferences.Add(new VerseReference
        {
            VerseId = covering.VerseId,
            CanonicalBook = Tobit,
            CanonicalChapter = 7,
            CanonicalVerse = 3,
            IsPrimary = false,
        });
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
    public async Task TheChapterTheTwoPrintOtherwiseIsRefused()
    {
        (await VerseLinkLoader.Refused(_db, Sources.SynodalSlug, Sources.KingJamesSlug))
            .Should().BeEquivalentTo([(Tobit, 7)]);
        (await VerseLinkLoader.Refused(_db, Sources.KingJamesSlug, Sources.SynodalSlug))
            .Should().BeEmpty("the King James is not declared against the Synodal");
    }

    [Fact]
    public async Task AVerseInARefusedChapterIsNotMetAtAnAddressItOnlyCovers()
    {
        // 7:3 is left to the Synodal alone, so the two share 7:1 and 7:2. The King James's two verses at
        // 7:2 are one word, so the line reads the same in whichever order they come.
        Prepare(
            source: ["ate", "called"],
            target: ["ate", "called called"],
            answers: ["0-0:0.9:0.9", "0-0:0.9:0.9 0-1:0.9:0.9"]);

        var proposals = await _aligner.Proposals(
            Sources.SynodalSlug, Sources.KingJamesSlug, _workspace, 0.1, selection: Selection.All);

        var verseOf = await _db.Words.ToDictionaryAsync(w => w.Id, w => w.Verse!.Number);
        proposals.Select(pair => (verseOf[pair.From], verseOf[pair.To])).Should().BeEquivalentTo([(1, 1), (2, 2), (2, 3)]);
    }

    [Fact]
    public void OnlyTheWordsOfAVerseStandingInARefusedChapterAreLeftOut()
    {
        var words = new Dictionary<(int, int, int), List<AlignmentPipeline.Word>>
        {
            [(Tobit, 7, 3)] = [new(1, "began", null), new(2, "wept", null, Aside: (Tobit, 7, 2))],
            [(Tobit, 8, 1)] = [new(3, "rose", null, Aside: (Tobit, 7, 18))],
            [(Tobit, 9, 1)] = [new(4, "sent", null, Aside: (Tobit, 8, 21))],
        };

        var met = AlignmentPipeline.Unmet(words, new HashSet<(int, int)> { (Tobit, 7) });

        met.Should().BeEquivalentTo(new Dictionary<(int, int, int), List<AlignmentPipeline.Word>>
        {
            [(Tobit, 7, 3)] = [new(1, "began", null)],
            [(Tobit, 9, 1)] = [new(4, "sent", null, Aside: (Tobit, 8, 21))],
        });
    }

    /// <summary>A workspace as a finished run over the verses the two share leaves it.</summary>
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
