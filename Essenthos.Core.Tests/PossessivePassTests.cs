using System.Text.Json;
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
/// Genesis 4:8 in miniature: אָחִיו "his brother" is one Hebrew word, the Synodal spends two on it,
/// and the aligner linked only the noun.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PossessivePassTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Text _russian;
    private readonly Text _hebrew;

    public PossessivePassTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");

        _russian = Corpus.Add(_db, "RUSV", TextKind.Translation, "rus",
            (4, 8, ["и", "восстал", "Каин", "на", "Авеля", "брата", "своего", "и", "мой", "его", "и", "ты"]));
        _hebrew = Corpus.Add(_db, "BHSA", TextKind.ManuscriptTradition, "hbo",
            (4, 8, ["וַ", "יָּקָם", "קַיִן", "אֶל", "הֶבֶל", "אָחִיו", "וַ", "יַּהַרְגֵהוּ"]));
        _db.SaveChanges();

        _db.WordAt(_hebrew, 4, 8, 6).Morphology = Suffix("p3", "sg", "m");
        _db.WordAt(_hebrew, 4, 8, 8).Morphology = Suffix("p3", "sg", "m");
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    [Fact]
    public async Task TheAgreeingPossessiveBesideTheLinkedNounIsLinkedToTheSameWordAndOnlyOnce()
    {
        Link(6, 6); // брата → אָחִיו
        Link(8, 7); // и → וַ
        Link(10, 8); // его → יַּהַרְגֵהוּ, already reaching the Hebrew
        var pass = new PossessivePass(_db, NullLogger<PossessivePass>.Instance);

        var report = await pass.Run("RUSV", "BHSA", apply: true);
        var again = await pass.Run("RUSV", "BHSA", apply: true);

        report.Should().StartWith("RUSV to BHSA: 1 possessives");
        again.Should().StartWith("RUSV to BHSA: 0 possessives");
        var written = await _db.Links.AsNoTracking()
            .Where(link => link.Source == PossessivePass.Source)
            .Select(link => new
            {
                link.Method,
                From = link.Words.Single(word => word.Side == LinkSide.From).Word!.Position,
                To = link.Words.Single(word => word.Side == LinkSide.To).Word!.Position,
                Claims = link.Claims.Count,
            })
            .ToListAsync();
        written.Should().ContainSingle().Which.Should().Be(new { Method = LinkMethod.RuleBased, From = 7, To = 6, Claims = 1 });
    }

    /// <summary>мой is first person; a third-person suffix beside it is not what it renders.</summary>
    [Fact]
    public async Task APossessiveThatDisagreesWithTheSuffixIsLeftAlone()
    {
        Link(10, 8); // его → יַּהַרְגֵהוּ; мой stands beside it and is first person

        var report = await new PossessivePass(_db, NullLogger<PossessivePass>.Instance).Run("RUSV", "BHSA", apply: false);

        report.Should().StartWith("RUSV to BHSA: 0 possessives");
    }

    /// <summary>
    /// своего beside брата shares אָחִיו rightly, and so does его four words away, since it agrees
    /// with the suffix; и is not a pronoun; ты on the same word agrees with nothing on it and goes.
    /// </summary>
    [Fact]
    public async Task APronounApartFromTheWordThatRendersTheSameHebrewAndDisagreeingWithItIsWithdrawn()
    {
        Link(6, 6); // брата → אָחִיו
        Link(7, 6); // своего → אָחִיו, beside it
        Link(10, 6); // его → אָחִיו, apart but agreeing
        Link(12, 6); // ты → אָחִיו, apart and second person
        Link(1, 6); // и → אָחִיו, not a pronoun

        var report = await new SharedWordPass(_db, NullLogger<SharedWordPass>.Instance).Run("RUSV", "BHSA", apply: true);

        report.Should().StartWith("RUSV to BHSA: 1 aligner links");
        var left = await _db.Links.AsNoTracking()
            .Select(link => link.Words.Single(word => word.Side == LinkSide.From).Word!.Position)
            .ToListAsync();
        left.Should().BeEquivalentTo([1, 6, 7, 10]);
    }

    private static JsonDocument Suffix(string person, string number, string gender) =>
        JsonDocument.Parse($$"""{"suffixPerson": "{{person}}", "suffixNumber": "{{number}}", "suffixGender": "{{gender}}"}""");

    private void Link(int russian, int hebrew)
    {
        var link = new Link
        {
            FromTextId = _russian.Id,
            ToTextId = _hebrew.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner,
            Confidence = 0.9,
            Source = "SIL.Machine, aligned as written",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(_russian, 4, 8, russian), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(_hebrew, 4, 8, hebrew), Side = LinkSide.To });
        _db.LinkClaims.Add(new LinkClaim { Link = link, Method = LinkMethod.Aligner, Confidence = 0.9, Source = link.Source });
        _db.SaveChanges();
    }
}
