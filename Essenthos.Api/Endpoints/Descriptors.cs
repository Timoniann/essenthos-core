using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// What this corpus says an entity is, in the language the reader asked for, as pieces rather than
/// a sentence.
///
/// <para>
/// **It is structured output and never a string.** A rendered line arriving as HTML with anchors in
/// it would put the client's markup in the API's hands and the API's prose in the client's, and the
/// reader already receives words this way — a word and, beside it, whom it names — so a description
/// arrives the same way: the parts in order, and the part that is a name carrying the entity it is.
/// The client joins them and makes the links. Nothing here knows what a link looks like.
/// </para>
///
/// <para>
/// **Every name the line needs arrives with it.** A clause names a target, and rendering that
/// target means knowing what kind of thing it is, what it is called in the language being rendered,
/// and what it is called in the cases that language's phrases put it in — none of which is on the
/// subject's own record. A card that had to fetch each target's record to say <em>тесть Мойсея</em>
/// would make one request per name in a line, so the target is complete when it arrives.
/// </para>
///
/// <para>
/// A language the encyclopedia has no phrasings for is answered in English and told so:
/// <see cref="EntityDescriptorResponse.Language"/> is the language rendered and never the one asked
/// for. The alternative for such a reader is <see cref="Database.Entities.Entity.Distinguisher"/>,
/// which is English too and is somebody else's prose with no link in it.
/// </para>
/// </summary>
internal static class Descriptors
{
    /// <summary>
    /// The confidence at or above which a clause is shown plainly. Below it the clause is kept and
    /// marked rather than dropped, which is the descriptor contract's rule: a description that
    /// quietly loses its weakest clause reads as more certain than the corpus is.
    /// </summary>
    private const double Plain = 0.70;

