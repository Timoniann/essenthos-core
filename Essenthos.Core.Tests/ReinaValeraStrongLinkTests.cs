using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Reina-Valera's Strong numbers are Rubén Gómez's, and the owner allowed them for the mapping
/// alone: they are read from the edition's files for one run, and only the links drawn from them,
/// credited to him, reach the corpus — never a number on a word.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ReinaValeraStrongLinkTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ReinaValeraStrongLinkLoader _loader;
    private readonly Text _spanish;
    private readonly Text _hebrew;
    private readonly string _folder;

    public ReinaValeraStrongLinkTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _loader = new ReinaValeraStrongLinkLoader(
            _db,
            new TaggedTextLinkLoader(_db, NullLogger<TaggedTextLinkLoader>.Instance),
            NullLogger<ReinaValeraStrongLinkLoader>.Instance);

        _spanish = Corpus.Add(_db, EbibleTextSource.ReinaValera, TextKind.Translation, "spa",
            (1, 1, ["EN", "el", "principio", "crió", "Dios", "los", "cielos"]));
        _hebrew = Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo",
            (1, 1, ["בְּ", "רֵאשִׁית", "בָּרָא", "אֱלֹהִים", "אֵת", "הַ", "שָּׁמַיִם"]));
        _db.SaveChanges();

        var hebrew = _db.Words.Where(w => w.TextId == _hebrew.Id).OrderBy(w => w.Position).ToList();
        string[] numbers = ["H9003", "H7225", "H1254", "H430", "H853", "H9009", "H8064"];
        for (var at = 0; at < numbers.Length; at++)
        {
            hebrew[at].StrongNumber = numbers[at];
        }

        _db.SaveChanges();

        _folder = Path.Combine(Path.GetTempPath(), $"rv-strong-{Guid.NewGuid():N}", "ReinaValera1909");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "02-GENspaRV1909.usfm"),
            "\\id GEN Genesis\n\\c 1\n\\p\n\\v 1 \\w EN el principio|strong=\"H7225\"\\w* \\w crió|strong=\"H1254\"\\w* "
            + "\\w Dios|strong=\"H0430\"\\w* \\w los cielos|strong=\"H8064\"\\w*.\n");
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
        Directory.Delete(Path.GetDirectoryName(_folder)!, recursive: true);
    }

    private Word Spanish(int position) => _db.WordAt(_spanish, 1, 1, position);

    private Word Hebrew(int position) => _db.WordAt(_hebrew, 1, 1, position);

    [Fact]
    public void TheWordsOfOneTagAreOneUnitAndTheirNumberIsRead()
    {
        var words = EbibleTextSource.Numbers(_folder)[(1, 1, 1)];

        words.Select(w => w.Text).Should().Equal("EN", "el", "principio", "crió", "Dios", "los", "cielos");
        words.Take(3).Should().OnlyContain(w => w.Numbers.SequenceEqual(new[] { "H7225" }));
        words.Take(3).Select(w => w.Unit).Distinct().Should().ContainSingle();
        words.Select(w => w.Unit).Distinct().Should().HaveCount(4);
    }

    [Fact]
    public async Task TheNumbersReachTheLinksCreditedToGomezAndNeverTheWords()
    {
        await _loader.Load(_folder, [_hebrew.Slug]);

        var links = await _db.Links.AsNoTracking().Include(l => l.Words).Include(l => l.Provenance)
            .Where(l => l.FromTextId == _spanish.Id).ToListAsync();
        links.Should().HaveCount(4).And.OnlyContain(link =>
            link.Method == LinkMethod.StrongNumber
            && link.Provenance!.Source.StartsWith(ReinaValeraStrongLinkLoader.Credit));
        links.Single(link => link.Words.Any(w => w.WordId == Hebrew(2).Id)).Words
            .Where(w => w.Side == LinkSide.From).Select(w => w.WordId)
            .Should().BeEquivalentTo([Spanish(1).Id, Spanish(2).Id, Spanish(3).Id]);

        (await _db.Words.Where(w => w.TextId == _spanish.Id).AnyAsync(w => w.StrongNumber != null)).Should().BeFalse();
        (await _db.WordStrongs.AnyAsync(s => s.Word!.TextId == _spanish.Id)).Should().BeFalse();
    }

    /// <summary>
    /// A word the pair already says has no counterpart is left out of the number its phrase carries:
    /// the article a verdict found supplied is not rendered by the H7225 on <em>EN el principio</em>.
    /// </summary>
    [Fact]
    public async Task AWordStatedAbsentIsLeftOutOfItsPhrasesMatch()
    {
        var absence = new Link
        {
            FromTextId = _spanish.Id, ToTextId = _hebrew.Id, Relation = LinkRelation.Expands,
            Method = LinkMethod.RuleBased, Confidence = 0.95, Provenance = new() { Source = "a verdict under test" },
        };
        absence.Words.Add(new LinkWord { WordId = Spanish(2).Id, Side = LinkSide.From });
        absence.Claims.Add(new LinkClaim { Method = LinkMethod.RuleBased, Confidence = 0.95, Provenance = absence.Provenance });
        _db.Links.Add(absence);
        _db.SaveChanges();

        await _loader.Load(_folder, [_hebrew.Slug]);

        var beginning = await _db.Links.Include(l => l.Words)
            .SingleAsync(l => l.Method == LinkMethod.StrongNumber && l.Words.Any(w => w.WordId == Hebrew(2).Id));
        beginning.Words.Where(w => w.Side == LinkSide.From).Select(w => w.WordId)
            .Should().BeEquivalentTo([Spanish(1).Id, Spanish(3).Id]);
    }

    [Fact]
    public async Task AWordTheHandAlignmentNamesIsScoredAgainstItAndNotWrittenOver()
    {
        var stated = new Link
        {
            FromTextId = _spanish.Id, ToTextId = _hebrew.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource, Provenance = new() { Source = "Clear Bible Alignments under test" },
        };
        stated.Words.Add(new LinkWord { WordId = Spanish(5).Id, Side = LinkSide.From });
        stated.Words.Add(new LinkWord { WordId = Hebrew(4).Id, Side = LinkSide.To });
        var wrong = new Link
        {
            FromTextId = _spanish.Id, ToTextId = _hebrew.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource, Provenance = stated.Provenance,
        };
        wrong.Words.Add(new LinkWord { WordId = Spanish(4).Id, Side = LinkSide.From });
        wrong.Words.Add(new LinkWord { WordId = Hebrew(7).Id, Side = LinkSide.To });
        _db.Links.AddRange(stated, wrong);
        _db.SaveChanges();

        var score = (await _loader.Score(_folder, [_hebrew.Slug])).Single(s => s.To == _hebrew.Slug);
        score.Should().BeEquivalentTo(new { StatedWords = 2, AgreedWords = 1, Pairs = 2, AgreedPairs = 1 });

        await _loader.Load(_folder, [_hebrew.Slug]);
        var numbered = await _db.Links.Include(l => l.Words)
            .Where(l => l.FromTextId == _spanish.Id && l.Method == LinkMethod.StrongNumber).ToListAsync();
        numbered.Should().HaveCount(2).And.NotContain(link =>
            link.Words.Any(w => w.WordId == Spanish(4).Id || w.WordId == Spanish(5).Id));
    }
}
