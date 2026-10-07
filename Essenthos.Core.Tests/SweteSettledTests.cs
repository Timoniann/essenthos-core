using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Swete;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The verses settled one by one against the page: a running head and lines of the apparatus read into
/// the text, and the chapter openings the transcription lost.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SweteSettledTests(Swete swete) : IClassFixture<Swete>
{
    private const int Numbers = 4;
    private const int Judges = 7;
    private const int FirstSamuel = 9;

    [Theory]
    [InlineData(FirstSamuel, 8, 2, "καὶ ταῦτα τὰ ὀνόματα τῶν υἱῶν αὐτοῦ· πρωτότοκος Ἰωήλ, καὶ ὄνομα τοῦ δευτέρου Ἀβιά, δικασταὶ ἐν Βηρσάβεε.")]
    [InlineData(FirstSamuel, 11, 11, "καὶ ἐγενήθη μετὰ τὴν οὔριον καὶ ἔθετο Σαοὺλ τὸν λαὸν εἰς τρεῖς ἀρχάς, καὶ εἰσπορεύονται μέσον τῆς "
                                     + "παρεμβολῆς ἐν φυλακῇ τῇ ἑωθινῇ, κοὶ ἔτυπτον τοὺς υἱοὺς Ἄμμων ἕως διεθερμάνθη ἡ ἡμέρα· καὶ "
                                     + "ἐγενήθησαν, οἱ ὑπολελιμμένοι διεσπάρησαν, καὶ οὐχ ὑπελείφθησαν ἐν αὐτοῖς δύο κατὰ τὸ αὐτό.")]
    [InlineData(Numbers, 16, 50, "καὶ ἐπέστρεψεν Ἀαρὼν πρὸς Μωυσῆν ἐπὶ τὴν θύραν τῆς σκηνῆς τοῦ μαρτυρίου, καὶ ἐκόπασεν ἡ θραῦσις.")]
    public void TheVerseReadsAsThePagePrintsIt(int book, int chapter, int verse, string printed) =>
        Swete.Text(swete.Verse(book, chapter, verse)).Should().Be(printed);

    /// <summary>
    /// Judges 18:8 runs over a page, and the transcription read the apparatus of Alexandrinus at the
    /// page's foot into it: the verse is its own words again, ending where Swete's does.
    /// </summary>
    [Fact]
    public void TheApparatusIsNotTheText()
    {
        var text = Swete.Text(swete.Verse(Judges, 18, 8));

        text.Should().StartWith("καὶ ἦλθον οἱ πέντε ἄνδρες πρὸς τοὺς ἀδελφοὺς αὐτῶν εἰς Σαραὰ καὶ Ἐσθαόλ, καὶ εἶπον τοῖς ἀδελφοῖς αὐτῶν Τί ὑμεῖς κάθησθε");
        swete.Verse(Judges, 18, 8).Words.Should().HaveCount(21);
    }

    [Fact]
    public void EverySettlementChangesSomethingAndCitesItsPage() =>
        SweteSettled.All.Should().NotBeEmpty().And.OnlyContain(r =>
            r.Digitised != r.Printed && r.Why.Contains("vol. 1", StringComparison.Ordinal)
                                     && r.Why.Contains("scan leaf", StringComparison.Ordinal));
}