    /// <summary>
    /// The descriptions of a set of entities, in a fixed handful of queries whatever their number. A
    /// page of search results asks for forty of these at once.
    ///
    /// <para>
    /// **A family tie is read from both ends.** A clause is written from its own subject's side, so
    /// Reuben's <em>son of Jacob</em> is on Reuben's record and Jacob's own line used to know nothing
    /// of it: Jacob read <em>father of Dan and Naphtali</em> beside a tree of twelve sons. A parent's,
    /// spouse's, child's or sibling's clause written on the other person is therefore read here as
    /// well, turned to this side — <em>father of Reuben</em> — by the sex of the person it is now
    /// said of. Where that sex is not known the clause is not turned, because <em>father</em> or
    /// <em>mother</em> would be a guess. Every other relation is read from its own side only.
    /// </para>
    /// </summary>
    /// <param name="language">The language the words of the line are said in: the interface's.</param>
    /// <param name="names">
    /// The language the names in it are written in, which is the chosen text's: a Ukrainian page over
    /// the Synodal reads <em>син Марии</em>, Ukrainian words around the Synodal's name. The line's own
    /// language where nothing is said.
    /// </param>
    public static async Task<Dictionary<string, EntityDescriptorResponse>> Of(
        AppDbContext db,
        IEnumerable<string> slugs,
        string? language,
        CancellationToken cancellationToken,
        string? names = null)
    {
        var wanted = slugs.Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var spoken = DescriptorPhrasings.Spoken(language);
        var phrasings = DescriptorPhrasings.For(spoken)!;
        var named = string.IsNullOrWhiteSpace(names) ? spoken : names.Trim();

        // Everything the target contributes is taken along this join rather than looked up per
        // clause: a subquery in a projection is evaluated once per joined row, which is what took
        // /v1/corpora from a millisecond to 46 seconds.
        var own = await db.EntityDescriptors
            .Where(d => wanted.Contains(d.Entity!.Slug))
            .OrderBy(d => d.EntityId).ThenBy(d => d.Ordinal)
            .Select(d => new Clause(
                d.Entity!.Slug,
                d.Ordinal,
                d.Relation,
                d.TargetEntityId,
                d.Target!.Kind,
                d.Target.Slug,
                d.Target.Name,
                d.Target.Distinguisher,
                d.CanonicalBook,
                d.CanonicalChapter,
                d.CanonicalVerse,
                d.Method,
                d.Confidence,
                d.Source,
                d.Note)
            {
                Citation = d.Citation,
                Witness = d.Witness,
                Original = d.Original,
                SubjectId = d.EntityId,
                SubjectSex = d.Entity.Sex,
                TargetSex = d.Target.Sex,
            })
            .ToListAsync(cancellationToken);

        // The same ties written on the other person, with that person as the target.
        var written = await db.EntityDescriptors
            .Where(d => wanted.Contains(d.Target!.Slug)
                        && FamilyTies.Turnable.Contains(d.Relation)
                        && d.EntityId != d.TargetEntityId
                        && !FamilyEndpoints.Deities.Contains(d.Entity!.Slug)
                        && !FamilyEndpoints.Deities.Contains(d.Target.Slug))
            .OrderBy(d => d.CanonicalBook).ThenBy(d => d.CanonicalChapter).ThenBy(d => d.CanonicalVerse)
            .ThenBy(d => d.EntityId).ThenBy(d => d.Ordinal)
            .Select(d => new Clause(
                d.Target!.Slug,
                d.Ordinal,
                d.Relation,
                d.EntityId,
                d.Entity!.Kind,
                d.Entity.Slug,
                d.Entity.Name,
                d.Entity.Distinguisher,
                d.CanonicalBook,
                d.CanonicalChapter,
                d.CanonicalVerse,
                d.Method,
                d.Confidence,
                d.Source,
                d.Note)
            {
                Citation = d.Citation,
                Witness = d.Witness,
                Original = d.Original,
                SubjectId = d.TargetEntityId,
                SubjectSex = d.Target.Sex,
                TargetSex = d.Entity.Sex,
                Stated = d.Relation,
            })
            .ToListAsync(cancellationToken);

        if (own.Count == 0 && written.Count == 0)
        {
            return [];
        }

        // The sex our own rows state, for the people the dataset gives none: the subject a written
        // tie is turned for, and the child or sibling a count has to call a son or a daughter.
        var unsexed = written.Where(c => c.SubjectSex is null).Select(c => c.SubjectId)
            .Concat(own.Where(c => c.TargetSex is null && FamilyTies.Counts(c.Relation)).Select(c => c.TargetEntityId))
            .Distinct()
            .ToList();
        var sexes = await OursOnlyRecords.SexesOf(db, unsexed, cancellationToken);
        string? SexOf(int id, string? held) => held ?? sexes.GetValueOrDefault(id);

        var turned = written
            .Select(c => FamilyTies.Turn(c.Relation, SexOf(c.SubjectId, c.SubjectSex)) is { } relation
                ? c with { Relation = relation, TargetSex = FamilyTies.SexOfTheWriter(c.Relation) ?? c.TargetSex }
                : null)
            .OfType<Clause>()
            .ToList();
        var clauses = FamilyTies.Folded([.. own.Select(c => c with { TargetSex = SexOf(c.TargetEntityId, c.TargetSex) }), .. turned]);

        // A brother or sister whom the recorded parents show to be a half-sibling is said so.
        var full = clauses
            .Where(c => c.Relation is DescriptorRelations.BrotherOf or DescriptorRelations.SisterOf)
            .Select(c => (c.SubjectId, c.TargetEntityId))
            .Distinct()
            .ToList();
        var halved = await SharedParents.Halved(db, full, cancellationToken);
        clauses = [.. clauses.Select(c => c.Relation is DescriptorRelations.BrotherOf or DescriptorRelations.SisterOf
                                          && halved.Contains((c.SubjectId, c.TargetEntityId))
            ? c with
            {
                Relation = c.Relation == DescriptorRelations.BrotherOf
                    ? DescriptorRelations.HalfBrotherOf
                    : DescriptorRelations.HalfSisterOf,
            }
            : c)];

        var halves = clauses
            .Where(c => c.Relation is DescriptorRelations.HalfBrotherOf or DescriptorRelations.HalfSisterOf)
            .Select(c => (c.Slug, c.TargetEntityId))
            .Distinct()
            .ToList();
        var sharing = await SharedParents.Of(db, halves, cancellationToken);
        clauses = [.. clauses.Select(c => sharing.TryGetValue((c.Slug, c.TargetEntityId), out var parent)
            ? c with { SharedParent = parent }
            : c)];

        var targets = clauses.Select(c => c.TargetEntityId).Distinct().ToList();
        var rows = await db.EntityNameForms
            .Where(f => targets.Contains(f.EntityId) && f.Language == named)
            .Select(f => new { f.EntityId, f.GrammaticalCase, f.Form })
            .ToListAsync(cancellationToken);

        var forms = rows
            .GroupBy(f => f.EntityId)
            .ToDictionary(
                target => target.Key,
                target => (IReadOnlyDictionary<string, string>)target.ToDictionary(
                    f => f.GrammaticalCase, f => f.Form, StringComparer.Ordinal));

        // A language that does not decline its names calls a record by the heading spelling of its
        // own text where no pass produced a form: the Reina Valera's israelitas, not Israelites.
        // The rule the lists and the record pages name a record by, so a line and a list agree.
        var missing = targets
            .Where(t => !forms.TryGetValue(t, out var cases) || !cases.ContainsKey(GrammaticalCases.Nominative))
            .ToList();
        var printedNames = missing.Count == 0 || !EntityNames.NamesByThePrintedSpelling(named)
            ? []
            : await EntityNames.Of(db, missing, named, cancellationToken);
        foreach (var (target, printed) in printedNames)
        {
            var cases = new Dictionary<string, string>(StringComparer.Ordinal);
            if (forms.TryGetValue(target, out var held))
            {
                foreach (var (grammaticalCase, form) in held)
                {
                    cases[grammaticalCase] = form;
                }
            }

            cases[GrammaticalCases.Nominative] = printed;
            forms[target] = cases;
        }

        // A person headed by the name most of their verses use is called by it in the line too, as
        // their page and the lists call them: companion of Paul rather than of Saul.
        var headings = await RenamedHeadings.Of(db, targets, named, cancellationToken);
        HeadedAs(headings, named, forms);
        clauses = [.. clauses.Select(c => headings.TryGetValue(c.TargetEntityId, out var heading)
            ? c with { TargetEnglishName = heading.Name }
            : c)];

        return clauses
            .GroupBy(c => c.Slug, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => Render(spoken, [.. group], phrasings, forms),
                StringComparer.Ordinal);
    }

