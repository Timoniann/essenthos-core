using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// A text's declaration is written once, when the text is first loaded; a definition corrected later
/// reaches a corpus already holding the text through <see cref="TextDeclarationLoader"/>, and a corpus
/// loaded cold already reads as it leaves a warm one.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TextDeclarationTests : IAsyncLifetime
{
    private static readonly string[] Loaded = ["JND2024", "LUTH1912", "UBIO", "GRCBRENT", "WEB", "BHSA", "VIGLT"];

    private readonly AppDbContext _db;

    public TextDeclarationTests(WitnessDatabase database) => _db = database.NewContext();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _db.Texts.Where(t => Loaded.Contains(t.Slug)).ExecuteDeleteAsync();
        await _db.DisposeAsync();
    }

    /// <summary>
    /// Every passage a repair takes out is in no definition, and every replacement is in its
    /// definition, so the repairs describe the step from what a warm corpus holds to what a cold one
    /// would; a field set from the definition names a field the definition fills.
    /// </summary>
    [Fact]
    public void EveryRepairLeadsToWhatTheDefinitionSays()
    {
        var definitions = TextCorpus.Definitions.ToDictionary(d => d.Slug, StringComparer.OrdinalIgnoreCase);

        foreach (var repair in TextDeclarations.Notes)
        {
            definitions.Should().ContainKey(repair.Slug);
            var note = definitions[repair.Slug].RightsNote;
            note.Should().NotBeNullOrEmpty(repair.Slug);
            if (repair.Was is not null)
            {
                note.Should().NotContain(repair.Was, repair.Slug);
                if (repair.Now.Length > 0)
                {
                    note.Should().Contain(repair.Now, repair.Slug);
                }
            }
        }

        foreach (var repair in TextDeclarations.Fields)
        {
            definitions.Should().ContainKey(repair.Slug);
        }

        foreach (var repair in TextDeclarations.Parts.Where(r => r.Was is null))
        {
            definitions[repair.Slug].PartSources.Should().Contain(repair.Now, repair.Slug);
        }
    }

    /// <summary>
    /// The numberings and taggings the corpus uses without naming them are named in no text's
    /// declaration either.
    /// </summary>
    [Fact]
    public void NoDefinitionNamesTheNumberingsTheOwnerRuledOn()
    {
        string[] unnamed = ["Gómez", "Gomez", "Concordances et Traductions", "concordance.bible", "Zefania"];
        foreach (var definition in TextCorpus.Definitions)
        {
            string?[] said = [definition.RightsNote, definition.Citation, definition.About, definition.Edition];
            foreach (var name in unnamed)
            {
                said.Should().NotContain(line => line != null && line.Contains(name, StringComparison.Ordinal),
                    $"{definition.Slug} names {name}");
            }
        }
    }

    [Fact]
    public async Task AWarmCorpusTakesTheCorrectedDeclarationsAndKeepsWhatLaterStepsAppended()
    {
        const string appended = " Modified: something a later step wrote.";
        var definitions = TextCorpus.Definitions.ToDictionary(d => d.Slug, StringComparer.OrdinalIgnoreCase);

        Add("JND2024", note: "CrossWire publishes the module as Public Domain, and its description says the 2024 text "
                             + "is free of rights as BPC provides it. The Strong numbers in the module are the work of "
                             + "Concordances et Traductions de la Bible (concordance.bible); they are read to draw "
                             + "links and are not stored on the words." + appended,
            citation: "La Sainte Bible, traduction J.N. Darby, révision BPC 2024 (JND v2.0), as CrossWire publishes it "
                      + "in the SWORD module FreJND, with the Strong numbers of concordance.bible.");
        Add("LUTH1912", note: "Settled for the text and unstated for the tagging, and the two are worth keeping apart. "
                              + definitions["LUTH1912"].RightsNote + " None of the three says who assigned the Strong "
                              + "numbers or under what terms eBible obtained them, and a tagging layer is somebody's "
                              + "work even when the text under it is nobody's.");
        Add("UBIO", note: TextDeclarations.Notes.Single(r => r.Slug == "UBIO").Was + appended);
        Add("GRCBRENT", note: definitions["GRCBRENT"].RightsNote);
        var named = TextDeclarations.Notes.Single(r => r.Slug == "WEB");
        var web = Add("WEB", name: "World English Bible",
            note: definitions["WEB"].RightsNote!.Replace(named.Now, named.Was!, StringComparison.Ordinal) + appended,
            edition: "The updated edition, protocanon only, which renders the divine name as LORD; the copy loaded is "
                     + "eBible's of 2026-08-26");
        TextPartSources.Add(web, TextDeclarations.Parts.Single(r => r.Slug == "WEB").Was!);
        Add("BHSA", note: null);
        Add("VIGLT", note: definitions["VIGLT"].RightsNote, holder: "Far East Broadcasting Company");
        await _db.SaveChangesAsync();

        var loader = new TextDeclarationLoader(_db, NullLogger<TextDeclarationLoader>.Instance);
        var outcome = await loader.Load();
        outcome.Texts.Should().Be(Loaded.Length);

        var texts = await _db.Texts.AsNoTracking().Where(t => Loaded.Contains(t.Slug)).ToDictionaryAsync(t => t.Slug);
        texts["JND2024"].RightsNote.Should().Be(definitions["JND2024"].RightsNote + appended);
        texts["JND2024"].Citation.Should().Be(definitions["JND2024"].Citation);
        texts["LUTH1912"].RightsNote.Should().Be(definitions["LUTH1912"].RightsNote);
        texts["UBIO"].RightsNote.Should().Be(definitions["UBIO"].RightsNote + appended);
        texts["GRCBRENT"].Citation.Should().Be(definitions["GRCBRENT"].Citation);
        TextPartSources.Of(texts["GRCBRENT"]).Should().Equal(BrentonEdits.GlauxPart);
        texts["WEB"].Edition.Should().Be(definitions["WEB"].Edition);
        texts["WEB"].Name.Should().Be("World English Bible (edited)");
        texts["WEB"].RightsNote.Should().Be(definitions["WEB"].RightsNote + appended);
        TextPartSources.Of(texts["WEB"]).Should().Equal(DeuterocanonTextSource.WorldEnglishSource);
        texts["BHSA"].RightsNote.Should().Be(definitions["BHSA"].RightsNote);
        texts["VIGLT"].RightsHolder.Should().Be(definitions["VIGLT"].RightsHolder);

        (await loader.Load()).Changes.Should().Be(0, "a second pass finds every declaration already corrected");
    }

    private Text Add(
        string slug, string? note, string? citation = null, string? edition = null, string? holder = null,
        string? name = null)
    {
        var text = new Text
        {
            Slug = slug,
            Name = name ?? slug,
            Language = "und",
            Kind = TextKind.Translation,
            RightsNote = note,
            Citation = citation,
            Edition = edition,
            RightsHolder = holder,
        };
        _db.Texts.Add(text);
        return text;
    }
}
