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
/// How a variant between the two Greek witnesses is written down.
///
/// It used to be written as <c>omits</c> whichever edition lacked the word, so the relation said
/// that the editions differ here and never which of them has the word — and where they differ by
/// substituting one word for another, Matthew 1:10's Ἀμώς against αμων, it said so as four
/// independent absences rather than as two readings of the same place.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class GreekVariantTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Text _nestle;
    private readonly Text _scrivener;

    public GreekVariantTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");

        _nestle = Corpus.Add(_db, "nestle1904", TextKind.CriticalEdition, "grc",
            (1, 1, ["τὸν", "Ἀμώς", "δὲ"]),
            (1, 2, ["Δαυείδ", "τοῦ"]),
            (1, 3, ["Ἰησοῦ"]));
        _scrivener = Corpus.Add(_db, "scrivener1894", TextKind.PrintedEdition, "grc",
            (1, 1, ["τον", "αμων", "δε"]),
            (1, 2, ["δαβιδ"]),
            (1, 3, ["ιησου", "χριστου"]));
        _db.SaveChanges();

        // Amos and Amon are different words and carry different numbers, which is exactly why no
        // number pairs them and why their place in the verse is all there is to go on.
        Number(_nestle,
            (1, 1, "G3588"), (1, 2, "G301"), (1, 3, "G1161"),
            (2, 1, "G1138"), (2, 2, "G3588"),
            (3, 1, "G2424"));
        Number(_scrivener,
            (1, 1, "G3588"), (1, 2, "G300"), (1, 3, "G1161"),
            (2, 1, "G1138"),
            (3, 1, "G2424"), (3, 2, "G5547"));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    /// <summary>
    /// One reading written two ways is one link, and it says what it is: a different word in the
    /// same place, inferred from that place and from nothing else.
    /// </summary>
    [Fact]
    public async Task TwoWordsNoNumberPairsStandingInTheSamePlaceAreOneSubstitution()
    {
        await Load();

        var link = (await Links()).Single(l => l.From.Contains("Ἀμώς"));

        link.To.Should().Equal("αμων");
        link.Relation.Should().Be(LinkRelation.Renders);
        link.Method.Should().Be(LinkMethod.Lexical);
        link.Confidence.Should().BeLessThan(0.85);
    }

    /// <summary>
    /// The direction the relation has to carry. A word the first edition has and the second does
    /// not is not the same claim as a word the second has and the first does not, and a link with
    /// one empty side cannot tell them apart on its own.
    /// </summary>
    [Fact]
    public async Task AWordOnlyOneEditionHasSaysWhichEditionLacksIt()
    {
        await Load();
        var links = await Links();

        var added = links.Single(l => l.From.Contains("τοῦ"));
        added.To.Should().BeEmpty();
        added.Relation.Should().Be(LinkRelation.Expands);

        var missing = links.Single(l => l.To.Contains("χριστου"));
        missing.From.Should().BeEmpty();
        missing.Relation.Should().Be(LinkRelation.Omits);
    }

    [Fact]
    public async Task TheOutcomeCountsTheTwoDirectionsApart()
    {
        var outcome = await Load();

        outcome.Added.Should().Be(1);
        outcome.Missing.Should().Be(1);
    }

    private async Task<GreekWitnessOutcome> Load()
    {
        _db.ChangeTracker.Clear();
        return await new GreekWitnessLinkLoader(_db, NullLogger<GreekWitnessLinkLoader>.Instance)
            .Load("nestle1904", "scrivener1894");
    }

    /// <summary>The number each word carries, by verse and place. Everything here is chapter one.</summary>
    private void Number(Text text, params (int Verse, int Position, string Strong)[] words)
    {
        foreach (var (verse, position, strong) in words)
        {
            _db.WordAt(text, 1, verse, position).StrongNumber = strong;
        }
    }

    private async Task<List<(List<string> From, List<string> To, LinkRelation Relation, LinkMethod Method,
        double? Confidence)>> Links()
    {
        var links = await _db.Links.AsNoTracking().OrderBy(l => l.Id).ToListAsync();
        var members = await _db.LinkWords.AsNoTracking().Include(w => w.Word).ToListAsync();

        return links
            .Select(link => (
                members.Where(m => m.LinkId == link.Id && m.Side == LinkSide.From)
                    .Select(m => m.Word!.Surface).ToList(),
                members.Where(m => m.LinkId == link.Id && m.Side == LinkSide.To)
                    .Select(m => m.Word!.Surface).ToList(),
                link.Relation,
                link.Method,
                link.Confidence))
            .ToList();
    }
}
