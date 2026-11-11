using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The record files as they ship: every one of them has to be readable before the corpus ever sees
/// it, because a loader that throws at the forty-first record of a boot has failed every step after it.
/// </summary>
public sealed class ThingFileTests
{
    private static readonly IReadOnlySet<string> Subtypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "furnishing", "vessel", "structure", "vestment", "monument", "weapon", "image", "implement",
        "feast", "fast", "sabbath", "new-moon", "sacred-year", "appointed-time", "rite", "book", "tree",
    };

    private readonly IReadOnlyList<ThingRecord> _records = ThingFiles.Read();

    [Fact]
    public void EveryRecordIsWellFormed()
    {
        _records.Select(r => r.Slug).Should().OnlyHaveUniqueItems();
        foreach (var record in _records)
        {
            record.Called.Should().NotBeNullOrEmpty($"{record.Slug} has to be findable by its names");
            foreach (var name in record.Called!)
            {
                name.Label.Should().NotBeNullOrWhiteSpace();
                foreach (var number in $"{name.HebrewStrongNumber},{name.GreekStrongNumber}".Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    number.Should().MatchRegex("^[HG][0-9]+$", $"{record.Slug}: {name.Label}");
                }
            }

            record.Kind.Should().BeOneOf("object", "observance");
            Subtypes.Should().Contain(record.Subtype, $"{record.Slug} has to be sorted by a subtype the pages know");
            record.Names.Should().ContainKeys(["ukr", "deu", "rus", "spa"], $"{record.Slug} is named in every language its page is read in");
            record.Distinguisher.Should().NotBeNullOrWhiteSpace();
            foreach (var passage in record.Passages ?? [])
            {
                ScriptureSpan.TryParse(passage.Reference).Should().NotBeNull($"{record.Slug}: {passage.Reference}");
                PassageRoles.All.Should().Contain(passage.Role);
            }

            foreach (var time in record.Times ?? [])
            {
                ScriptureSpan.TryParse(time.Reference).Should().NotBeNull($"{record.Slug}: {time.Reference}");
                ObservanceCycles.All.Should().Contain(time.Cycle);
            }

            foreach (var relation in record.Related ?? [])
            {
                ThingRelations.All.Should().Contain(relation.Type, $"{record.Slug} relates as {relation.Type}");
                ScriptureSpan.TryParse(relation.Reference)!.IsVerse.Should().BeTrue($"{record.Slug}: {relation.Reference}");
            }

            foreach (var rule in record.Occurrences ?? [])
            {
                rule.Strong.Should().MatchRegex("^[HG][0-9]+$");
                foreach (var span in (rule.Only ?? []).Concat(rule.Except ?? []))
                {
                    ScriptureSpan.TryParse(span).Should().NotBeNull($"{record.Slug}: {span}");
                }
            }
        }
    }

    /// <summary>The names readers search the appointed times by, in the Hebrew they are known by.</summary>
    [Theory]
    [InlineData("day-of-atonement", "Yom Kippur")]
    [InlineData("day-of-trumpets", "Yom Teruah")]
    [InlineData("feast-of-booths", "Sukkot")]
    [InlineData("feast-of-weeks", "Shavuot")]
    [InlineData("passover", "Pesach")]
    [InlineData("new-moon", "Rosh Chodesh")]
    [InlineData("sabbath", "Shabbat")]
    [InlineData("feast-of-dedication", "Hanukkah")]
    [InlineData("menorah", "Menorah")]
    public void AnAppointedTimeAnswersToItsHebrewName(string slug, string name) =>
        _records.Single(r => r.Slug == slug).Called!.Select(n => n.Label).Should().Contain(name);

    /// <summary>
    /// The narratives' records are beings, a city and a man, and each is written as the others are:
    /// named in Ukrainian, sorted by a kind the pages know, every span readable, and no address
    /// another list holds, since two lists writing one record would each undo the other.
    /// </summary>
    [Fact]
    public void TheNarrativesFileIsWellFormed()
    {
        var file = ThingFiles.Read(ThingSet.Narratives);

        file.Records.Select(r => r.Slug).Should().OnlyHaveUniqueItems()
            .And.NotIntersectWith(_records.Select(r => r.Slug));
        foreach (var record in file.Records)
        {
            record.Kind.Should().BeOneOf("person", "place", "people");
            record.Names.Should().ContainKey("ukr", $"{record.Slug} needs its Ukrainian name");
            record.Called.Should().NotBeNullOrEmpty();
            record.Distinguisher.Should().NotBeNullOrWhiteSpace();
            record.Occurrences.Should().NotBeNullOrEmpty($"{record.Slug} has to name its words");
        }

        foreach (var rule in file.Records.SelectMany(r => r.Occurrences ?? []).Concat(file.Refers!.SelectMany(r => r.Occurrences)))
        {
            rule.Strong.Should().MatchRegex("^[HG][0-9]+$");
            foreach (var span in (rule.Only ?? []).Concat(rule.Except ?? []))
            {
                ScriptureSpan.TryParse(span).Should().NotBeNull(span);
            }
        }

        file.Refers.Should().ContainSingle(r => r.Slug == "eve");
    }

    /// <summary>
    /// The woman of Genesis 2-4 is Eve where the word refers to her, the curse on the serpent
    /// included; the naming of what she is (2:23) and any man's wife (2:24) are not her.
    /// </summary>
    [Theory]
    [InlineData(2, 22, true)]
    [InlineData(2, 23, false)]
    [InlineData(2, 24, false)]
    [InlineData(2, 25, true)]
    [InlineData(3, 15, true)]
    [InlineData(4, 1, true)]
    [InlineData(4, 19, false)]
    public void TheWomanIsEveWhereTheWordRefersToHer(int chapter, int verse, bool eve) =>
        ThingFiles.Read(ThingSet.Narratives).Refers!.Single(r => r.Slug == "eve").Occurrences
            .Any(rule => rule.Strong == "H802" && rule.Admits(1, chapter, verse, 1)).Should().Be(eve);

    /// <summary>
    /// The garden God planted is a garden inside the land of Eden, the trees of its midst stand in it,
    /// and the Nephilim are a people; each is named in every language its page is read in.
    /// </summary>
    [Fact]
    public void TheGardenIsAPlaceInEdenAndTheNephilimAPeople()
    {
        var narratives = ThingFiles.Read(ThingSet.Narratives).Records.ToDictionary(r => r.Slug);

        var garden = narratives["garden-of-eden"];
        (garden.Kind, garden.PlaceKind).Should().Be(("place", "garden"));
        garden.Related.Should().ContainSingle().Which.Should().Be(new ThingRelation(ThingRelations.PartOf, "eden-2", "GEN 2:8", null));
        garden.Occurrences.Should().ContainSingle().Which.Admits(1, 3, 23, 1).Should().BeTrue();
        _records.Where(r => r.Slug.StartsWith("tree-of-", StringComparison.Ordinal)).Should().HaveCount(2)
            .And.OnlyContain(r => r.Related!.Single().To == "garden-of-eden" && r.Related!.Single().Type == ThingRelations.StandsIn);

        var nephilim = narratives["nephilim"];
        nephilim.EntityKind.Should().Be(EntityKind.People);
        nephilim.Occurrences.Should().ContainSingle().Which.Strong.Should().Be("H5303");

        foreach (var record in new[] { garden, nephilim })
        {
            record.Names.Should().ContainKeys(["ukr", "deu", "rus", "spa"], record.Slug);
        }
    }

    /// <summary>
    /// Nathanael's words are the owner's ruling, which is what lets them show him and not the record
    /// of Bartholomew; the New Jerusalem's in Revelation 21 likewise, and not the earthly city's.
    /// </summary>
    [Theory]
    [InlineData("nathanael", "G3482")]
    [InlineData("new-jerusalem", "G2419")]
    public void TheWordsTheOwnerRuledOnSaySo(string slug, string strong) =>
        ThingFiles.Read(ThingSet.Narratives).Records.Single(r => r.Slug == slug).Occurrences!
            .Should().Contain(rule => rule.Strong == strong && rule.Reviewed != null);

    /// <summary>The slugs another agent's models are keyed on, which the files must keep.</summary>
    [Fact]
    public void TheSlugsOthersDependOnAreThere() =>
        _records.Select(r => r.Slug).Should()
            .Contain(["ark-of-the-covenant", "menorah", "noahs-ark", "tabernacle"]);

    /// <summary>
    /// A relation the records file states becomes a row at its verse, a model's reading until the owner reviews the
    /// record and his ruling after; one to a record nobody holds, or resting on more than a verse, is no row.
    /// </summary>
    [Fact]
    public void ARelationIsARowAtItsVerseUnderWhoeverStatedIt()
    {
        var record = Record(null, new ThingRelation(ThingRelations.KeptIn, "ark-of-the-covenant", "HEB 9:4", "laid up"),
            new ThingRelation(ThingRelations.StandsIn, "nowhere", "EXO 40:21", null),
            new ThingRelation(ThingRelations.StandsIn, "tabernacle", "EXO 40", null));

        var rows = ThingRelations.Of(ThingSet.MadeAndKept, record, slug => slug != "nowhere").ToList();
        var reviewed = ThingRelations.Of(ThingSet.MadeAndKept, record with { Reviewed = "the project owner, 2026-10-01" }, _ => true)
            .First().Row!;

        rows.Select(r => r.Row is null).Should().Equal(false, true, true);
        var kept = rows[0].Row!;
        (kept.Type, kept.CanonicalBook, kept.CanonicalChapter, kept.CanonicalVerse, kept.Notes)
            .Should().Be((ThingRelations.KeptIn, 58, 9, 4, "laid up"));
        (kept.Method, kept.Confidence, kept.Source).Should().Be((LinkMethod.ModelReading, ThingRelations.ByReading, ThingSet.MadeAndKept.Source));
        (reviewed.Method, reviewed.Confidence, reviewed.Source).Should().Be((LinkMethod.Manual, null, ThingSet.MadeAndKept.ReviewedSource));
    }

    [Fact]
    public void ARelationNoRecordStandsInIsRefused()
    {
        var record = Record(null, new ThingRelation("rests-on", "tabernacle", "EXO 40:21", null));

        var read = () => ThingRelations.Of(ThingSet.MadeAndKept, record, _ => true).ToList();

        read.Should().Throw<InvalidDataException>().WithMessage("*rests-on*");
    }

    private static ThingRecord Record(string? reviewed, params ThingRelation[] related) => new(
        "golden-pot", "object", "vessel", "Golden pot", null, null, "the pot of manna", null, null, null, null, related, null,
        null, reviewed);
}

