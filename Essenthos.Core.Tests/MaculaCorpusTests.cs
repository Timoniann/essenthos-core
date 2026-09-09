using System.Text.Json;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Macula;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The annotation loader against a real database: what the rows say about where they came from,
/// that a second run writes nothing, and — the one that matters — that a file whose verses are cut
/// differently is refused whole rather than loaded one word out.
///
/// A handful of words rather than the whole New Testament. Whether the reader reads the format is
/// <see cref="MaculaTests"/>, which needs no database; what is under test here is the path from a
/// row in the file to a row in the table, and the guard standing across it.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class MaculaCorpusTests(WitnessDatabase database) : IAsyncLifetime
{
    private const string Header =
        "ref\tclass\ttype\tlemma\tmorph\tperson\tnumber\tgender\tcase\ttense\tvoice\tmood\tdegree\ttext\n";

    /// <summary>
    /// Matthew 1:1 as MACULA annotates it. Ἰησοῦ is <c>proper</c> and Δαυείδ is the row where the
    /// two copies of Nestle 1904 drifted apart — MACULA prints Δαυίδ — which is what the note is
    /// for.
    /// </summary>
    private const string Matthew =
        Header +
        "MAT 1:1!1\tnoun\tcommon\tβίβλος\tN-NSF\t\tsingular\tfeminine\tnominative\t\t\t\t\tΒίβλος\n" +
        "MAT 1:1!2\tnoun\tcommon\tγένεσις\tN-GSF\t\tsingular\tfeminine\tgenitive\t\t\t\t\tγενέσεως\n" +
        "MAT 1:1!3\tnoun\tproper\tἸησοῦς\tN-GSM\t\tsingular\tmasculine\tgenitive\t\t\t\t\tἸησοῦ\n" +
        "MAT 1:1!4\tnoun\tproper\tΔαυίδ\tN-PRI\t\t\t\tgenitive\t\t\t\t\tΔαυίδ\n" +
        "MAT 1:2!1\tverb\t\tγεννάω\tV-AAI-3S\tthird\tsingular\t\t\taorist\tactive\tindicative\t\tἐγέννησεν\n";

    private static readonly string[] First = ["Βίβλος", "γενέσεως", "Ἰησοῦ", "Δαυεὶδ"];

    private static readonly string[] Second = ["ἐγέννησεν"];

    /// <summary>Where these verses sit in the canonical frame, which is what the addresses name.</summary>
    private const int MatthewOrdinal = 40;

    public Task InitializeAsync() => Clear();

    public Task DisposeAsync() => Clear();

    private async Task Clear()
    {
        await using var db = database.NewContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE text CASCADE");
    }

    /// <summary>A folder shaped the way the fetch script leaves one.</summary>
    private static string Folder(string table)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"macula-{Guid.NewGuid():n}", "Macula");
        Directory.CreateDirectory(Path.Combine(folder, "tsv"));
        File.WriteAllText(MaculaReader.Path(folder), table);
        return folder;
    }

    /// <summary>
    /// The rows carry no confidence, which is the whole difference between this source and every
    /// other second opinion in the corpus: MACULA states its analysis of the Nestle 1904 word, and
    /// the word is ours. Nothing was inferred, so there is nothing to be unsure about.
    /// </summary>
    [Fact]
    public async Task WritesAStatedAnnotationBesideTheWordsOwnMorphology()
    {
        await using var db = database.NewContext();
        var nestle = Corpus.Add(
            db, NestleTextSource.Slug, TextKind.CriticalEdition, "grc", (1, 1, First), (1, 2, Second));
        db.SaveChanges();
        db.In(nestle, MatthewOrdinal);

        var folder = Folder(Matthew);
        try
        {
            var loader = new MaculaAnnotationLoader(db, NullLogger<MaculaAnnotationLoader>.Instance);
            var outcome = await loader.Load(folder);

            outcome.Annotated.Should().Be(5);
            outcome.Proper.Should().Be(2, "Ἰησοῦ and Δαυείδ are names and the other three are not");
            outcome.Drifted.Should().Be(1, "MACULA prints Δαυίδ where this corpus prints Δαυεὶδ");

            var written = await db.WordParsings.AsNoTracking().Include(p => p.Word).ToListAsync();
            written.Should().HaveCount(5);
            written.Should().OnlyContain(p => p.Method == LinkMethod.StatedBySource);
            written.Should().OnlyContain(p => p.Confidence == null);
            written.Should().OnlyContain(p => p.Source.StartsWith("MACULA Greek Linguistic Datasets"));

            var jesus = written.Single(p => p.Word!.Surface == "Ἰησοῦ");
            Feature(jesus.Morphology, "type").Should().Be("proper");
            Feature(jesus.Morphology, "class").Should().Be("noun");
            jesus.Note.Should().BeNull();

            var verb = written.Single(p => p.Word!.Surface == "ἐγέννησεν");
            Feature(verb.Morphology, "type").Should().BeNull();
            Feature(verb.Morphology, "morph").Should().Be("V-AAI-3S");
            Feature(verb.Morphology, "tense").Should().Be("aorist");
            Feature(verb.Morphology, "case").Should().BeNull();
            verb.Lemma.Should().Be("γεννάω");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);
        }
    }

    /// <summary>
    /// Both copies of the edition descend from the same repository at different times, so a word
    /// spelt two ways is expected. It is recorded on the row rather than smoothed over, because a
    /// reader checking one of these annotations should be able to see that the letters differ
    /// without going back to either file.
    /// </summary>
    [Fact]
    public async Task NamesBothSpellingsWhereTheTwoCopiesOfTheEditionDrifted()
    {
        await using var db = database.NewContext();
        var nestle = Corpus.Add(
            db, NestleTextSource.Slug, TextKind.CriticalEdition, "grc", (1, 1, First), (1, 2, Second));
        db.SaveChanges();
        db.In(nestle, MatthewOrdinal);

        var folder = Folder(Matthew);
        try
        {
            var loader = new MaculaAnnotationLoader(db, NullLogger<MaculaAnnotationLoader>.Instance);
            await loader.Load(folder);

            var david = await db.WordParsings.AsNoTracking()
                .Include(p => p.Word)
                .SingleAsync(p => p.Word!.Surface == "Δαυεὶδ");

            david.Note.Should().Be("this corpus prints Δαυεὶδ where MACULA prints Δαυίδ");
            david.Confidence.Should().BeNull("the spelling drifted; the address did not");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);
        }
    }

    /// <summary>
    /// The failure this loader exists to prevent. A file whose verses are divided differently would
    /// load without complaint and put every annotation after the seam on the wrong word — 137,779
    /// rows, each as confident as the last, and no error anywhere. So the addresses are compared
    /// before anything is written and the first that disagrees stops the load.
    /// </summary>
    [Fact]
    public async Task RefusesTheWholeFileWhenAVerseIsCutDifferently()
    {
        await using var db = database.NewContext();
        var nestle = Corpus.Add(
            db, NestleTextSource.Slug, TextKind.CriticalEdition, "grc", (1, 1, First), (1, 2, Second));
        db.SaveChanges();
        db.In(nestle, MatthewOrdinal);

        // The same five words, but with the fourth put at the head of the next verse — which is
        // exactly what a differently divided edition looks like.
        var moved = Matthew.Replace("MAT 1:1!4", "MAT 1:2!1").Replace("MAT 1:2!1\tverb", "MAT 1:2!2\tverb");
        var folder = Folder(moved);
        try
        {
            var loader = new MaculaAnnotationLoader(db, NullLogger<MaculaAnnotationLoader>.Instance);
            var thrown = async () => await loader.Load(folder);

            await thrown.Should().ThrowAsync<InvalidDataException>().WithMessage("*wrong words*");
            (await db.WordParsings.CountAsync()).Should().Be(0);
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
        var nestle = Corpus.Add(
            db, NestleTextSource.Slug, TextKind.CriticalEdition, "grc", (1, 1, First), (1, 2, Second));
        db.SaveChanges();
        db.In(nestle, MatthewOrdinal);

        var folder = Folder(Matthew);
        try
        {
            var loader = new MaculaAnnotationLoader(db, NullLogger<MaculaAnnotationLoader>.Instance);
            await loader.Load(folder);

            var again = await loader.Load(folder);

            again.AlreadyLoaded.Should().BeTrue();
            (await db.WordParsings.CountAsync()).Should().Be(5);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);
        }
    }

    /// <summary>
    /// A checkout without the corpus must load, so a missing file is a message and not a failure.
    /// </summary>
    [Fact]
    public async Task SaysNothingIsThereRatherThanFailing()
    {
        await using var db = database.NewContext();
        var nestle = Corpus.Add(db, NestleTextSource.Slug, TextKind.CriticalEdition, "grc", (1, 1, First));
        db.SaveChanges();
        db.In(nestle, MatthewOrdinal);

        var loader = new MaculaAnnotationLoader(db, NullLogger<MaculaAnnotationLoader>.Instance);
        var outcome = await loader.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n")));

        outcome.Annotated.Should().Be(0);
        (await db.WordParsings.CountAsync()).Should().Be(0);
    }

    private static string? Feature(JsonDocument morphology, string name) =>
        morphology.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
}
