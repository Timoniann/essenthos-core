using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The events of one year come in the order the text tells them: by the verse each is read from, Ussher's
/// annals by his paragraphs, what no verse places after what one does, the world's events last.
/// </summary>
public sealed class EventOrderTests
{
    private const string BibleData = "BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0";
    private const string Ussher = "Ussher's Annals of the World, 1658, transcribed in BibleData by Brady Stephenson, CC BY 4.0";

    [Fact]
    public void TheCreationComesBeforeAdamsBirthByTheVerseItsDescriptionCites()
    {
        Ordered(
                Told("birthadam1", "Birth", 1, 1, 27),
                Unreferenced("creation", "Unique", "All of Creation was formed by G-d's spoken word. (GEN 1:1-31)"))
            .Should().Equal("creation", "birthadam1");
    }

    [Fact]
    public void TheFloodEndsBeforeTheCovenantWithNoah() =>
        Ordered(Told("covenantwithnoah", "Unique", 1, 9, 9), Told("endflood", "End", 1, 8, 13))
            .Should().Equal("endflood", "covenantwithnoah");

    [Fact]
    public void BooksComeInTheirOrderBeforeChaptersAndVerses() =>
        Ordered(
                Told("enddaniel2captivity", "End", 27, 1, 21),
                Told("cyrusdecree", "Message", 15, 1, 1),
                Told("isaiah", "Begin", 23, 6, 1),
                Told("kings", "End", 12, 15, 27))
            .Should().Equal("kings", "cyrusdecree", "isaiah", "enddaniel2captivity");

    [Fact]
    public void AtOneVerseAKingDiesHisReignEndsAndTheNextBegins() =>
        Ordered(
                Told("beginhezekiah1reign", "Begin", 12, 16, 20),
                Told("endhezekiah1coregencywithahaz", "End", 12, 16, 20),
                Told("deathahaz1", "Death", 12, 16, 20))
            .Should().Equal("deathahaz1", "endhezekiah1coregencywithahaz", "beginhezekiah1reign");

    [Fact]
    public void AReignBegunAndEndedAtOneVerseBeginsFirst() =>
        Ordered(
                Told("endzimri2reign", "End", 11, 16, 15),
                Told("beginzimri2reign", "Begin", 11, 16, 15),
                Told("endelah1reign", "End", 11, 16, 15))
            .Should().Equal("endelah1reign", "beginzimri2reign", "endzimri2reign");

    [Fact]
    public void AReignThatEndsInTheYearItsKingDiesEndsAtHisDeath() =>
        Ordered(
                Told("endahab1reign", "End", 11, 16, 29) with { EntityId = 7 },
                Told("battleatramothgilead", "Battle", 11, 22, 3),
                Told("deathahab1", "Death", 11, 22, 37) with { EntityId = 7 },
                Told("jehuprophecytojeshoshaphat", "Prophesy", 14, 19, 2))
            .Should().Equal("battleatramothgilead", "deathahab1", "endahab1reign", "jehuprophecytojeshoshaphat");

    [Fact]
    public void AReignHandedOverInLifeEndsAtTheHandoverBeforeTheSuccessorBegins() =>
        Ordered(
                Told("deathdavid1", "Death", 11, 2, 10) with { EntityId = 3 },
                Told("enddavidreignjerusalem", "End", 10, 5, 5) with { EntityId = 3 },
                Told("beginsolomon1reign", "Begin", 11, 1, 39) with { EntityId = 4 },
                Told("enddavid1reign", "End", 11, 2, 10) with { EntityId = 3 })
            .Should().Equal("enddavid1reign", "enddavidreignjerusalem", "beginsolomon1reign", "deathdavid1");

    [Fact]
    public void AReignEndsWhereTheTextEndsItNotAtAnotherKingsDeathFiledUnderIt() =>
        Ordered(
                Told("endjehoram1reign", "End", 12, 8, 17) with { EntityId = 5 },
                Told("deathjehoram1", "Death", 12, 9, 24) with { EntityId = 5 },
                Told("beginahaziah2reign", "Begin", 12, 8, 25))
            .Should().Equal("endjehoram1reign", "beginahaziah2reign", "deathjehoram1");

