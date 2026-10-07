using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Refused">Why each rejected form was rejected, so a bad pass is visible as a shape rather than a number.</param>
internal sealed record NameFormRefusals(
    int UnknownEntity,
    int UnknownCase,
    int Empty,
    int AlreadyHeld,
    int MixedScripts = 0)
{
    public int Total => UnknownEntity + UnknownCase + Empty + AlreadyHeld + MixedScripts;

    public override string ToString() =>
        $"{UnknownEntity} for an entity the encyclopedia does not hold, " +
        $"{UnknownCase} in a case a form cannot be held in, " +
        $"{Empty} that were empty, " +
        $"{MixedScripts} with a Latin letter in a Cyrillic name, and " +
        $"{AlreadyHeld} the corpus already holds a sound form for";
}

internal sealed record NameFormOutcome(
    bool AlreadyLoaded,
    bool NoFiles,
    int Files,
    int Records,
    int Replaced,
    int Skipped,
    int Declined,
    int Forms,
    int Repaired,
    int Bared,
    NameFormRefusals Refused,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded ? "every name form on this disk is already loaded"
        : NoFiles ? "no name-form files are on this disk, so nothing was loaded from them"
        : $"{Declined} entities decline their names in {Forms} forms, read from {Records} records " +
          $"over {Files} files in {Elapsed}. {Skipped} records added no missing form, " +
          $"{Replaced} forms were superseded by a later file, " +
          $"{Repaired} forms replaced one that carried its own preposition, and {Bared} forms had " +
          $"one taken off on the way in. Refused: {Refused}.";
}

/// <summary>
/// The name a rendered line needs, in the case the phrase puts it in, read from what a generation
/// pass produced.
///
/// <para>
/// A clause names a <em>target</em>, and the line puts that target's name into a case:
/// <em>тесть Мойсея</em>, not <em>тесть Мойсей</em>. The descriptor pass produces forms for the
/// subject of the claims it writes, so the two sets only coincide once every entity has been
/// described — measured on the first tranche loaded, 205 of 985 claims had a Ukrainian form for the
/// entity they point at, which is four lines in five reading <em>брат Moses</em>. This loads the
/// other half: forms asked for on their own, for the entities somebody else's claim already names.
/// </para>
///
/// <para>
/// **A form that carries its own preposition has it taken off, and a stored one that carries one is
/// replaced.** <c>DescriptorPhrasings</c> supplies the preposition itself, so <em>в Авані</em>
/// reaches a reader as <em>похований у в Авані</em>. <see cref="NameForms"/> is the rule and it is
/// deliberately narrow. Everything else about a form is the model's word: nothing here invents an
/// ending, and a language a pass left out stays missing, because the rendering falls back to the
/// English name and a wrong ending is worse than an English one.
/// </para>
///
/// <para>
/// **The guard is per entity, language and case.** The descriptor pass shares this table, and
/// later batches can add cases an earlier batch omitted. A sound stored form remains unchanged;
/// another form of the same entity does not prevent filling a missing case.
/// </para>
/// </summary>
internal sealed class EntityNameFormLoader(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<EntityNameFormLoader> logger)
{
    /// <summary>
    /// How every source string written here begins. It is what tells this loader's rows from the
    /// descriptor pass's rows in the same table.
    /// </summary>
    public const string SourcePrefix = "declined by";

    private static readonly IReadOnlyDictionary<string, Dictionary<string, string>> NoNames =
        new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

