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
/// What a Ge'ez word is given to mean when a chapter is read: the aligned Greek word's meaning,
/// carried with that word and the link, and Dillmann's entry where the word's letters or its Greek
/// settle which.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class GeezMeaningTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _geez;

    public GeezMeaningTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        _geez = Corpus.Add(_db, "GEEZ81", TextKind.PrintedEdition, "gez", (1, 1, ["ቃልየ", "ገብሩ", "ሎሙ"]));
        var greek = Corpus.Add(_db, "GRCBRENT", TextKind.PrintedEdition, "grc", (1, 1, ["λόγος", "ἐποίησαν"]));
        _db.SaveChanges();

        _db.WordAt(greek, 1, 1, 1).Lemma = "λόγος";
        _db.WordAt(greek, 1, 1, 2).Lemma = "ποιέω";
        _db.LexiconGlosses.AddRange(
            new LexiconGloss { Entry = "G3056", StrongNumber = "G3056", Lemma = "λόγος", Gloss = "word", Source = "a test" },
            new LexiconGloss { Entry = "G4160", StrongNumber = "G4160", Lemma = "ποιέω", Gloss = "to do", Source = "a test" });
        _db.GeezLexiconEntries.AddRange(
            Headword("Lword", "ቃል", ["vox", "verbum"], ["λαλιά"]),
            Headword("Lmade", "ገብረ", ["fecit"], ["ποιεῖν"]),
            Headword("Lservant", "ገብር", ["servus"], ["δοῦλος"]));

        Align(_db.WordAt(_geez, 1, 1, 1), _db.WordAt(greek, 1, 1, 1), 0.8);
        Align(_db.WordAt(_geez, 1, 1, 2), _db.WordAt(greek, 1, 1, 2), 0.6);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task TheMeaningThroughTheGreekCarriesTheGreekWordAndTheLink()
    {
        var word = await Read(1);
        word.ThroughGreek.Should().BeEquivalentTo(
            new ThroughGreekResponse("GRCBRENT", "λόγος", "λόγος", ["word"], "aligner", 0.8));
    }

    [Fact]
    public async Task DillmannsEntryIsGivenWhereTheLettersOrTheGreekSettleIt()
    {
        (await Read(1)).GeezEntry.Should().BeEquivalentTo(new GeezEntryResponse("ቃል", ["vox", "verbum"], ["λαλιά"], "form"));
        (await Read(2)).GeezEntry.Should().BeEquivalentTo(new GeezEntryResponse("ገብረ", ["fecit"], ["ποιεῖν"], "greek"));
    }

    [Fact]
    public async Task AWordNothingReachesIsGivenNothing()
    {
        var word = await Read(3);
        word.ThroughGreek.Should().BeNull();
        word.GeezEntry.Should().BeNull();
    }

    private async Task<TextWordResponse> Read(int position)
    {
        var chapter = await Texts.ReadChapter(_db, _geez.Id, 1, 1, default);
        return chapter.Single().Words[position - 1];
    }

    private void Align(Word from, Word to, double confidence)
    {
        var link = new Link
        {
            FromTextId = from.TextId,
            ToTextId = to.TextId,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner,
            Confidence = confidence,
            Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = from, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = to, Side = LinkSide.To });
    }

    private static GeezLexiconEntry Headword(string entry, string headword, string[] latin, string[] greek) => new()
    {
        Entry = entry,
        Headword = headword,
        Forms = [headword],
        Consonants = [GeezLexicon.ConsonantsOf(headword)],
        Latin = latin,
        Greek = greek,
        Source = "a test",
    };
}