    /// <summary>The same for one entity, which is what the entity page and the hover card ask.</summary>
    public static async Task<EntityDescriptorResponse?> Of(
        AppDbContext db,
        string slug,
        string? language,
        CancellationToken cancellationToken,
        string? names = null)
    {
        var found = await Of(db, [slug], language, cancellationToken, names);
        return found.GetValueOrDefault(slug);
    }

    /// <summary>
    /// The renamed people's names in the line's language, where that language heads them differently:
    /// the heading's nominative, and its genitive where the language declines a name — which is named
    /// by the heading only where a printed genitive was found, so a Ukrainian line keeps <em>Савла</em>
    /// rather than say <em>Paul</em> inside a Ukrainian phrase.
    /// </summary>
    private static void HeadedAs(
        IReadOnlyDictionary<int, RenamedHeading> headings,
        string named,
        Dictionary<int, IReadOnlyDictionary<string, string>> forms)
    {
        foreach (var (target, heading) in headings)
        {
            var held = forms.GetValueOrDefault(target);
            if (EntityNames.Local(named) is null)
            {
                if (held is not null)
                {
                    forms[target] = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [GrammaticalCases.Nominative] = heading.Name,
                    };
                }

                continue;
            }

            if (heading.LocalName is not { } local
                || string.Equals(local, held?.GetValueOrDefault(GrammaticalCases.Nominative), StringComparison.Ordinal)
                || (RenamedHeadings.Declines(named) && heading.LocalGenitive is null))
            {
                continue;
            }

            var cases = new Dictionary<string, string>(StringComparer.Ordinal) { [GrammaticalCases.Nominative] = local };
            if (heading.LocalGenitive is { } genitive)
            {
                cases[GrammaticalCases.Genitive] = genitive;
            }

            forms[target] = cases;
        }
    }

    /// <summary>
    /// The clauses of one entity as a line and as the claims that made it. Both, because they are
    /// two different questions: the line is what a reader is shown, and the claims are what lets
    /// them check it — the verse each clause was read from, and who read it.
    ///
    /// <para>
    /// The line comes in a fixed order: parents, spouses, children, siblings, then everything else in
    /// the order its clauses were written. Names that follow one another under one phrase share it —
    /// <em>son of Isaac and Rebekah</em> — and more than three children, or more than three siblings
    /// under one phrase, are counted rather than listed: <em>father of twelve sons and a daughter</em>.
    /// </para>
    /// </summary>
    private static EntityDescriptorResponse Render(
        string language,
        IReadOnlyList<Clause> clauses,
        IReadOnlyDictionary<string, Phrasing> phrasings,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> forms)
    {
        var items = new List<Item>();
        var index = new Dictionary<(string Relation, int Target), Item>();
        foreach (var clause in FamilyTies.Ordered(clauses))
        {
            // A relation with no phrasing cannot be rendered and must not be printed as itself.
            // The loader refuses these, so reaching one here means the vocabulary and the phrasings
            // have drifted apart — which the test over them exists to catch first.
            if (!phrasings.TryGetValue(clause.Relation, out var phrasing))
            {
                continue;
            }

            var key = (clause.Relation, clause.TargetEntityId);
            if (!index.TryGetValue(key, out var item))
            {
                phrasing = DescriptorPhrasings.ByTheSharedParent(language, clause.Relation, clause.SharedParent) ?? phrasing;
                item = new Item(clause, phrasing, phrasings[clause.Relation]);
                index[key] = item;
                items.Add(item);
            }

            item.Clauses.Add(clause);
        }

        var parts = new List<DescriptorPartResponse>();
        var counted = new List<DescriptorCountResponse>();
        foreach (var run in Runs(items, (a, b) => a.First.Relation == b.First.Relation))
        {
            if (FamilyTies.Counts(run[0].First.Relation) && run.Count > MostListed)
            {
                Separate(parts);
                var shared = run.Select(item => item.Phrasing).Distinct().Count() == 1 ? run[0].Phrasing : run[0].Unshared;
                var text = shared.Before + DescriptorPhrasings.Counted(language, Kin(run));
                parts.Add(new DescriptorPartResponse(text) { Doubtful = run.All(item => item.Doubtful) });
                counted.Add(new DescriptorCountResponse(run[0].First.Relation, text));
                continue;
            }

            var said = run.Select(item => (Item: item, Said: DescriptorPhrasings.Say(
                language, item.Phrasing, item.First.TargetKind,
                forms.GetValueOrDefault(item.First.TargetEntityId), item.First.TargetEnglishName))).ToList();
            foreach (var list in Runs(said, (a, b) => a.Item.Phrasing == b.Item.Phrasing
                                                      && a.Item.Phrasing.After.Length == 0
                                                      && b.Said.Before.StartsWith(b.Item.Phrasing.Before, StringComparison.Ordinal)))
            {
                Separate(parts);
                for (var at = 0; at < list.Count; at++)
                {
                    var (item, (before, name)) = list[at];
                    var target = Target(item.First, forms.GetValueOrDefault(item.First.TargetEntityId));
                    if (at == 0)
                    {
                        parts.Add(new DescriptorPartResponse(before) { Doubtful = list.All(one => one.Item.Doubtful) });
                    }
                    else
                    {
                        parts.Add(new DescriptorPartResponse(at == list.Count - 1
                            ? $" {DescriptorPhrasings.Conjunction(language)} "
                            : DescriptorPhrasings.Separator));
                        if (before.Length > item.Phrasing.Before.Length)
                        {
                            parts.Add(new DescriptorPartResponse(before[item.Phrasing.Before.Length..]) { Doubtful = item.Doubtful });
                        }
                    }

                    parts.Add(new DescriptorPartResponse(name) { Entity = target, Doubtful = item.Doubtful });
                    if (item.Phrasing.After.Length > 0)
                    {
                        parts.Add(new DescriptorPartResponse(item.Phrasing.After) { Doubtful = item.Doubtful });
                    }
                }
            }
        }

        var claims = items
            .SelectMany(item => item.Clauses.Select(clause => Claim(clause, forms)))
            .ToList();

        return new EntityDescriptorResponse(language, parts, claims)
        {
            Counted = counted.Count == 0 ? null : counted,
        };
    }

    /// <summary>The most names of children or siblings under one phrase that are listed; more are counted.</summary>
    private const int MostListed = 3;

    private static void Separate(List<DescriptorPartResponse> parts)
    {
        if (parts.Count > 0)
        {
            parts.Add(new DescriptorPartResponse(DescriptorPhrasings.Separator));
        }
    }

    /// <summary>Consecutive elements that belong together by <paramref name="together"/>, as lists.</summary>
    private static IEnumerable<List<T>> Runs<T>(IEnumerable<T> items, Func<T, T, bool> together)
    {
        List<T>? run = null;
        foreach (var item in items)
        {
            if (run is not null && together(run[^1], item))
            {
                run.Add(item);
                continue;
            }

            if (run is not null)
            {
                yield return run;
            }

            run = [item];
        }

        if (run is not null)
        {
            yield return run;
        }
    }

    /// <summary>
    /// Whom a counted run counts: sons and daughters, or brothers and sisters, where every one has a
    /// known sex; otherwise all of them as children or as siblings, since a count that guessed one
    /// would be wrong in a way the reader cannot see.
    /// </summary>
    private static List<(Kin Kin, int Count)> Kin(IReadOnlyList<Item> run)
    {
        var children = !FamilyTies.Siblings.Contains(run[0].First.Relation);
        var sexes = run.Select(item => item.Clauses.Select(c => c.TargetSex).FirstOrDefault(s => s is not null)).ToList();
        if (sexes.Any(sex => sex is not (SharedParents.Male or SharedParents.Female)))
        {
            return [(children ? Endpoints.Kin.Child : Endpoints.Kin.Sibling, run.Count)];
        }

        var male = sexes.Count(sex => sex == SharedParents.Male);
        return
        [
            (children ? Endpoints.Kin.Son : Endpoints.Kin.Brother, male),
            (children ? Endpoints.Kin.Daughter : Endpoints.Kin.Sister, run.Count - male),
        ];
    }

    private static DescriptorClaimResponse Claim(
        Clause clause,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> forms) =>
        new(
            clause.Ordinal,
            clause.Relation,
            Target(clause, forms.GetValueOrDefault(clause.TargetEntityId)),
            new VerseRefResponse(
                clause.Book,
                BookReferences.Name(clause.Book),
                BookReferences.Slug(clause.Book),
                clause.Chapter,
                clause.Verse),
            EnumSpelling.Of(clause.Method),
            clause.Confidence,
            clause.Source,
            clause.Doubtful)
        {
            Note = clause.Note,
            Verses = Relationships.Verses(clause.Citation),
            Witness = clause.Witness,
            Original = clause.Original,
            SharedParent = clause.SharedParent,
            Stated = clause.Stated,
        };

    /// <summary>
    /// The target as a client needs it to draw a link: what kind of page it is, what it is called
    /// in this language, every case of that name the pass produced, and the English name that
    /// stands in for a case it did not.
    ///
    /// <para>
    /// <see cref="DescriptorTargetResponse.Forms"/> is keyed by case rather than being a fixed pair
    /// of fields, which is what makes the locative Ukrainian needs for <em>місто в Юдеї</em>
    /// additive: a language turning out to need a fourth case is a phrasing and a generation pass,
    /// not a change to the wire.
    /// </para>
    /// </summary>
    private static DescriptorTargetResponse Target(
        Clause clause,
        IReadOnlyDictionary<string, string>? cases) =>
        new(
            EnumSpelling.Of(clause.TargetKind),
            clause.TargetSlug,
            cases?.GetValueOrDefault(GrammaticalCases.Nominative) ?? clause.TargetEnglishName,
            clause.TargetEnglishName)
        {
            Forms = cases is { Count: > 0 }
                ? new Dictionary<string, string>(cases, StringComparer.Ordinal)
                : null,
            Distinguisher = clause.TargetDistinguisher,
        };

    /// <summary>
    /// One relation and target as the line says it once, with every clause that states it: the
    /// subject's own and the other person's, turned. A part is doubtful only when every claim behind
    /// it is, so a later claim of the same fact lifts the doubt.
    /// </summary>
    private sealed class Item(Clause first, Phrasing phrasing, Phrasing plain)
    {
        public Clause First { get; } = first;

        /// <summary>The phrasing the item is said with, by the shared parent where a language has a word for it.</summary>
        public Phrasing Phrasing { get; } = phrasing;

        /// <summary>The table's own phrasing, which a count of half-siblings by different parents falls back to.</summary>
        public Phrasing Unshared { get; } = plain;

        public List<Clause> Clauses { get; } = [];

        public bool Doubtful => Clauses.All(c => c.Doubtful);
    }

    /// <summary>One clause with its target flattened, so the render is a loop and not a join.</summary>
    internal sealed record Clause(
        string Slug,
        int Ordinal,
        string Relation,
        int TargetEntityId,
        EntityKind TargetKind,
        string TargetSlug,
        string TargetEnglishName,
        string? TargetDistinguisher,
        int Book,
        int Chapter,
        int Verse,
        LinkMethod Method,
        double? Confidence,
        string Source,
        string? Note)
    {
        public string? Citation { get; init; }

        public string? Witness { get; init; }

        /// <summary>For a half-sibling, whether the two share a father or a mother, where the corpus knows.</summary>
        public string? SharedParent { get; init; }

        public string? Original { get; init; }

        /// <summary>The entity the clause is said of, by id.</summary>
        public int SubjectId { get; init; }

        public string? SubjectSex { get; init; }

        public string? TargetSex { get; init; }

        /// <summary>
        /// For a tie written on the other person and turned to this side, the relation as it was
        /// written there: <c>son-of</c> on Reuben's record, read on Jacob's as <c>father-of</c>. Null
        /// for a clause of the subject's own.
        /// </summary>
        public string? Stated { get; init; }

        public bool Doubtful => Confidence is { } confidence && confidence < Plain;
    }
}