    public async Task<NameFormOutcome> Load(
        string resources,
        CancellationToken cancellationToken = default)
    {
        var directory = Where(resources);
        if (!Directory.Exists(directory))
        {
            logger.LogInformation(
                "No name-form files at {Directory}, so a clause naming an entity nothing has "
                + "described renders that entity's English name. They are what a generation pass "
                + "writes and they stay out of the repository; point \"{Key}\" at a directory of "
                + "{Pattern} files to load them",
                directory,
                NameFormFiles.ConfigurationKey,
                NameFormFiles.FilePattern);
            return Nothing(alreadyLoaded: false);
        }

        var started = Stopwatch.StartNew();
        var (records, files) = NameFormFiles.Read(directory);
        if (records.Count == 0)
        {
            return Nothing(alreadyLoaded: false) with { Files = files, Elapsed = started.Elapsed };
        }

        var entities = await Slugs(records, cancellationToken);
        var held = await Held(entities.Values, cancellationToken);

        int unknownEntity = 0, unknownCase = 0, empty = 0, alreadyHeld = 0, mixedScripts = 0;
        int skipped = 0, repaired = 0, bared = 0, replaced = 0;

        // What this run has settled per form rather than per entity: the row it wrote, or null
        // where the corpus already held a sound one. Two files naming one entity is ordinary here
        // — a repair pass and a target pass are about different halves of the same name — so a
        // second record fills what the first left out and overwrites only what it also says.
        var decided = new Dictionary<(int Entity, string Language, string Case), EntityNameForm?>();

        foreach (var record in records)
        {
            if (!entities.TryGetValue(record.Entity, out var entityId))
            {
                unknownEntity += record.Names?.Sum(language => language.Value.Count) ?? 1;
                continue;
            }

            var changed = false;
            var source = Source(record);
            foreach (var (language, cases) in record.Names ?? NoNames)
            {
                foreach (var (grammaticalCase, given) in cases)
                {
                    if (!GrammaticalCases.All.Contains(grammaticalCase))
                    {
                        unknownCase++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(given))
                    {
                        empty++;
                        continue;
                    }

                    var form = NameForms.Bare(language, given, grammaticalCase);
                    if (form.Length == 0)
                    {
                        empty++;
                        continue;
                    }

                    if (NameForms.MixesScripts(language, form))
                    {
                        mixedScripts++;
                        continue;
                    }

                    if (form != given.Trim())
                    {
                        bared++;
                    }

                    var key = (entityId, language, grammaticalCase);
                    if (decided.TryGetValue(key, out var earlier))
                    {
                        if (earlier is null)
                        {
                            if (held.TryGetValue(key, out var kept)
                                && NameForms.RestoresItsArticle(language, grammaticalCase, kept.Form, form))
                            {
                                kept.Form = form;
                                kept.Source = source;
                                repaired++;
                                changed = true;
                            }

                            continue;
                        }

                        earlier.Form = form;
                        earlier.Source = source;
                        replaced++;
                        changed = true;
                        continue;
                    }

                    if (held.TryGetValue(key, out var standing))
                    {
                        if (NameForms.Bare(language, standing.Form, grammaticalCase) == standing.Form
                            && !NameForms.MixesScripts(language, standing.Form)
                            && !NameForms.RestoresItsArticle(language, grammaticalCase, standing.Form, form))
                        {
                            alreadyHeld++;
                            decided[key] = null;
                            continue;
                        }

                        // The stored form is the one this loader repairs: it carries a preposition
                        // the phrase supplies. A pass that has since been asked for the bare form
                        // supersedes it rather than being refused by it.
                        db.EntityNameForms.Remove(standing);
                        repaired++;
                    }

                    var row = new EntityNameForm
                    {
                        EntityId = entityId,
                        Language = language,
                        GrammaticalCase = grammaticalCase,
                        Form = form,
                        Method = LinkMethod.ModelReading,
                        Confidence = 1,
                        Source = source,
                    };

                    db.EntityNameForms.Add(row);
                    decided[key] = row;
                    changed = true;
                }
            }

            if (!changed)
            {
                skipped++;
            }
        }

        var forms = decided.Values.Count(row => row is not null);
        var wrote = decided
            .Where(pair => pair.Value is not null)
            .Select(pair => pair.Key.Entity)
            .Distinct()
            .Count();

        var refused = new NameFormRefusals(unknownEntity, unknownCase, empty, alreadyHeld, mixedScripts);
        var missing = unknownEntity + unknownCase + empty;

        if (forms > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var outcome = new NameFormOutcome(
            AlreadyLoaded: forms == 0 && skipped == records.Count && missing == 0,
            NoFiles: false,
            files, records.Count, replaced, skipped, wrote, forms, repaired, bared, refused,
            started.Elapsed);

        if (missing > 0)
        {
            logger.LogWarning(
                "{Count} name forms were refused and are not in the corpus: {Refused}. A language "
                + "with no form falls back to the English name, which is a visible gap; the one "
                + "thing that must not happen is an invented ending, so a form nothing can place "
                + "is dropped rather than guessed at",
                missing,
                refused);
        }

        logger.LogInformation("Declined: {Outcome}", outcome);
        return outcome;
    }

    private static NameFormOutcome Nothing(bool alreadyLoaded) =>
        new(alreadyLoaded, !alreadyLoaded, 0, 0, 0, 0, 0, 0, 0, 0,
            new NameFormRefusals(0, 0, 0, 0), TimeSpan.Zero);

    /// <summary>
    /// Where the files are. Under the corpus sources by default, in this project's own folder:
    /// they are the one part of that tree nobody else made.
    /// </summary>
    private string Where(string resources)
    {
        var configured = configuration[NameFormFiles.ConfigurationKey];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(resources, NameFormFiles.DefaultFolder)
            : configured;
    }

    /// <summary>
    /// Who said it, in the words a reader gets: which model, and when it was asked. Taken from the
    /// record rather than declared here, so a second pass under a different model cannot be stored
    /// under the first one's name.
    /// </summary>
    private static string Source(NameFormRecord record) =>
        $"{SourcePrefix} {record.Model}, asked {record.AskedAt}";

    /// <summary>
    /// Which entity each slug the files name is, in one query rather than one per record.
    /// </summary>
    private async Task<Dictionary<string, int>> Slugs(
        IReadOnlyCollection<NameFormRecord> records,
        CancellationToken cancellationToken)
    {
        var slugs = records.Select(r => r.Entity).Distinct(StringComparer.Ordinal).ToList();

        return await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .ToDictionaryAsync(e => e.Slug, e => e.Id, StringComparer.Ordinal, cancellationToken);
    }

    /// <summary>
    /// Every form the corpus already holds for these entities, whoever wrote it, keyed the way the
    /// table's unique index is. Without it a second writer's insert is a constraint violation
    /// rather than a decision.
    /// </summary>
    private async Task<Dictionary<(int Entity, string Language, string Case), EntityNameForm>> Held(
        IReadOnlyCollection<int> entities,
        CancellationToken cancellationToken)
    {
        var rows = await db.EntityNameForms
            .Where(f => entities.Contains(f.EntityId))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(f => (f.EntityId, f.Language, f.GrammaticalCase));
    }
}
