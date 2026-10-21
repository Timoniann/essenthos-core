using System.Text.RegularExpressions;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What our own records say to a reader, as the files that write them hold it: a record's notes and
/// line, the reason beside each of its alternatives, the notes on its passages and appointed times,
/// the verse a title's bearer is named at, and every rendering of the notes. None of it speaks of how
/// the record was settled — what the encyclopedia merged, which dataset holds whom, what was linked
/// or is left for review, what an audit ruled out — because a reader is owed what the text says, and
/// those sentences were written to whoever was settling it.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed partial class ReaderCopyTests : IDisposable
{
    private readonly AppDbContext _db;

    public ReaderCopyTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    [Fact]
    public void NoRecordFileTellsTheReaderHowTheRecordWasSettled()
    {
        var said = new List<(string Where, string Text)>();
        void Add(string where, string? text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                said.Add((where, text));
            }
        }

        foreach (var ruling in SenseReadingFiles.AllRulings().SelectMany(file => file.Rulings))
        {
            var slug = ruling.Existing ?? ruling.Create?.Slug;
            Add($"{slug} notes", ruling.Create?.Notes);
            Add($"{slug} line", ruling.Create?.Distinguisher);
            Add($"{slug} notes", ruling.Says?.Notes);
            Add($"{slug} line", ruling.Says?.Distinguisher);
            foreach (var alternative in ruling.Alternatives ?? [])
            {
                Add($"{slug} alternative", alternative.Reason);
                Add($"{slug} alternative", alternative.Describes);
            }
        }

        foreach (var title in SenseReadingFiles.Titles().Titles)
        {
            Add($"{title.Slug} notes", title.Notes);
            Add($"{title.Slug} line", title.Distinguisher);
            foreach (var alternative in title.Alternatives ?? [])
            {
                Add($"{title.Slug} alternative", alternative.Reason);
            }

            foreach (var bearer in title.Bearers ?? [])
            {
                Add($"{title.Slug} bearer {bearer.Slug}", bearer.Why);
            }
        }

        foreach (var set in new[] { ThingSet.MadeAndKept, ThingSet.Narratives })
        foreach (var thing in ThingFiles.Read(set).Records)
        {
            Add($"{thing.Slug} notes", thing.Notes);
            Add($"{thing.Slug} line", thing.Distinguisher);
            foreach (var passage in thing.Passages ?? [])
            {
                Add($"{thing.Slug} passage", passage.Note);
            }

            foreach (var time in thing.Times ?? [])
            {
                Add($"{thing.Slug} time", time.Note);
            }
        }

        foreach (var people in PeopleFiles.Read().Tribes)
        {
            Add($"{people.Slug} notes", people.Notes);
            Add($"{people.Slug} line", people.Distinguisher);
        }

        foreach (var rendered in NoteTranslationLoader.Read())
        {
            Add($"{rendered.Slug} notes", rendered.English);
            Add($"{rendered.Slug} notes in Ukrainian", rendered.Ukr);
            Add($"{rendered.Slug} notes in German", rendered.Deu);
            Add($"{rendered.Slug} notes in Spanish", rendered.Spa);
        }

        said.Should().HaveCountGreaterThan(1000, "every file a record's reader copy comes from is read");
        said.Where(one => Settling().IsMatch(one.Text))
            .Select(one => $"{one.Where}: {Settling().Match(one.Text).Value}")
            .Should().BeEmpty();
    }

    /// <summary>
    /// The corpus already loaded is rewritten too: a note, a people's note and an alternative's reason
    /// that still read as the loader wrote them take the files' words, and a note somebody has changed
    /// since is left as it is.
    /// </summary>
    [Fact]
    public async Task TheMigrationRewritesWhatStillReadsAsLoaded()
    {
        const string loaded =
            "A contemporary of Ezra, not the pre-exilic Hilkiah son of Shallum in Ezra's own genealogy. The " +
            "encyclopedia's record merges the two, which is why its distinguisher names this verse while its own " +
            "verse list does not.";
        const string reason =
            "The record the reading chose, and the one the encyclopedia's own verse list gives this verse to. The " +
            "audit of 2026-09-06 by claude-opus-5 under prompt audit-1 ruled it out at high confidence: A " +
            "contemporary of Ezra, not Ezra's pre-exilic great-grandfather; the record wrongly merges the two. " +
            "Nobody has shown that the two are not one man; what has been shown is that the record does not fit the verse.";
        const string named =
            "Named here as Medes. Strong's own English is a Madian, a native of Madai; the King James renders it Mede.";

        var hilkiah = Record("hilkiah-at-ezras-right-hand", EntityKind.Person, loaded);
        var forefather = Record("hilkiah-3", EntityKind.Person, null);
        var medes = Record("medes", EntityKind.People, named);
        var zimri = Record("zimri-in-jezebels-cry", EntityKind.Person, "A note somebody wrote since.");
        _db.Entities.AddRange(hilkiah, forefather, medes, zimri);
        await _db.SaveChangesAsync();
        _db.EntityAlternatives.Add(new EntityAlternative
        {
            EntityId = hilkiah.Id, AlternativeEntityId = forefather.Id, Reason = reason, Source = "a test",
        });
        await _db.SaveChangesAsync();

        _db.Periods.AddRange(
            new Period
            {
                Slug = "life-noah", Name = "Noah", Kind = "life", Level = 2, Source = "a test",
                Notes = "The dataset marks one end of this with an asterisk, meaning the year is inferred from another " +
                        "figure rather than stated in the text. Runs 350 years past the close of \"The Flood\", the era " +
                        "it opens in. The parent of a band is where it begins, not what contains it.",
            },
            new Period
            {
                Slug = "era-return", Name = "The return and the Persian period", Kind = "era", Level = 0,
                Source = "a test",
                Notes = "Where the scripture layer stops. Its chronology is arithmetic over the genealogies and the reign " +
                        "lengths, and those stop here, so nothing after this point is narrated on this axis: not the four " +
                        "centuries between the testaments, and not the New Testament itself. The jubilees are counted " +
                        "forward rather than narrated, and the only marks past this era are world history's. The gap is " +
                        "stated, not a loading failure.",
            });
        _db.Events.Add(new Event
        {
            Slug = "beginfirsttempleconstruction", Name = "First Temple construction began", Source = "a test",
            Notes = "[Ussher date p67] Wikidata has this event as \"Solomon's Temple\" at 996 BCE " +
                    "(http://www.wikidata.org/entity/Q223644), where this row holds 967 BCE. One event under two " +
                    "datasets, 29 years apart — so world history draws no second mark for it and the disagreement is here.",
        });
        await _db.SaveChangesAsync();

        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        new Rewriting().Apply(builder);
        builder.Operations.OfType<SqlOperation>().Should().HaveCount(4);
        foreach (var operation in builder.Operations.OfType<SqlOperation>())
        {
            await _db.Database.ExecuteSqlRawAsync(operation.Sql);
        }

        var notes = await _db.Entities.AsNoTracking().ToDictionaryAsync(e => e.Slug, e => e.Notes);
        notes["hilkiah-at-ezras-right-hand"].Should().Be(
            NoteTranslationLoader.Read().Single(r => r.Slug == "hilkiah-at-ezras-right-hand").English);
        notes["medes"].Should().BeNull();
        notes["zimri-in-jezebels-cry"].Should().Be("A note somebody wrote since.");

        var period = await _db.Periods.AsNoTracking().SingleAsync(p => p.Slug == "life-noah");
        period.Notes.Should().Be(
            "One end of this is inferred from another figure rather than stated in the text. Runs 350 years past " +
            "the close of \"The Flood\", the era it opens in.");
        (await _db.Periods.AsNoTracking().SingleAsync(p => p.Slug == "era-return")).Notes
            .Should().StartWith("Where the chronology the genealogies and the reign lengths give stops");
        (await _db.Events.AsNoTracking().SingleAsync()).Notes.Should().Be(
            "[Ussher date p67] Wikidata dates it, as \"Solomon's Temple\", to 996 BCE " +
            "(http://www.wikidata.org/entity/Q223644): 29 years from 967 BCE.");

        var rewritten = await _db.EntityAlternatives.AsNoTracking().SingleAsync();
        rewritten.Reason.Should().Be(SenseReadingFiles.AllRulings()
            .SelectMany(file => file.Rulings)
            .Single(r => r.Create?.Slug == "hilkiah-at-ezras-right-hand")
            .Alternatives!.Single().Reason);
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM period");
        _db.Database.ExecuteSqlRaw("DELETE FROM event");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    private static Entity Record(string slug, EntityKind kind, string? notes) => new()
    {
        Kind = kind, Slug = slug, Name = slug, Notes = notes, SourceId = slug, Source = "a test",
    };

    private sealed class Rewriting : ReaderCopySaysNothingOfHowItWasSettled
    {
        public void Apply(MigrationBuilder builder) => Up(builder);
    }

    [GeneratedRegex(
        @"\b(encyclopedia|dataset|corpus|BibleData|Theographic|TIPNR|this record|the record|own record|for review|" +
        @"audit|under prompt|linked|Named here as|is held as|are held|joined here|the entry is held|project owner|" +
        @"the owner (ruled|decided)|agent|review page|in chat)\b" +
        @"|енциклопед|набір даних|корпус|цей запис|цього запису|цим записом|залишено (на|для)" +
        @"|Enzyklopädie|Datensatz|Korpus|dieser Eintrag|diesem Eintrag|dieses Eintrags|Prüfung" +
        @"|enciclopedia|conjunto de datos|este registro|esta entrada|revisión",
        RegexOptions.IgnoreCase)]
    private static partial Regex Settling();
}
