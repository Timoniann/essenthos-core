using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A town and its people, a people and its valley, and a place the translations read as a common
/// word.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnRecordPeopleAndPlaceTests(WitnessDatabase database) : OwnRecordTests(database)
{
    /// <summary>
    /// Chronicles' <em>the father of Gibeon</em> is the town and its people: the Hebrew word and the
    /// Synodal's «Гаваонитян» linked to it each name the Gibeonites beside Gibeon, in the word panel
    /// and on both pages, and a second load writes nothing again.
    /// </summary>
    [Fact]
    public async Task TheFatherOfGibeonNamesTheTownAndItsPeople()
    {
        var ruled = Rulings.Single(r => r.Reference == "1CH 8:29" && r.Existing == "gibeonites");
        ruled.Alongside.Should().Be("gibeon");
        var town = Db.Words.Single(w => w.Id == Words[ruled.Word]);
        var synodalAddress = new RuledWord("RUSV", "1CH 8:29", 5, "Гаваонитян");
        var synodal = Db.Place([synodalAddress])[synodalAddress];
        var gibeon = await Db.Entities.SingleAsync(e => e.Slug == "gibeon");
        Db.WordEntities.Add(new WordEntity
        {
            WordId = town.Id, EntityId = gibeon.Id, Method = LinkMethod.Lexical, Confidence = 0.9,
            Source = "a resolution by number", Note = "H1391",
        });
        var link = new Link { FromTextId = synodal.TextId, ToTextId = town.TextId,
            Method = LinkMethod.StatedBySource, Relation = LinkRelation.Renders,
            Provenance = new() { Source = "a fixture's stated link" } };
        Db.Links.Add(link);
        link.Words.Add(new() { Word = synodal, Side = LinkSide.From });
        link.Words.Add(new() { Word = town, Side = LinkSide.To });
        await Db.SaveChangesAsync();

        await Load();


        foreach (var word in new[] { town.Id, synodal.Id })
        {
            (await Essenthos.Core.Corpus.Annotations.AllOf(Db, [word], CancellationToken.None))[word]
                .Select(e => e.Slug).Should().Equal("gibeonites", "gibeon");
            (await Db.Database.SqlQueryRaw<int>(
                    $"WITH {Annotating.Settled}, {Annotating.Named} SELECT entity_id AS \"Value\" FROM named WHERE word_id = {word}")
                    .ToListAsync())
                .Should().BeEquivalentTo([gibeon.Id, (await Db.Entities.SingleAsync(e => e.Slug == "gibeonites")).Id]);
        }

        var ids = await Db.WordEntities.Where(a => a.WordId == town.Id || a.WordId == synodal.Id)
            .OrderBy(a => a.Id).Select(a => a.Id).ToArrayAsync();
        await Load();
        (await Db.WordEntities.Where(a => a.WordId == town.Id || a.WordId == synodal.Id)
            .OrderBy(a => a.Id).Select(a => a.Id).ToArrayAsync()).Should().Equal(ids);
    }

    /// <summary>
    /// A town and its people stand beside each other only where a person ruled both on the word: a
    /// people ruled over a town only a resolution gave is the one answer, as it was.
    /// </summary>
    [Fact]
    public async Task ATownOnlyAResolutionGaveDoesNotStandBesideARuledPeople()
    {
        var word = Words[Rulings.First(r => r.Alongside is "gibeon").Word];
        var gibeon = await Db.Entities.SingleAsync(e => e.Slug == "gibeon");
        var gibeonites = await Db.Entities.SingleAsync(e => e.Slug == "gibeonites");
        Db.WordEntities.AddRange(
            new WordEntity { WordId = word, EntityId = gibeon.Id, Method = LinkMethod.Lexical, Confidence = 0.9, Source = "a resolution" },
            new WordEntity { WordId = word, EntityId = gibeonites.Id, Method = LinkMethod.Manual, Source = "a ruling" });
        await Db.SaveChangesAsync();

        (await Essenthos.Core.Corpus.Annotations.AllOf(Db, [word], CancellationToken.None))[word]
            .Select(e => e.Slug).Should().Equal("gibeonites");
        (await Db.Database.SqlQueryRaw<int>(
                $"WITH {Annotating.Settled}, {Annotating.Named} SELECT entity_id AS \"Value\" FROM named WHERE word_id = {word}")
                .ToListAsync())
            .Should().Equal(gibeonites.Id);
    }

    /// <summary>
    /// The Rephaim are a people of their own, and the word that names them reaches the translation
    /// linked to it; the same word after <em>the valley of</em> is the valley.
    /// </summary>
    [Fact]
    public async Task TheRephaimAreAPeopleAndTheirValleyIsAPlace()
    {
        var english = Db.WordAt(English, 1, 1, 1);
        Tie(Words[Ruled("rephaim", "GEN 14:5").Word], english);
        await Db.SaveChangesAsync();

        await Load();

        var rephaim = await Db.Entities.SingleAsync(e => e.Slug == "rephaim");
        rephaim.Kind.Should().Be(EntityKind.People);
        (await Db.WordEntities.Where(a => a.WordId == english.Id).Select(a => a.Entity!.Slug).ToListAsync())
            .Should().Equal("rephaim");
        (await Db.WordEntities.Where(a => a.WordId == Words[Ruled("valleyofrephaim", "JOS 15:8").Word])
            .Select(a => a.Entity!.Slug).ToListAsync()).Should().Equal("valleyofrephaim");
        (await Db.WordEntities.CountAsync(a => a.EntityId == rephaim.Id && a.Word!.Text!.Slug == "BHSA"))
            .Should().Be(11);
    }

    /// <summary>
    /// A place the Hebrew names where every translation reads a common word stays on the Hebrew word:
    /// Horesh is the wood the translations say, and the link does not carry the place to it.
    /// </summary>
    [Fact]
    public async Task APlaceTheTranslationsReadAsACommonWordStaysOnTheHebrew()
    {
        var english = Db.WordAt(English, 1, 1, 1);
        var horesh = Words[Ruled("horesh", "1SA 23:15").Word];
        Tie(horesh, english);
        await Db.SaveChangesAsync();

        await Load();

        (await Db.WordEntities.Where(a => a.WordId == horesh).Select(a => a.Entity!.Slug).ToListAsync())
            .Should().Equal("horesh");
        (await Db.WordEntities.AnyAsync(a => a.WordId == english.Id)).Should().BeFalse();
    }
}