/// <param name="Language">
/// The language actually rendered, never the one asked for. A language the encyclopedia has no
/// phrasings for is answered in English and says <c>eng</c> here, because a client told otherwise
/// puts Ukrainian grammar around an English name and prints <em>син Reuel</em>.
/// </param>
/// <param name="Parts">
/// The line, in order. Concatenating <c>text</c> gives the sentence; a part carrying
/// <c>entity</c> is the piece to make a link of. There is no markup here and there is not meant
/// to be.
/// </param>
/// <param name="Claims">
/// What the line is made of, with the verse each clause was read from and who read it. This is
/// what lets a reader check a claim rather than believe it, and what an entity page shows beside
/// the line.
/// </param>
internal record EntityDescriptorResponse(
    string Language,
    IList<DescriptorPartResponse> Parts,
    IList<DescriptorClaimResponse> Claims)
{
    /// <summary>
    /// The relations the line says as a count rather than by name, each with the words it says it
    /// in: <em>father of twelve sons and a daughter</em>. A client drawing the line from the claims
    /// puts these words where the run of that relation's claims would stand. Null where nothing is
    /// counted.
    /// </summary>
    public IList<DescriptorCountResponse>? Counted { get; init; }
}

/// <param name="Relation">The relation whose claims the count stands for: <c>father-of</c>.</param>
/// <param name="Text">The whole clause, the phrase and the count, in the language rendered.</param>
internal record DescriptorCountResponse(string Relation, string Text);