/// <summary>How a record file writes a stretch of Scripture, and what each form holds.</summary>
public sealed class ScriptureSpanTests
{
    [Theory]
    [InlineData("LEV", 3, 23, 1, true)]
    [InlineData("LEV", 4, 1, 1, false)]
    [InlineData("GEN 6-9", 1, 9, 29, true)]
    [InlineData("GEN 6-9", 1, 10, 1, false)]
    [InlineData("EXO 25:10-22", 2, 25, 22, true)]
    [InlineData("EXO 25:10-22", 2, 25, 23, false)]
    [InlineData("EXO 25:10-26:37", 2, 26, 1, true)]
    [InlineData("EXO 25:10-26:37", 2, 25, 9, false)]
    [InlineData("LEV 23", 3, 23, 44, true)]
    [InlineData("2KI 12:10", 12, 12, 10, true)]
    [InlineData("1SA 4", 9, 4, 11, true)]
    [InlineData("JHN 2:20", 43, 2, 20, true)]
    [InlineData("PHP 2:8", 50, 2, 8, true)]
    [InlineData("SNG 1:1", 22, 1, 1, true)]
    [InlineData("EZK 40", 26, 40, 5, true)]
    public void ASpanHoldsTheVersesItNames(string written, int book, int chapter, int verse, bool holds) =>
        ScriptureSpan.Parse(written).Holds(book, chapter, verse, 1).Should().Be(holds);

    [Fact]
    public void AnOccurrenceIsNamedByItsPlaceInTheVerse()
    {
        var second = ScriptureSpan.Parse("NUM 8:2#2");
        second.Holds(4, 8, 2, 2).Should().BeTrue();
        second.Holds(4, 8, 2, 1).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("XYZ 1:1")]
    [InlineData("EXO 25:22-10")]
    [InlineData("EXO 25#2")]
    [InlineData("EXO 0:1")]
    [InlineData("EXO 25:x")]
    public void WhatIsNotASpanIsRefused(string written) => ScriptureSpan.TryParse(written).Should().BeNull();

    [Fact]
    public void ARuleTakesWhatItsSpansAdmit()
    {
        var rule = new OccurrenceRule("H4501", ["EXO 25-40", "LEV"], ["EXO 37:17#2"], null);
        rule.Admits(2, 25, 31, 1).Should().BeTrue();
        rule.Admits(2, 37, 17, 2).Should().BeFalse();
        rule.Admits(2, 37, 17, 1).Should().BeTrue();
        rule.Admits(11, 7, 49, 1).Should().BeFalse();
    }
}
