using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests.Lexicon;

/// <summary>
/// A number's places paged after or before a place rather than counted off from the start: walked either way, page by page,
/// they are the list in its order (text, then the frame's address, then position), each place once, and the first and last
/// pages say there is nothing before or after them.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class StrongOccurrencePagingTests : IDisposable
{
    private const string Number = "H1254";

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public StrongOccurrencePagingTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        var hebrew = Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo",
            (1, 1, ["בָּרָא", "אֱלֹהִים", "בָּרָא"]),
            (1, 2, ["בָּרָא"]),
            (2, 3, ["בָּרָא", "לַעֲשׂוֹת"]));
        var english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (1, 1, ["created", "God"]),
            (1, 2, ["created"]));
        _db.SaveChanges();
        foreach (var word in _db.Words.Where(w => w.Surface == "בָּרָא" || w.Surface == "created"))
        {
            word.StrongNumber = Number;
        }

        // The Hebrew's 2:3 stands before its 1:2 in the frame, so the list is not in the edition's own order.
        var placed = _db.VerseReferences.Single(r => r.Verse!.TextId == hebrew.Id && r.Verse.ChapterNumber == 2);
        placed.CanonicalChapter = 1;
        placed.CanonicalVerse = 1;
        _ = english;
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task PagedAfterAndBeforeAPlaceTheyAreTheWholeListInItsOrder()
    {
        var whole = (await Page(new OccurrencePaging(0, 200))).Items.Select(i => i.WordId).ToList();
        whole.Should().HaveCount(6);
        whole.Should().Equal((await Page(new OccurrencePaging(0, 200))).Items.OrderBy(i => i.Corpus, StringComparer.Ordinal)
            .ThenBy(i => i.BookOrdinal).ThenBy(i => i.Chapter).ThenBy(i => i.Verse).ThenBy(i => i.Position).ThenBy(i => i.WordId)
            .Select(i => i.WordId));

        var forward = new List<long>();
        var page = await Page(new OccurrencePaging(0, 2));
        page.Previous.Should().BeNull("the first page starts the list");
        forward.AddRange(page.Items.Select(i => i.WordId));
        while (page.Next is { } next)
        {
            OccurrenceCursor.TryParse(next, out var after).Should().BeTrue();
            page = await Page(new OccurrencePaging(0, 2, After: after));
            page.Previous.Should().NotBeNull();
            page.Total.Should().Be(6);
            forward.AddRange(page.Items.Select(i => i.WordId));
        }

        forward.Should().Equal(whole);

        var backward = new List<long>(page.Items.Select(i => i.WordId));
        while (page.Previous is { } previous)
        {
            OccurrenceCursor.TryParse(previous, out var before).Should().BeTrue();
            page = await Page(new OccurrencePaging(0, 2, Before: before));
            page.Next.Should().NotBeNull();
            backward.InsertRange(0, page.Items.Select(i => i.WordId));
        }

        backward.Should().Equal(whole);

        (await Page(new OccurrencePaging(4, 2))).Items.Select(i => i.WordId).Should().Equal(whole[4..6], "an offset still answers for one release");
    }

    [Fact]
    public async Task TheBookMapCountsTheWordsOfEachTextAsTheListDoes()
    {
        var kjv = _db.Texts.Single(t => t.Slug == "KJV");

        var map = await StrongBookEndpoints.Map(_db, Number, [new TextEntry(kjv.Id, kjv.Slug, [1, 2], true)], default);

        map.ByText.Select(t => (t.Corpus, t.Count)).Should().Equal(("BHSA", 4), ("KJV", 2));
        map.ByText.Should().Equal((await Page(new OccurrencePaging(0, 1))).ByText);
    }

    [Fact]
    public void ACursorReadsBackAsWritten()
    {
        var cursor = new OccurrenceCursor("KJV", 19, 119, 0, 3, 4503599627370495);
        OccurrenceCursor.TryParse(cursor.ToString(), out var read).Should().BeTrue();
        read.Should().Be(cursor);
        OccurrenceCursor.TryParse("KJV:19.119", out _).Should().BeFalse();
        OccurrenceCursor.TryParse("19.119.1.3.4", out _).Should().BeFalse();
    }

    private Task<StrongOccurrenceListResponse> Page(OccurrencePaging paging) =>
        StrongEndpoints.OccurrencePage(_db, Number, _db.Words.Where(w => w.StrongNumber == Number), paging, default);
}