/// <param name="Doubtful">
/// The clause this part belongs to is below the confidence at which the corpus states something
/// plainly. It is still sent, because dropping it would make the description read as more certain
/// than it is; how a doubtful piece is marked is the client's.
/// </param>
internal record DescriptorPartResponse(string Text)
{
    public DescriptorTargetResponse? Entity { get; init; }

    public bool Doubtful { get; init; }
}

/// <summary>
/// Whom or what a clause names, complete enough to be drawn as a link without asking again.
/// </summary>
/// <param name="Kind">
/// <c>person</c>, <c>place</c> or <c>people</c>. A slug does not say which, and the client routes
/// each to a different page, so without this a link cannot be built at all.
/// </param>
/// <param name="Name">
/// The nominative in the language being rendered, or the English name where the pass produced no
/// form for it. This is the name to print where the phrase does not inflect.
/// </param>
/// <param name="EnglishName">
/// The name the corpus holds, which every entity has. It is what a case the pass did not produce
/// falls back to, and the client needs it because the fallback happens where the phrase is built.
/// </param>
internal record DescriptorTargetResponse(
    string Kind,
    string Slug,
    string Name,
    string EnglishName)
{
    /// <summary>
    /// The name in every case a pass produced for this language, keyed by case — <c>nominative</c>,
    /// <c>genitive</c>, <c>locative</c>. Null where no pass has produced any form for this entity in
    /// this language, which is where <see cref="EnglishName"/> is the whole of what can be said.
    ///
    /// A map and not a pair of fields: <em>місто в Юдеї</em> needs a locative that the genitive
    /// cannot stand in for, and the next language to need a case should cost a phrasing rather than
    /// a change to the wire.
    /// </summary>
    public Dictionary<string, string>? Forms { get; init; }

    /// <summary>
    /// The imported sentence, where the target still has one. It is what a link's own hover shows
    /// until a description has been generated for the entity on the other end.
    /// </summary>
    public string? Distinguisher { get; init; }
}