    [Fact]
    public void TheRankOfANarrowedListIsTheWholeListsRank()
    {
        EventOrder.Facts[] all =
        [
            Told("endahab1reign", "End", 11, 16, 29) with { EntityId = 7 },
            Told("battleatramothgilead", "Battle", 11, 22, 3),
            Told("deathahab1", "Death", 11, 22, 37) with { EntityId = 7 },
        ];

        var rank = EventOrder.Rank(all);

        all.Where(e => e.Kind != "Death").OrderBy(e => rank[e.Slug]).Select(e => e.Slug)
            .Should().Equal("battleatramothgilead", "endahab1reign");
    }

    [Fact]
    public void ABirthComesBeforeADeathAtOneVerse() =>
        Ordered(Told("deathrachel1", "Death", 1, 35, 18), Told("birthbenjamin1", "Birth", 1, 35, 18))
            .Should().Equal("birthbenjamin1", "deathrachel1");

    [Fact]
    public void WhatFollowsEverythingAtItsVerseDoes() =>
        Ordered(Told("begin430yearspromisetocovenant", "Begin", 1, 15, 1), Told("promisetoabram1", "Unique", 1, 15, 1))
            .Should().Equal("promisetoabram1", "begin430yearspromisetocovenant");

    [Fact]
    public void AnEventPlacedByHandStandsAtItsVerse() =>
        Ordered(
                Told("beginothnieljudge", "Begin", 7, 3, 9),
                Unreferenced("begineraofthejudges", "Begin", "The beginning of the period of time Israel was governed by the judges."),
                Told("endjoshuatojudges", "End", 7, 2, 16))
            .Should().Equal("endjoshuatojudges", "begineraofthejudges", "beginothnieljudge");

    [Fact]
    public void WhatNoVersePlacesFollowsEndsFirstAndTheWorldComesLast() =>
        Ordered(
                World("ebla"),
                Unreferenced("jubilee14", "Jubilee", "Jubilee #14"),
                Unreferenced("beginahasuerus1reign", "Begin", "The son of Darius, Xerxes I, succeeded him."),
                Unreferenced("enddarius1reign", "End", "Darius died in October 486 BCE."),
                Told("vashtibanished", "Travel", 17, 1, 1))
            .Should().Equal("vashtibanished", "enddarius1reign", "beginahasuerus1reign", "jubilee14", "ebla");

    [Fact]
    public void UsshersAnnalsKeepHisParagraphsWhateverTheirVerses() =>
        Ordered(
                Annal("ussher-6288", 42, 3, 10),
                Annal("ussher-6282", 42, 4, 19),
                Annal("ussher-6286", 40, 3, 2),
                Annal("ussher-6283", 42, 3, 1))
            .Should().Equal("ussher-6282", "ussher-6283", "ussher-6286", "ussher-6288");

    private static List<string> Ordered(params EventOrder.Facts[] events)
    {
        var places = EventOrder.Of(events);
        return [.. events.Select(e => places[e.Slug]).Order(EventOrder.InYear).Select(p => p.Slug)];
    }

    private static EventOrder.Facts Told(string slug, string kind, int book, int chapter, int verse) =>
        new(slug, Realms.Scripture, BibleData, kind, book, chapter, verse, null, null, 1);

    private static EventOrder.Facts Unreferenced(string slug, string kind, string description) =>
        new(slug, Realms.Scripture, BibleData, kind, null, null, null, description, null, 1);

    private static EventOrder.Facts Annal(string slug, int book, int chapter, int verse) =>
        new(slug, Realms.Scripture, Ussher, "Annal", book, chapter, verse, null, null, 3987);

    private static EventOrder.Facts World(string slug) =>
        new(slug, Realms.World, "Wikidata, query.wikidata.org, CC0", "Founding", null, null, null, null, null, 1);
}
