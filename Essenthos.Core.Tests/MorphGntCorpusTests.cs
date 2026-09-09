using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.MorphGnt;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The parsing loader against a real database: the rows it writes, what they say about where they
/// came from, and that a second run writes nothing.
///
/// A handful of words rather than the whole New Testament, because what is under test here is the
/// path — the join's answer turned into rows, the provenance constraints the table carries, the
/// guard that stops a second load doubling it. Whether the join is right over 137,779 words is
/// <see cref="MorphGntTests"/>, which needs no database.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class MorphGntCorpusTests(WitnessDatabase database) : IAsyncLifetime
{
    /// <summary>
    /// Matthew 1:1 and the first words of 1:2, as MorphGNT publishes them. The last line is the one
    /// worth having: the SBLGNT prints Δαυὶδ where Nestle 1904 prints Δαυεὶδ, so it exercises the
    /// spelling pass rather than the letter-for-letter one.
    /// </summary>
    private const string MatthewParsing =
        """
        010101 N- ----NSF- Βίβλος Βίβλος βίβλος βίβλος
        010101 N- ----GSF- γενέσεως γενέσεως γενέσεως γένεσις
        010101 N- ----GSM- Ἰησοῦ Ἰησοῦ Ἰησοῦ Ἰησοῦς
        010101 N- ----GSM- χριστοῦ χριστοῦ Χριστοῦ Χριστός
        010101 N- ----GSM- υἱοῦ υἱοῦ υἱοῦ υἱός
        010101 N- ----GSM- Δαυὶδ Δαυὶδ Δαυίδ Δαυίδ
        010102 N- ----NSM- Ἀβραὰμ Ἀβραὰμ Ἀβραάμ Ἀβραάμ
        010102 V- 3AAI-S-- ἐγέννησεν ἐγέννησεν ἐγέννησε(ν) γεννάω
        """;

    /// <summary>The same words as Nestle 1904 prints them, in the same order.</summary>
    private static readonly string[] First =
        ["Βίβλος", "γενέσεως", "Ἰησοῦ", "Χριστοῦ", "υἱοῦ", "Δαυεὶδ"];

    private static readonly string[] Second = ["Ἀβραὰμ", "ἐγέννησεν"];

    /// <summary>Where these verses sit in the canonical frame, which is what the join addresses by.</summary>
    private const int Matthew = 40;

    public Task InitializeAsync() => Clear();

    public Task DisposeAsync() => Clear();

    private async Task Clear()
    {
        await using var db = database.NewContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE text CASCADE");
    }

    /// <summary>
    /// A folder shaped the way the fetch script leaves one, holding the one book these verses are
    /// in. The loader reads all 27 names and a missing file is a missing book, so the other 26 are
    /// written empty.
    /// </summary>
    private static string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"morphgnt-{Guid.NewGuid():n}", "MorphGnt");
        Directory.CreateDirectory(Path.Combine(folder, "parsing"));

        foreach (var book in MorphGntReader.Books)
        {
            File.WriteAllText(
                Path.Combine(folder, "parsing", $"{book}-morphgnt.txt"),
                book == "61-Mt" ? MatthewParsing : string.Empty);
        }

        return folder;
    }

    [Fact]
    public async Task WritesAParsingBesideTheWordsOwnMorphologyAndSaysWhereItCameFrom()
    {
        await using var db = database.NewContext();
        var nestle = Corpus.Add(
            db, NestleTextSource.Slug, TextKind.CriticalEdition, "grc", (1, 1, First), (1, 2, Second));
        db.SaveChanges();
        db.In(nestle, Matthew);

        var folder = Folder();
        try
        {
            var loader = new MorphGntParsingLoader(db, NullLogger<MorphGntParsingLoader>.Instance);
            var outcome = await loader.Load(folder);

            outcome.Printed.Should().Be(7);
            outcome.Spelled.Should().Be(1, "the SBLGNT writes Δαυὶδ where Nestle writes Δαυεὶδ");
            outcome.Unreached.Should().Be(0);
            outcome.Unused.Should().Be(0);

            var written = await db.WordParsings.AsNoTracking().Include(p => p.Word).ToListAsync();
            written.Should().HaveCount(8);
            written.Should().OnlyContain(p => p.Method == LinkMethod.Lexical);
            written.Should().OnlyContain(p => p.Confidence != null);
            written.Should().OnlyContain(p => p.Source.StartsWith("morphgnt/sblgnt"));

            var david = written.Single(p => p.Word!.Surface == "Δαυεὶδ");
            david.Confidence.Should().Be(0.85);
            david.Note.Should().Be("Nestle writes Δαυεὶδ where the SBLGNT writes Δαυὶδ");
            david.Lemma.Should().Be("Δαυίδ");

            var verb = written.Single(p => p.Word!.Surface == "ἐγέννησεν");
            verb.Confidence.Should().Be(0.98);
            verb.Note.Should().BeNull();
            Feature(verb.Morphology, "pos").Should().Be("V-");
            Feature(verb.Morphology, "tense").Should().Be("aorist");
            Feature(verb.Morphology, "voice").Should().Be("active");
            Feature(verb.Morphology, "mood").Should().Be("indicative");
            Feature(verb.Morphology, "person").Should().Be("third");
            Feature(verb.Morphology, "case").Should().BeNull();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);
        }
    }

    /// <summary>
    /// The pass runs on every boot, so a second run must write nothing. Without the guard the
    /// unique index would refuse it, which is a load that fails rather than a load that is done.
    /// </summary>
    [Fact]
    public async Task RunsAgainWithoutWritingAnythingTwice()
    {
        await using var db = database.NewContext();
        var nestle = Corpus.Add(db, NestleTextSource.Slug, TextKind.CriticalEdition, "grc", (1, 1, First));
        db.SaveChanges();
        db.In(nestle, Matthew);

        var folder = Folder();
        try
        {
            var loader = new MorphGntParsingLoader(db, NullLogger<MorphGntParsingLoader>.Instance);
            await loader.Load(folder);

            var again = await loader.Load(folder);

            again.AlreadyLoaded.Should().BeTrue();
            (await db.WordParsings.CountAsync()).Should().Be(6);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);
        }
    }

    /// <summary>
    /// A checkout without the corpus must load, so a missing folder is a message and not a failure.
    /// </summary>
    [Fact]
    public async Task SaysNothingIsThereRatherThanFailing()
    {
        await using var db = database.NewContext();
        var nestle = Corpus.Add(db, NestleTextSource.Slug, TextKind.CriticalEdition, "grc", (1, 1, First));
        db.SaveChanges();
        db.In(nestle, Matthew);

        var loader = new MorphGntParsingLoader(db, NullLogger<MorphGntParsingLoader>.Instance);
        var outcome = await loader.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n")));

        outcome.Printed.Should().Be(0);
        (await db.WordParsings.CountAsync()).Should().Be(0);
    }

    private static string? Feature(JsonDocument morphology, string name) =>
        morphology.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
}