/// <param name="Confidence">
/// How sure the method that produced this clause was, and null exactly where a person or a source
/// stated it rather than a process concluding it.
/// </param>
internal record DescriptorClaimResponse(
    int Ordinal,
    string Relation,
    DescriptorTargetResponse Target,
    VerseRefResponse Reference,
    string Method,
    double? Confidence,
    string Source,
    bool Doubtful)
{
    /// <summary>Why, in the words the row carries. Usually the model's own sentence for the clause.</summary>
    public string? Note { get; init; }

    /// <summary>
    /// Every verse the clause rests on where one does not hold it — a passage, or two verses
    /// composed — the first being <see cref="Reference"/>. Null for a clause read from one verse.
    /// </summary>
    public IList<VerseRefResponse>? Verses { get; init; }

    /// <summary>
    /// The text the clause was read in, by its slug, where that was an original rather than the
    /// King James every clause is shown in: <c>BHSA</c>, <c>NESTLE1904</c>. Null for a reading of
    /// the English.
    /// </summary>
    public string? Witness { get; init; }

    /// <summary>The words of <see cref="Witness"/> the clause rests on, as that text prints them.</summary>
    public string? Original { get; init; }

    /// <summary>
    /// For a half-sibling, the parent the two share where the corpus records one: <c>father</c> or
    /// <c>mother</c>, which a language with a word for each says. Null otherwise.
    /// </summary>
    public string? SharedParent { get; init; }

    /// <summary>
    /// Where the claim was written on the other person's record and is read here from this side, the
    /// relation as written there: Reuben's <c>son-of</c> Jacob, shown on Jacob as <c>father-of</c>
    /// Reuben. Null for a claim written on this record.
    /// </summary>
    public string? Stated { get; init; }
}
