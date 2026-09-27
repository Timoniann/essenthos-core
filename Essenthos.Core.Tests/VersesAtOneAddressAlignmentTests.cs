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
/// Several verses of one text can stand at one address — the lettered verses the Greek prints after
/// a numbered one, a verse covering the address beside the verse standing there — and the aligner
/// reads them as one sentence. Each has to be read whole, in the order the edition writes them, or
/// the sentence is the first word of each verse, then the second of each.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class VersesAtOneAddressAlignmentTests : IDisposable
{
    private const string Slug = "LXX";

    private readonly AppDbContext _db;
    private readonly AlignmentPipeline _aligner;
    private readonly Text _text;

    public VersesAtOneAddressAlignmentTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _aligner = new AlignmentPipeline(_db, NullLogger<AlignmentPipeline>.Instance);

        _text = Corpus.Add(_db, Slug, TextKind.Translation, "eng",
            (1, 1, ["in", "the", "beginning"]), (1, 2, ["and", "the", "earth"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    [Fact]
    public async Task LetteredVersesAreReadWholeInTheOrderTheEditionWritesThem()
    {
        // The edition writes 2a before 2b whichever of the two was stored first.
        Lettered("b", 4, ["and", "darkness"]);
        Lettered("a", 3, ["was", "without", "form"]);
        await _db.SaveChangesAsync();

        (await Read((1, 1, 2))).Should().Equal("and", "the", "earth", "was", "without", "form", "and", "darkness");
    }

    [Fact]
    public async Task AVerseCoveringTheAddressIsReadWholeBesideTheVerseStandingThere()
    {
        _db.VerseReferences.Add(new VerseReference
        {
            Verse = _db.VerseAt(_text, 1, 1),
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 2,
            IsPrimary = false,
        });
        await _db.SaveChangesAsync();

        (await Read((1, 1, 2))).Should().Equal("in", "the", "beginning", "and", "the", "earth");
    }

    private void Lettered(string label, int sequence, string[] words)
    {
        var numbered = _db.VerseAt(_text, 1, 2);
        var verse = new Verse
        {
            TextId = _text.Id,
            BookId = numbered.BookId,
            ChapterId = numbered.ChapterId,
            ChapterNumber = 1,
            Number = 2,
            Label = label,
            Sequence = sequence,
        };
        _db.Verses.Add(verse);
        _db.VerseReferences.Add(new VerseReference
        {
            Verse = verse, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 2, IsPrimary = true,
        });
        for (var position = 0; position < words.Length; position++)
        {
            _db.Words.Add(new Word
            {
                TextId = _text.Id,
                Verse = verse,
                Position = position + 1,
                Surface = words[position],
                Trailer = " ",
            });
        }
    }

    /// <summary>The words the aligner is handed at an address, as the text writes them.</summary>
    private async Task<List<string>> Read((int, int, int) address)
    {
        var surfaces = await _db.Words.ToDictionaryAsync(w => w.Id, w => w.Surface);
        var words = await _aligner.Named(Slug, Slug, null, CancellationToken.None);
        return [.. words[address].Select(word => surfaces[word.Id])];
    }
}
