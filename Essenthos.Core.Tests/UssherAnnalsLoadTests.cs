using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Annals written to a database on top of the encyclopedia, which is the only place the two
/// meet: the chronology the years hang on is declared by the other loader, and the slug space and
/// the event table are shared.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class UssherAnnalsLoadTests : IDisposable
{
    private const int FirstApostolicBook = 40;

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public UssherAnnalsLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        var folder = Path.GetDirectoryName(TestResources.Path("BibleData2026", "BibleData-Person.csv"))!;
        new BibleDataLoader(_db, NullLogger<BibleDataLoader>.Instance).Load(folder).GetAwaiter().GetResult();
        new UssherAnnalsLoader(_db, NullLogger<UssherAnnalsLoader>.Instance).Load(folder).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    /// <summary>
    /// The whole reason this loader exists. The computed chronology dates one New Testament verse
    /// and the world layer dates none of the narrative, so before the Annals the corpus stopped
    /// four centuries before the thing it is for.
    /// </summary>
    [Fact]
    public async Task TheNewTestamentIsDated()
    {
        var dated = await _db.Events
            .CountAsync(e => e.Source == UssherAnnalsLoader.Source && e.CanonicalBook >= FirstApostolicBook);

        dated.Should().BeGreaterThan(300);
    }

    [Fact]
    public async Task NoAnnalIsAnchoredOutsideTheNewTestament() =>
        (await _db.Events
            .Where(e => e.Source == UssherAnnalsLoader.Source)
            .AllAsync(e => e.CanonicalBook >= FirstApostolicBook))
        .Should().BeTrue();

    /// <summary>
    /// A title stands next to the thing it stands for, so a reader can check one against the other
    /// without leaving the row — which is what makes quoting an opening sentence honest rather than
    /// merely short.
    /// </summary>
    [Fact]
    public async Task EveryAnnalCarriesItsParagraphVerbatimAndSaysWhoseTheTitleIs()
    {
        var annals = await _db.Events
            .Where(e => e.Source == UssherAnnalsLoader.Source)
            .ToListAsync();

        annals.Should().OnlyContain(e => e.NameSource == EventNames.Quoted || e.NameSource == EventNames.Generated);
        annals.Should().OnlyContain(e => e.Description != null && e.Description.Length >= e.Name.Length);
        annals.Where(e => e.NameSource == EventNames.Quoted)
            .Should().OnlyContain(e => e.Description!.Contains(FirstWords(e.Name), StringComparison.Ordinal));
    }

    /// <summary>
    /// Enough of a quoted title to place it in the paragraph, and no more: the citations and the
    /// marginal sources are taken out of a title and left in the paragraph, so the two match at the
    /// start and part company later.
    /// </summary>
    private static string FirstWords(string name) =>
        name.Length <= 20 ? name.TrimEnd('…') : name[..20];

    /// <summary>
    /// The rows the other loader wrote are titled by their own dataset, and nothing here changes
    /// that: the column separates the two kinds of name rather than casting doubt on both.
    /// </summary>
    [Fact]
    public async Task TheDatasetsThatTitleTheirOwnRowsStillSaySo() =>
        (await _db.Events
            .Where(e => e.Source != UssherAnnalsLoader.Source)
            .AllAsync(e => e.NameSource == EventNames.FromTheSource))
        .Should().BeTrue();

    /// <summary>
    /// His year hangs on his own reckoning and never on the default one, which computes from the
    /// genealogies and has no opinion about anything after Artaxerxes.
    /// </summary>
    [Fact]
    public async Task HisYearsAreHisOwnReckoningsAndNobodyElses()
    {
        var elsewhere = await _db.EventDates
            .Where(d => d.Event!.Source == UssherAnnalsLoader.Source)
            .CountAsync(d => d.Chronology!.Slug != "ussher");

        elsewhere.Should().Be(0);
    }

    [Fact]
    public async Task EveryDateCitesTheParagraphItCameFrom() =>
        (await _db.EventDates
            .Where(d => d.Event!.Source == UssherAnnalsLoader.Source)
            .AllAsync(d => d.Citation != null && d.Citation.StartsWith("¶")))
        .Should().BeTrue();

    /// <summary>
    /// Nothing is loaded twice. The guard is on the Annals' own rows rather than on the
    /// encyclopedia's, so that they can reach a database that already holds one.
    /// </summary>
    [Fact]
    public async Task ASecondRunLoadsNothing()
    {
        var before = await _db.Events.CountAsync();

        var outcome = await new UssherAnnalsLoader(_db, NullLogger<UssherAnnalsLoader>.Instance)
            .Load(Path.GetDirectoryName(TestResources.Path("BibleData2026", "BibleData-Person.csv"))!);

        outcome.AlreadyLoaded.Should().BeTrue();
        (await _db.Events.CountAsync()).Should().Be(before);
    }
}