/// <summary>
/// The settled verses written into a Swete loaded before them, as the corpus this machine holds is:
/// the transcription's verses as the earlier restorations left them, with the chapter numbers already
/// taken out.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SweteSettledLoadTests : IDisposable
{
    private static readonly (string File, int Canonical, string Name)[] Books =
        [("04.Numeri", 4, "Numbers"), ("08.Judices", 7, "Judges"), ("11.Regnorum_I", 9, "1 Samuel")];

    private readonly AppDbContext _db;
    private readonly SweteRestorationLoader _loader;

    public SweteSettledLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new SweteRestorationLoader(_db, NullLogger<SweteRestorationLoader>.Instance);
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM text");

    private static SweteBook Read(string file, IReadOnlyList<SweteRestoration>? set, bool keepChapterMarkers)
    {
        var lines = SweteDivisions.Lines(file, File.ReadLines(Path.Combine(TestResources.SweteFolder, SweteTextSource.FileName(file))));
        return SweteReader.Read(set is null ? SweteRestorations.Apply(file, lines) : SweteRestorations.Apply(file, lines, set),
            keepChapterMarkers);
    }

    private static IEnumerable<(int Chapter, int Verse)> Restored(string file) =>
        SweteRestorations.All.Where(r => r.Book == file && r.Label.Length == 0).Select(r => (r.Chapter, r.Verse)).Distinct();

    private static IReadOnlyList<SweteWord> Words(SweteBook book, int chapter, int verse) =>
        book.Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse && v.Label.Length == 0).Words;

    /// <summary>Every restored verse of the three books as the last pass before these left it, numbers out.</summary>
    private Text Loaded()
    {
        var text = new Text { Slug = SweteTextSource.Slug, Name = "Swete", Kind = TextKind.CriticalEdition, Language = "grc" };
        _db.Texts.Add(text);
        foreach (var (file, canonical, name) in Books)
        {
            var earlier = Read(file, SweteRestorations.Earlier[^1], keepChapterMarkers: false);
            _db.AddBook(text, canonical, name, [.. Restored(file).Select(at =>
                (at.Chapter, at.Verse, Words(earlier, at.Chapter, at.Verse).Select(w => w.Surface).ToArray()))]);
        }

        _db.SaveChanges();
        foreach (var (file, canonical, _) in Books)
        {
            var earlier = Read(file, SweteRestorations.Earlier[^1], keepChapterMarkers: false);
            foreach (var (chapter, verse) in Restored(file))
            {
                var words = Words(earlier, chapter, verse);
                var stored = _db.Words.Where(w => w.TextId == text.Id && w.Verse!.Book!.CanonicalOrdinal == canonical
                                                  && w.Verse.ChapterNumber == chapter && w.Verse.Number == verse)
                    .OrderBy(w => w.Position).ToList();
                for (var at = 0; at < words.Count; at++)
                {
                    stored[at].Trailer = words[at].Trailer;
                }
            }
        }

        _db.SaveChanges();
        return text;
    }

    private Word At(Text text, int canonical, int chapter, int verse, string surface) =>
        _db.Words.Single(w => w.TextId == text.Id && w.Verse!.Book!.CanonicalOrdinal == canonical
                              && w.Verse.ChapterNumber == chapter && w.Verse.Number == verse && w.Surface == surface);

    private string Stored(Text text, int canonical, int chapter, int verse) =>
        string.Concat(_db.Words.AsNoTracking()
            .Where(w => w.TextId == text.Id && w.Verse!.Book!.CanonicalOrdinal == canonical
                        && w.Verse.ChapterNumber == chapter && w.Verse.Number == verse)
            .OrderBy(w => w.Position)
            .Select(w => w.Surface + w.Trailer)
            .ToList());

    /// <summary>
    /// An empty verse takes its opening line, a verse the boundary pass already took the number out of
    /// is not given it back, and the words the page shares with the transcription keep their rows and
    /// links while a token of the apparatus goes with the matcher's link it stood in alone.
    /// </summary>
    [Fact]
    public async Task EachVerseReadsAsThePageAndWhatItSharesKeepsItsRows()
    {
        var text = Loaded();
        Stored(text, 4, 17, 1).Should().BeEmpty();
        var brenton = Corpus.Add(_db, "GRCBRENT", TextKind.CriticalEdition, "grc", (1, 1, ["ἑωθινῇ", "Αμμανίτης"]));
        _db.SaveChanges();
        var kept = At(text, 9, 11, 11, "ἑωθινῇ");
        var apparatus = At(text, 9, 11, 11, "Αμανιτης");
        var keptLink = Link(text, brenton, kept, _db.WordAt(brenton, 1, 1, 1), "a test");
        var apparatusLink = Link(text, brenton, apparatus, _db.WordAt(brenton, 1, 1, 2), SeptuagintLinkLoader.Source);

        var outcome = await _loader.Load(TestResources.SweteFolder);

        outcome.Verses.Should().Be(SweteSettled.All.Select(r => (r.Book, r.Chapter, r.Verse)).Distinct().Count(),
            "the earlier restorations are already there");
        foreach (var (file, canonical, _) in Books)
        {
            var cold = Read(file, null, keepChapterMarkers: false);
            foreach (var (chapter, verse) in Restored(file))
            {
                Stored(text, canonical, chapter, verse).Should()
                    .Be(string.Concat(Words(cold, chapter, verse).Select(w => w.Surface + w.Trailer)), $"{file} {chapter}:{verse}");
            }
        }

        Stored(text, 4, 16, 50).Should().NotContain("XVII");
        (await _db.Words.AsNoTracking().SingleAsync(w => w.Id == kept.Id)).Position.Should().Be(22);
        (await _db.Links.AnyAsync(l => l.Id == keptLink.Id)).Should().BeTrue();
        (await _db.Links.AnyAsync(l => l.Id == apparatusLink.Id)).Should().BeFalse();
        (await _db.Texts.AsNoTracking().SingleAsync(t => t.Id == text.Id)).RightsNote.Should().Contain(SweteSettled.Note);

        (await _loader.Load(TestResources.SweteFolder)).Verses.Should().Be(0);
    }

    private Link Link(Text from, Text to, Word fromWord, Word toWord, string source)
    {
        var link = new Link
        {
            FromTextId = from.Id, ToTextId = to.Id, Relation = LinkRelation.Equals, Method = LinkMethod.Lexical,
            Confidence = 0.9, Provenance = new() { Source = source },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = fromWord, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = toWord, Side = LinkSide.To });
        _db.SaveChanges();
        return link;
    }
}

