using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// A Hebrew word that refers by its pronoun suffix (<c>מְלַאכְתּוֹ</c> "his work", by its <c>וֹ</c>): the original's word says
/// its suffix refers to God, and of the translation's words rendering it only the one rendering the suffix does, never the
/// one rendering the host.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class HebrewReferentTests : IDisposable
{
    private const string Macula = "MACULA Hebrew Linguistic Datasets, available at https://github.com/Clear-Bible/macula-hebrew/";

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _hebrew;
    private readonly Text _english;

    public HebrewReferentTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        // God ... his work ... his father ... his hand ... his work ... he (a pronoun as a whole) ... his seed, rendered "seed" alone
        // ... the Philistine, him ... here am I, here am I.
        _hebrew = Corpus.Add(_db, "BHSA", TextKind.PrintedEdition, "hbo",
            (2, 2, ["אֱלֹהִים", "מְלַאכְתּוֹ", "אָבִיו", "יָדוֹ", "מְלַאכְתּוֹ", "הוּא", "זַרְעוֹ", "לוֹ", "לוֹ", "הַפְּלִשְׁתִּי", "לוֹ", "הִנֵּנִי", "הִנֵּנִי"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (2, 2, ["God", "his", "work", "his", "father", "his", "hand", "his", "work", "he", "seed", "unto", "him", "him", "Philistine", "him",
                "Here", "Here"]));
        _db.SaveChanges();

        var god = new Entity { Kind = EntityKind.Person, Slug = "god", Name = "God" };
        _db.Entities.Add(god);
        var philistines = new Entity { Kind = EntityKind.People, Slug = "philistines", Name = "Philistines" };
        _db.Entities.Add(philistines);
        _db.SaveChanges();
        _db.WordEntities.Add(new WordEntity
        {
            WordId = Hebrew(10).Id, EntityId = philistines.Id, Method = LinkMethod.StatedBySource, Source = "a test", Role = WordEntityRoles.People,
        });
        Hebrew(10).Morphology = JsonDocument.Parse("""{"pos":"adjv","number":"sg","lexicalSet":"gntl"}""");
        foreach (var word in new[] { Hebrew(1), English(1) })
        {
            _db.WordEntities.Add(new WordEntity
            {
                WordId = word.Id, EntityId = god.Id, Method = LinkMethod.StatedBySource, Source = "a test", Role = WordEntityRoles.Name,
            });
        }

        Parsed(Hebrew(1), "noun", "אֱלֹהִים", null, false);
        Parsed(Hebrew(2), "noun", "מְלָאכָה", "3ms", true);
        Parsed(Hebrew(3), "noun", "אָב", "3ms", true);
        Parsed(Hebrew(4), "noun", "יָד", "3ms", true);
        Parsed(Hebrew(5), "noun", "מְלָאכָה", "3ms", true);
        Parsed(Hebrew(6), "pron", "הוּא", null, false);
        Parsed(Hebrew(7), "noun", "זֶרַע", "3ms", true);
        Parsed(Hebrew(8), "prep", "לְ", "3ms", true);
        Parsed(Hebrew(9), "prep", "לְ", "3ms", true);
        Parsed(Hebrew(10), "adj", "פְּלִשְׁתִּי", null, false);
        Parsed(Hebrew(11), "prep", "לְ", "3ms", true);
        Parsed(Hebrew(12), "ij", "הִנֵּה", "1cs", true);
        Parsed(Hebrew(13), "ij", "הִנֵּה", "1cs", true);
        foreach (var position in new[] { 2, 3, 4, 5, 6, 7, 8, 9, 12, 13 })
        {
            _db.WordReferents.Add(new WordReferent
            {
                WordId = Hebrew(position).Id, ReferentWordId = Hebrew(1).Id, Method = LinkMethod.StatedBySource, Source = Macula,
            });
        }

        _db.WordReferents.Add(new WordReferent
        {
            WordId = Hebrew(11).Id, ReferentWordId = Hebrew(10).Id, Method = LinkMethod.StatedBySource, Source = Macula,
        });

        Align([English(1)], Hebrew(1));
        Align([English(2), English(3)], Hebrew(2));
        Align([English(4), English(5)], Hebrew(3));
        Align([English(6), English(7)], Hebrew(4));
        Align([English(8), English(9)], Hebrew(5));
        Align([English(10)], Hebrew(6));
        Align([English(11)], Hebrew(7));
        Align([English(12), English(13)], Hebrew(8));
        Align([English(14)], Hebrew(9));
        Align([English(15)], Hebrew(10));
        Align([English(16)], Hebrew(11));
        Align([English(17)], Hebrew(12));
        Align([English(18)], Hebrew(13));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task TheOriginalsWordSaysItsSuffixRefers()
    {
        var word = await Read(_hebrew, 2);
        word.Referents.Should().ContainSingle().Which.Should().Be(new ReferentResponse("person", "god", "אֱלֹהִים", Macula)
        {
            Suffix = true,
            Language = "hbo",
        });
    }

    [Fact]
    public async Task TheWordRenderingTheSuffixRefersAndTheWordRenderingTheHostDoesNot()
    {
        (await Read(_english, 2)).Referents.Should().ContainSingle().Which.Slug.Should().Be("god", "his renders his father and his hand too");
        (await Read(_english, 3)).Referents.Should().BeNull("work renders only work");
        (await Read(_english, 8)).Referents.Should().ContainSingle().Which.Slug.Should().Be("god");
        (await Read(_english, 9)).Referents.Should().BeNull();
    }

    [Fact]
    public async Task AWordRenderingTheHostAloneIsGivenNothing()
    {
        (await Read(_english, 11)).Referents.Should().BeNull("seed renders the noun; nothing says it renders the suffix");
        (await Read(_english, 5)).Referents.Should().BeNull("father is read nowhere else in the chapter");
    }

    [Fact]
    public async Task APrepositionsSuffixIsThePronounRenderingIt()
    {
        (await Read(_english, 12)).Referents.Should().BeNull("unto renders the preposition");
        (await Read(_english, 13)).Referents.Should().ContainSingle().Which.Slug.Should().Be("god");
        (await Read(_english, 14)).Referents.Should().ContainSingle().Which.Slug.Should().Be("god", "him alone renders the preposition with its suffix");
    }

    [Fact]
    public async Task APronounAsAWholeRefersAsAGreekOneDoes()
    {
        var word = await Read(_english, 10);
        word.Referents.Should().ContainSingle().Which.Should().Be(new ReferentResponse("person", "god", "אֱלֹהִים", Macula) { Language = "hbo" });
    }

    [Fact]
    public async Task ASingularGentilicReferredToIsNotItsPeople()
    {
        (await Read(_hebrew, 11)).Referents.Should().BeNull("the Philistine is one man; the word names his people only");
        (await Read(_english, 16)).Referents.Should().BeNull();
    }

    [Fact]
    public async Task AnInterjectionsOwnWordIsNotItsSuffix()
    {
        (await Read(_hebrew, 12)).Referents.Should().ContainSingle().Which.Slug.Should().Be("god");
        (await Read(_english, 17)).Referents.Should().BeNull("Here of 'Here am I' renders the particle, though it renders it again");
    }

    private async Task<TextWordResponse> Read(Text text, int position)
    {
        var chapter = await ChapterReading.ReadChapter(_db, text.Id, 1, 2, default);
        return chapter.Single(v => v.Number == 2).Words[position - 1];
    }

    private Word Hebrew(int position) => _db.WordAt(_hebrew, 2, 2, position);

    private Word English(int position) => _db.WordAt(_english, 2, 2, position);

    private void Parsed(Word word, string kind, string lemma, string? suffix, bool refersBySuffix)
    {
        var features = new Dictionary<string, string> { ["class"] = kind, ["language"] = "hbo" };
        if (suffix is not null)
        {
            features["suffix"] = suffix;
        }

        if (refersBySuffix)
        {
            features["refers"] = "suffix";
        }

        _db.WordParsings.Add(new WordParsing
        {
            WordId = word.Id, Lemma = lemma, Morphology = JsonDocument.Parse(JsonSerializer.Serialize(features)), Method = LinkMethod.StatedBySource,
            Source = Macula,
        });
    }

    private void Align(Word[] from, Word to)
    {
        var link = new Link
        {
            FromTextId = from[0].TextId,
            ToTextId = to.TextId,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner,
            Confidence = 0.9,
            Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        foreach (var word in from)
        {
            _db.LinkWords.Add(new LinkWord { Link = link, Word = word, Side = LinkSide.From });
        }

        _db.LinkWords.Add(new LinkWord { Link = link, Word = to, Side = LinkSide.To });
    }
}
