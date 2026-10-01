using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A word the translators put there, asked for from the endpoint a reader's click reaches.
///
/// The fact has one home: every edition's mark -- the Synodal's and the Berean's brackets, the King
/// James' italics -- is a supplied group over the words. An expands link is what that means against
/// one other text, and between two manuscripts it is a plus nobody inserted, so a link alone does not
/// make a word supplied.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SuppliedWordTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _english;
    private readonly Text _hebrew;

    public SuppliedWordTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _hebrew = Corpus.Add(_db, "BHSA", TextKind.ManuscriptTradition, "hbo",
            (1, 1, ["בְּ", "רֵאשִׁית"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (1, 1, ["In", "the", "beginning", "was"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task AWordInASuppliedSpanSaysSo()
    {
        Supply(4);

        (await WordEndpoints.Supplied(_db, WordId(4), default))
            .Should().BeTrue();
    }

    [Fact]
    public async Task AWordTheEditionDidNotSupplySaysNothing()
    {
        Supply(4);

        (await WordEndpoints.Supplied(_db, WordId(1), default))
            .Should().BeFalse();
    }

    /// <summary>
    /// An <c>expands</c> link says the other text has nothing here, which is a comparison's finding
    /// and not the edition's own statement; only the group says the translators supplied the word.
    /// </summary>
    [Fact]
    public async Task AnExpandsLinkAloneDoesNotSayTheWordWasSupplied()
    {
        Expands(4);

        (await WordEndpoints.Supplied(_db, WordId(4), default))
            .Should().BeFalse();
    }

    [Fact]
    public async Task AWordTheEditorsDoubtIsNotSupplied()
    {
        Supply(WordGroupKind.Doubtful, 4);

        (await WordEndpoints.Supplied(_db, WordId(4), default))
            .Should().BeFalse();
    }

    /// <summary>
    /// The direction is the whole claim. A word on the to side of an <c>expands</c> link is a word
    /// the other text supplied, not this one -- reading the side would say every Hebrew word
    /// beside a supplied English one was itself supplied.
    /// </summary>
    [Fact]
    public async Task AWordOnTheFarSideOfAnExpandsLinkIsNotSupplied()
    {
        var link = Expands(4);
        _db.LinkWords.Add(new LinkWord
        {
            LinkId = link.Id,
            WordId = _db.WordAt(_hebrew, 1, 1, 1).Id,
            Side = LinkSide.To,
        });
        _db.SaveChanges();

        (await WordEndpoints.Supplied(_db, _db.WordAt(_hebrew, 1, 1, 1).Id, default))
            .Should().BeFalse();
    }

    private long WordId(int position) => _db.WordAt(_english, 1, 1, position).Id;

    private void Supply(params int[] positions) => Supply(WordGroupKind.Supplied, positions);

    private void Supply(WordGroupKind kind, params int[] positions)
    {
        var group = new WordGroup
        {
            TextId = _english.Id,
            Kind = kind,
            Position = 1,
        };
        _db.WordGroups.Add(group);
        _db.SaveChanges();

        foreach (var position in positions)
        {
            _db.WordGroupWords.Add(new WordGroupWord { WordGroupId = group.Id, WordId = WordId(position) });
        }

        _db.SaveChanges();
    }

    private Link Expands(int position)
    {
        var link = new Link
        {
            FromTextId = _english.Id,
            ToTextId = _hebrew.Id,
            Relation = LinkRelation.Expands,
            Method = LinkMethod.StatedBySource,
            Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.SaveChanges();

        _db.LinkClaims.Add(new LinkClaim
        {
            LinkId = link.Id,
            Method = link.Method,
            Provenance = link.Provenance,
        });
        _db.LinkWords.Add(new LinkWord { LinkId = link.Id, WordId = WordId(position), Side = LinkSide.From });
        _db.SaveChanges();

        return link;
    }
}