/// <summary>
/// 3 Kingdoms 16:1 in a corpus loaded before it was divided from 15:34: its words are moved, rows and
/// all, whether the chapter's number still stands after them or the boundary pass already took it out.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SweteChapterOpeningMoveTests : IClassFixture<WitnessDatabase>, IDisposable
{
    private const int Kings = 11;

    private readonly AppDbContext db;

    public SweteChapterOpeningMoveTests(WitnessDatabase fixture) => db = fixture.NewContext();

    public void Dispose() => db.Dispose();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheWordsMoveWithTheirRowsAndASecondPassWritesNothing(bool numbered)
    {
        await db.Database.ExecuteSqlRawAsync("TRUNCATE text, entity RESTART IDENTITY CASCADE");
        var read = SweteTextSource.Read(TestResources.SweteFolder);
        var book = read.Books.Single(b => b.CanonicalOrdinal == Kings);
        var fifteen = book.Chapters.Single(c => c.Number == 15);
        var sixteen = book.Chapters.Single(c => c.Number == 16);
        var opening = sixteen.Verses[0].Words;
        WordDraft[] number = numbered ? [new WordDraft("XVI", " ")] : [];
        var runTogether = fifteen with
        {
            Verses = [.. fifteen.Verses.SkipLast(1),
                fifteen.Verses[^1] with { Words = [.. fifteen.Verses[^1].Words, .. opening, .. number] }],
        };
        var emptied = sixteen with { Verses = [sixteen.Verses[0] with { Words = number }, .. sixteen.Verses.Skip(1)] };
        await new CorpusLoader(db, NullLogger<CorpusLoader>.Instance).Load(new TextSource(read.Definition,
            [book with { Chapters = [runTogether, emptied] }]));
        var lordAt = fifteen.Verses[^1].Words.Count + 4;
        var lord = await db.Words.SingleAsync(w => w.Verse!.ChapterNumber == 15 && w.Verse.Number == 34 && w.Position == lordAt);
        lord.Surface.Should().Be("Κυρίου");
        var entity = new Entity { Kind = EntityKind.Person, Slug = "yhvh", Name = "the Lord", SourceId = "yhvh", Source = "a test" };
        db.Entities.Add(entity);
        db.WordEntities.Add(new WordEntity
        {
            WordId = lord.Id, Entity = entity, Method = LinkMethod.Lexical, Confidence = 0.9, Source = "a test",
        });
        await db.SaveChangesAsync();

        var loader = new EditionBoundaryRepairLoader(db);
        var outcome = await loader.Load(TestResources.Folder(string.Empty));

        outcome.MovedWords.Should().Be(opening.Count);
        outcome.RemovedWords.Should().Be(numbered ? 2 : 0);
        foreach (var chapter in new[] { fifteen, sixteen })
        foreach (var verse in chapter.Verses)
            (await db.Words.AsNoTracking().Where(w => w.Verse!.ChapterNumber == chapter.Number && w.Verse.Number == verse.Number
                                                      && w.Verse.Label == verse.Label)
                    .OrderBy(w => w.Position).Select(w => w.Surface + w.Trailer).ToListAsync())
                .Should().Equal(verse.Words.Select(w => w.Surface + w.Trailer), $"3 Kingdoms {chapter.Number}:{verse.Number}{verse.Label}");
        var moved = await db.Words.AsNoTracking().Include(w => w.Verse).SingleAsync(w => w.Id == lord.Id);
        (moved.Verse!.ChapterNumber, moved.Verse.Number, moved.Position).Should().Be((16, 1, 4));
        (await db.WordEntities.AnyAsync(we => we.WordId == lord.Id)).Should().BeTrue();

        var again = await loader.Load(TestResources.Folder(string.Empty));
        (again.RemovedWords, again.MovedWords, again.RewrittenWords).Should().Be((0, 0, 0));
    }
}
