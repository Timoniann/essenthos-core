using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What a load of the rulings does on a corpus that already holds them, or holds them in part: the
/// files are applied once and then recorded, a word that lost its answer is given it again, and a
/// ruling whose word is not where it says stops the load.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnRecordReloadTests(WitnessDatabase database) : OwnRecordTests(database)
{
    /// <summary>
    /// On a corpus built from nothing the titles are written after these rulings, so the Anointed is
    /// not held yet when the Synodal's companion ruling is checked. The owner decided it is a title,
    /// and that is what the ruling beside it rests on.
    /// </summary>
    [Fact]
    public async Task ACompanionRulingStandsBesideATitleTheOwnerDecidedBeforeTheTitleIsWritten()
    {
        await Db.Entities.Where(e => e.Slug == "anointed").ExecuteDeleteAsync();
        Db.ChangeTracker.Clear();

        var loading = async () => await Load();

        await loading.Should().NotThrowAsync();
    }

    /// <summary>
    /// A person decided who is named, so the seed carries no confidence — and a word reached across
    /// a link that is itself a guess does carry one, because there the reach is what is uncertain
    /// and not the decision.
    /// </summary>
    [Fact]
    public async Task ADecisionCarriedAcrossAnUncertainLinkPicksUpTheLinksConfidence()
    {
        var ruling = Rulings[0];
        var hebrew = await Db.Words.SingleAsync(w => w.Id == Words[ruling.Word]);
        var english = Db.WordAt(English, 1, 1, 1);

        var link = new Link
        {
            FromTextId = hebrew.TextId,
            ToTextId = english.TextId,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner,
            Confidence = 0.5,
            Provenance = new() { Source = "a test" },
        };
        Db.Links.Add(link);
        Db.LinkWords.Add(new LinkWord { Link = link, Word = hebrew, Side = LinkSide.From });
        Db.LinkWords.Add(new LinkWord { Link = link, Word = english, Side = LinkSide.To });
        await Db.SaveChangesAsync();

        await Load();

        var carried = await Db.WordEntities.SingleAsync(a => a.WordId == english.Id);
        carried.Method.Should().Be(LinkMethod.Manual);
        carried.Confidence.Should().Be(0.5);
    }

    /// <summary>
    /// The woman's record was listed by a dataset and is headed, described and read by us, so the
    /// page credits the ruling and not the dataset, on a database that loaded the ruling before the
    /// credit moved as well as on a cold one. The dataset's listing stays as a claim.
    /// </summary>
    [Fact]
    public async Task ARecordTheRulingReHeadsIsCreditedToTheRulingAndACreditIsNotMovedTwice()
    {
        await Load();
        await Load();

        var woman = await Db.Entities.Include(e => e.Claims).SingleAsync(e => e.Slug == "daughter");
        woman.Source.Should().StartWith("Essenthos").And.Contain("2026-09-30");
        woman.Claims.Should().ContainSingle(claim => claim.Method == LinkMethod.StatedBySource)
            .Which.Source.Should().Be("a test");

        // A database that loaded the file before the credit moved: the dataset's credit is back and
        // the file is already recorded, so nothing else of the file runs again.
        woman.Source = "BibleData by a test";
        await Db.SaveChangesAsync();
        await Load();
        (await Db.Entities.SingleAsync(e => e.Slug == "daughter")).Source.Should().StartWith("Essenthos");
    }

    /// <summary>
    /// And a record written before this pass named anything is named on the next boot. The guard
    /// that skips the rulings is what would otherwise keep it unnamed for ever — the records this
    /// is about are already there, so a step that named only what it created on this boot would
    /// never reach one of them.
    /// </summary>
    [Fact]
    public async Task ARecordWrittenBeforeThisPassNamedAnythingIsNamedOnTheNextBoot()
    {
        await Load();
        Db.EntityNames.RemoveRange(await Db.EntityNames.ToListAsync());
        await Db.SaveChangesAsync();

        var again = await Load();

        again.AlreadyLoaded.Should().BeTrue();
        again.Labelled.Should().Be(Rulings.Count(r => r.Create is not null));
        (await Db.EntityNames.CountAsync()).Should().Be(again.Labelled);
    }

    /// <summary>
    /// A rulings file arriving on a corpus that already holds the others is applied on the next
    /// boot, and only it: the files are decided at different times, so whether one is recorded says
    /// nothing about another.
    /// </summary>
    [Fact]
    public async Task ARulingsFileTheCorpusDoesNotYetHoldIsAppliedAloneOnTheNextBoot()
    {
        await Load();
        var report = SenseReadingFiles.ReportRulings();
        await Db.WordEntities.Where(a => a.Source == report.Source).ExecuteDeleteAsync();
        var entities = await Db.Entities.CountAsync();

        var again = await Load();

        again.AlreadyLoaded.Should().BeFalse();
        again.Created.Should().Be(0);
        (await Db.Entities.CountAsync()).Should().Be(entities);
        (await Db.WordEntities.Where(a => a.Source == report.Source).Select(a => a.WordId).ToListAsync())
            .Should().BeEquivalentTo(report.Rulings.Select(r => Words[r.Word]));
    }

    /// <summary>
    /// 1 Chronicles 1:9, where a reading had put the trading place on the son of Cush. The ruling is
    /// the answer, so the reading at the word and the copy the links carried from it are taken back
    /// rather than left beside it.
    /// </summary>
    [Fact]
    public async Task ARulingTakesBackTheReadingItOverrulesAndItsCarriedCopy()
    {
        var ruling = SenseReadingFiles.GenealogyRulings().Rulings[0];
        var place = new Entity
        {
            Kind = EntityKind.Place, Slug = "raamah-2", Name = "Raamah", SourceId = "raamah-2", Source = "a test",
        };
        Db.Entities.Add(place);
        await Db.SaveChangesAsync();

        const string reading = "a reading of the verse by a-model, prompt sense-1, run to 2026-09-06";
        var rendering = Db.WordAt(English, 1, 1, 1);
        Db.WordEntities.Add(new WordEntity
        {
            WordId = Words[ruling.Word], EntityId = place.Id, Method = LinkMethod.ModelReading,
            Confidence = 0.99, Source = reading, Note = "read as the place",
        });
        Db.WordEntities.Add(new WordEntity
        {
            WordId = rendering.Id, EntityId = place.Id, Method = LinkMethod.ModelReading,
            Confidence = 0.97, Source = reading,
            Note = $"through BHSA word {Words[ruling.Word]}, linked by aligner",
        });
        await Db.SaveChangesAsync();

        await Load();

        (await Db.WordEntities.AnyAsync(a => a.EntityId == place.Id)).Should().BeFalse();
        (await Db.WordEntities.Include(a => a.Entity).SingleAsync(a => a.WordId == Words[ruling.Word]))
            .Entity!.Slug.Should().Be(ruling.Existing);
    }

    /// <summary>
    /// A word is ruled on once, unless each later ruling corrects the one before it and says which
    /// answer it takes back. Two files ruling on one word otherwise both annotate it, and a reader
    /// would meet a word naming whichever of two decisions the loader happened to write first.
    /// </summary>
    [Fact]
    public void NoWordIsRuledOnTwiceUnlessTheLaterRulingCorrectsTheEarlier()
    {
        foreach (var word in Rulings.GroupBy(r => Words[r.Word]).Where(g => g.Count() > 1))
        {
            var rulings = word.ToList();
            foreach (var (earlier, later) in rulings.Zip(rulings.Skip(1)))
            {
                if (later.Alongside is not null)
                {
                    rulings.Should().HaveCount(2, $"word {word.Key} has a companion ruling and nothing else");
                    later.Corrects.Should().BeNull();
                    later.Alongside.Should().Be(earlier.Existing);
                    var companionFiles = SenseReadingFiles.AllRulings().Where(f => f.Rulings.Contains(earlier) || f.Rulings.Contains(later));
                    var town = Db.Entities.Single(e => e.Slug == later.Alongside).Kind == EntityKind.Place;
                    companionFiles.Should().OnlyContain(f => (town || !f.Carry) && f.Method == "manual");
                }
                else
                    later.Corrects.Should().Be(earlier.Create?.Slug ?? earlier.Existing,
                        $"the ruling on {later.Reference} corrects the one before it");
            }
        }
    }

    /// <summary>
    /// A ruling names its word by where it stands and how it reads, so a word that reads otherwise at
    /// that address is not the word ruled on: the load stops and says which, instead of putting the
    /// owner's decision on a stranger.
    /// </summary>
    [Fact]
    public async Task ARulingWhoseWordIsNotWhereItSaysStopsTheLoadAndNamesIt()
    {
        var ruling = Rulings[0];
        await Db.Words.Where(w => w.Id == Words[ruling.Word])
            .ExecuteUpdateAsync(set => set.SetProperty(w => w.Surface, "another"));

        var load = Load;

        (await load.Should().ThrowAsync<InvalidDataException>())
            .Which.Message.Should().Contain(ruling.Word.ToString()).And.Contain("never point it at whatever word");
        (await Db.WordEntities.AnyAsync()).Should().BeFalse();
    }

    /// <summary>
    /// A file is applied once and then skipped, so a word that lost its answer afterwards — a reload
    /// numbers a text's words afresh, and the annotations go with the old rows — is given it again on
    /// the next load, and on the load after that nothing is written.
    /// </summary>
    [Fact]
    public async Task AWordThatLostItsRulingsAnswerIsGivenItAgainOnce()
    {
        await Load();
        var file = SenseReadingFiles.NamesakeSecondRulings();
        var ruling = file.Rulings.First(r => Rulings.All(other => other.Corrects is null || other.Word != r.Word));
        await Db.WordEntities.Where(a => a.WordId == Words[ruling.Word]).ExecuteDeleteAsync();

        var again = await Load();

        again.Restored.Should().Be(1);
        (await Db.WordEntities.Include(a => a.Entity).SingleAsync(a => a.WordId == Words[ruling.Word]))
            .Should().Match<WordEntity>(a => a.Entity!.Slug == ruling.Existing && a.Source == file.Source);
        (await Load()).Restored.Should().Be(0);
    }
}
