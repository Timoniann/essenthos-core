using System.Text.Json;
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
/// The marks <c>/v1/verses?entity=</c> puts on a verse: which words name the entity, and where in
/// the verse's text they sit.
///
/// The offsets are the whole contract. A client slices the text with them, so an offset one off is
/// a highlight on the space before the name and the last letter left out — wrong in a way nobody
/// reports, because it still looks like a highlight.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class VerseMarkTests : IDisposable
{
    private const string Resolution = "a test resolution";

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _english;
    private readonly Entity _abram;

    public VerseMarkTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (12, 4, ["So", "Abram", "departed", "and", "Lot", "went", "with", "him"]));
        _db.SaveChanges();

        _abram = Person("abram", "Abram");
        var lot = Person("lot", "Lot");
        var lotan = Person("lotan", "Lotan");

        Names(Word(2), _abram, LinkMethod.StrongNumber, 0.9);
        Names(Word(5), lot, LinkMethod.Lexical, 0.8);
        Names(Word(5), lotan, LinkMethod.Lexical, 0.8);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void MarksANameAtItsPlaceInTheText()
    {
        var marks = VerseEndpoints.Marks(
        [
            new("So", " ", false), new("Abram", " ", true), new("departed", ".", false),
        ]);

        marks.Should().Equal(new VerseMarkResponse(3, 8));
        "So Abram departed."[3..8].Should().Be("Abram");
    }

    /// <summary>
    /// The text is the words joined and trimmed, so whatever the trim takes off the front is taken
    /// off every offset — here an article BHSA records and prints no letters for.
    /// </summary>
    [Fact]
    public void CountsFromWhereTheTrimmedTextBegins()
    {
        var marks = VerseEndpoints.Marks(
        [
            new(string.Empty, " ", false), new("Abram", " ", true),
        ]);

        marks.Should().Equal(new VerseMarkResponse(0, 5));
    }

    [Fact]
    public void MarksNothingForAWordPrintedWithNoLetters()
    {
        var marks = VerseEndpoints.Marks(
        [
            new("to", string.Empty, false), new(string.Empty, " ", true), new("Haran", string.Empty, false),
        ]);

        marks.Should().BeEmpty();
    }

    /// <summary>One name written as two words is one mark, over a space or a maqaf alike.</summary>
    [Theory]
    [InlineData("Kirjath", " ", "arba", "Kirjath arba")]
    [InlineData("בֵּֽית", "־", "אֵ֑ל", "בֵּֽית־אֵ֑ל")]
    public void JoinsTwoMarkedWordsWithNothingButASpaceOrADashBetween(
        string first, string between, string second, string text)
    {
        var marks = VerseEndpoints.Marks(
        [
            new("to", " ", false), new(first, between, true), new(second, ".", true),
        ]);

        marks.Should().Equal(new VerseMarkResponse(3, 3 + text.Length));
    }

    [Fact]
    public void KeepsTwoNamingsApartWhereAWordStandsBetweenThem()
    {
        var marks = VerseEndpoints.Marks(
        [
            new("Abram", " ", true), new("and", " ", false), new("Abram", ", ", true), new("Abram", ".", true),
        ]);

        marks.Should().Equal(
            new VerseMarkResponse(0, 5), new VerseMarkResponse(10, 15), new VerseMarkResponse(17, 22));
    }

    /// <summary>
    /// A character outside the Basic Multilingual Plane is two UTF-16 units, and two is what a
    /// JavaScript string counts it as — which is the index the client slices with.
    /// </summary>
    [Fact]
    public void CountsInUtf16UnitsAsAJavaScriptStringDoes()
    {
        var marks = VerseEndpoints.Marks(
        [
            new("𝔄", " ", false), new("Abram", string.Empty, true),
        ]);

        marks.Should().Equal(new VerseMarkResponse(3, 8));
    }

    [Fact]
    public async Task NamesTheWordsTheCorpusSettlesOnTheEntity()
    {
        var naming = await VerseEndpoints.Naming(_db, Words(), _abram.Slug, default);

        naming.Should().BeEquivalentTo([Word(2).Id]);
    }

    /// <summary>
    /// Two resolutions of the same standing and the same confidence name two people, and the word
    /// card shows neither. The list agrees with it rather than picking one.
    /// </summary>
    [Fact]
    public async Task MarksNoWordTwoEqualClaimsDisagreeAbout()
    {
        (await VerseEndpoints.Naming(_db, Words(), "lot", default)).Should().BeEmpty();
        (await VerseEndpoints.Naming(_db, Words(), "lotan", default)).Should().BeEmpty();
    }

    /// <summary>
    /// Joshua 10:1 names Jerusalem in the King James with no link to the Hebrew, so no annotation
    /// reaches the word; it is spelled as the text spells Jerusalem everywhere else, and is marked.
    /// </summary>
    [Fact]
    public void MarksAVerseTheAnnotationsMissedWhereTheTextSpellsTheName()
    {
        var spelled = VerseEndpoints.Spelled(
            [new("king", " ", false), new("of", " ", false), new("Jerusalem", " ", false), new("had", ".", false)],
            new HashSet<string> { "jerusalem" });

        VerseEndpoints.Marks(spelled).Should().Equal(new VerseMarkResponse(8, 17));
    }

    [Fact]
    public void MarksATwoWordNameSpelledAsOne()
    {
        var spelled = VerseEndpoints.Spelled(
            [new("to", " ", false), new("Beth", "-", false), new("el", ".", false)],
            new HashSet<string> { "bethel" });

        VerseEndpoints.Marks(spelled).Should().Equal(new VerseMarkResponse(3, 10));
    }

    /// <summary>A verse the corpus marked keeps its marks and gets none by spelling beside them.</summary>
    [Fact]
    public void SpellingNeverAddsToWhatTheAnnotationsSay()
    {
        var spelled = VerseEndpoints.Spelled(
            [new("Abram", " ", true), new("and", " ", false), new("Abram", ".", false)],
            new HashSet<string> { "abram" });

        VerseEndpoints.Marks(spelled).Should().Equal(new VerseMarkResponse(0, 5));
    }

    /// <summary>
    /// <em>God</em> in Genesis 1:1 is annotated to YHVH and is also the word Elohim, which no word
    /// is annotated to: the list of Elohim marks the word that renders the Hebrew carrying its number.
    /// </summary>
    [Fact]
    public async Task MarksAWordForGodByTheNumberItRenders()
    {
        var hebrew = Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo", (1, 1, ["בְּרֵאשִׁית", "בָּרָא", "אֱלֹהִים"]));
        var english = Corpus.Add(_db, "test-english", TextKind.Translation, "eng", (1, 1, ["In", "beginning", "God", "created"]));
        _db.SaveChanges();
        _db.WordAt(hebrew, 1, 1, 3).StrongNumber = "H430";
        var elohim = new Entity { Kind = EntityKind.Term, Slug = "elohim", Name = "Elohim", SourceId = "elohim", Source = "a test" };
        elohim.Names.Add(new EntityName { Label = "Elohim", HebrewStrongNumber = "H430" });
        _db.Entities.Add(elohim);
        var god = _db.WordAt(english, 1, 1, 3);
        var link = new Link
        {
            FromTextId = english.Id, ToTextId = hebrew.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StrongNumber, Confidence = 0.9, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = god, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(hebrew, 1, 1, 3), Side = LinkSide.To });
        _db.SaveChanges();

        var words = _db.Words.Where(w => w.TextId == english.Id || w.TextId == hebrew.Id).Select(w => w.Id).ToList();
        (await VerseEndpoints.Naming(_db, words, "elohim", default)).Should()
            .BeEquivalentTo([god.Id, _db.WordAt(hebrew, 1, 1, 3).Id]);
    }

    [Fact]
    public void SendsMarksOnlyWhereTheyWereAskedFor()
    {
        var book = new BookRefResponse(1, "Genesis", "genesis");
        var answer = new VerseTextListResponse(
            "KJV",
            [
                new VerseTextResponse(book, 12, 4, "So Abram departed") { Marks = [new VerseMarkResponse(3, 8)] },
                new VerseTextResponse(book, 12, 5, "And he took Sarai"),
            ],
            []);

        // As the host serializes it: web defaults, resolved through the generated context.
        var json = JsonSerializer.Serialize(
            answer,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = AppJsonSerializerContext.Default });

        json.Should().Contain("\"marks\":[{\"start\":3,\"end\":8}]");
        json.Should().Contain("\"marks\":null");
    }

    private Entity Person(string slug, string name)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = name, SourceId = slug, Source = "a test",
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private void Names(Word word, Entity entity, LinkMethod method, double confidence) =>
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id,
            EntityId = entity.Id,
            Method = method,
            Confidence = confidence,
            Source = Resolution,
        });

    private Word Word(int position) => _db.WordAt(_english, 12, 4, position);

    private List<long> Words() => [.. _db.Words.Where(w => w.TextId == _english.Id).Select(w => w.Id)];
}
