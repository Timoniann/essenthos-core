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
                d.Source)
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
                d.Source)
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

        var subjects = await db.Entities
            .Where(e => wanted.Contains(e.Slug))
            .Select(e => new { e.Id, e.Slug, e.Kind, e.Sex })
            .ToListAsync(cancellationToken);
        var people = subjects.Where(e => e.Kind == EntityKind.Person && !FamilyEndpoints.Deities.Contains(e.Slug))
            .Select(e => e.Id)
            .ToList();
        var (statedCounts, places) = await KinFacts.Statements(db, people, cancellationToken);
        var siblings = await SharedParents.Siblings(db, people, cancellationToken);

        if (own.Count == 0 && written.Count == 0 && statedCounts.Count == 0 && siblings.Count == 0)
        {
            return [];
        }

        // The sex our own rows state, for the people the dataset gives none: the subject a written
        // tie is turned for, and the child or sibling a count has to call a son or a daughter.
        var unsexed = written.Where(c => c.SubjectSex is null).Select(c => c.SubjectId)
            .Concat(own.Where(c => c.TargetSex is null && FamilyTies.Counts(c.Relation)).Select(c => c.TargetEntityId))
            .Concat(subjects.Where(e => e.Sex is null).Select(e => e.Id))
            .Concat(siblings.Select(s => s.Sibling))
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

        // The brothers and sisters the parents give, where no clause says so of the pair already.
        var subjectSex = subjects.ToDictionary(e => e.Id, e => SexOf(e.Id, e.Sex));
        var subjectSlugs = subjects.ToDictionary(e => e.Id, e => e.Slug);
        var tied = clauses.Where(c => FamilyTies.Siblings.Contains(c.Relation))
            .Select(c => (c.SubjectId, c.TargetEntityId))
            .ToHashSet();
        var unsaid = siblings
            .Where(s => !tied.Contains((s.Subject, s.Sibling))
                        && subjectSex.GetValueOrDefault(s.Subject) is SharedParents.Male or SharedParents.Female)
            .ToList();
        var kin = await Persons(db, unsaid.Select(s => s.Sibling), cancellationToken);
        clauses.AddRange(unsaid.Where(s => kin.ContainsKey(s.Sibling)).Select(s => Derived(
            subjectSlugs[s.Subject],
            s.Subject,
            subjectSex[s.Subject],
            subjectSex[s.Subject] == SharedParents.Male ? DescriptorRelations.BrotherOf : DescriptorRelations.SisterOf,
            kin[s.Sibling] with { Sex = SexOf(s.Sibling, kin[s.Sibling].Sex) },
            [s.Theirs, s.Mine],
            s.Confidence,
            ByTheParents)));

        var ancestors = await Ancestors(db, people, clauses, subjectSlugs, subjectSex, cancellationToken);
        clauses = ancestors.Clauses;

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

        var taught = await Companies(db, clauses, cancellationToken);
        var targets = clauses.Select(c => c.TargetEntityId)
            .Concat(taught.Members.Values.SelectMany(members => members.Select(m => m.Id)))
            .Distinct()
            .ToList();
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
        taught = taught.HeadedAs(headings);

        var lines = new Dictionary<string, EntityDescriptorResponse>(StringComparer.Ordinal);
        foreach (var subject in subjects)
        {
            var said = clauses.Where(c => c.SubjectId == subject.Id).ToList();
            var facts = new LineFacts(
                subjectSex.GetValueOrDefault(subject.Id),
                statedCounts.GetValueOrDefault(subject.Id),
                places.Where(p => p.Key.Child == subject.Id).ToDictionary(p => p.Key.Parent, p => p.Value),
                ancestors.Shown.GetValueOrDefault(subject.Id) ?? [],
                ancestors.More.Contains(subject.Id))
            {
                Taught = taught,
                OtherDisciples = taught.Others.GetValueOrDefault(subject.Id),
            };
            if (said.Count == 0 && facts.Children is null)
            {
                continue;
            }

            var line = Render(spoken, said, phrasings, forms, facts);
            if (line.Parts.Count > 0)
            {
                lines[subject.Slug] = line;
            }
        }

        return lines;
    }

    /// <summary>Whom a clause the line works out rather than reads off a record is credited to.</summary>
    private const string ByTheParents = "Essenthos, from the parents the records give";

    private const string ByTheTree = "Essenthos, from the family tree";

    /// <summary>The most descendants a line names; more are said as <em>and others</em>.</summary>
    private const int MostDescendants = 4;

    /// <summary>
    /// The descendants each person's line names: the ones the family tree reaches below their
    /// children and the ones a record states, chosen by how many verses name them, the most named
    /// first. A child, or anyone the line already names otherwise, is not one of them. One the tree
    /// reaches is said from the tree, with the verse that ties the two and the one that names the
    /// descendant; one only a record states stays as the record wrote it.
    ///
    /// <para>
    /// **Someone who lived before the Flood names only descendants born before it.** Everyone born
    /// after it descends from Adam and from Noah alike, so <em>ancestor of Jesus, David, Jacob,
    /// Moses</em> is true of both and tells a reader nothing about either: Adam's line names Enoch,
    /// Methuselah and Noah, and Noah's names none beyond his sons. The record's own statement stays
    /// where it is, in the relationships and on the family tree; the line leaves it out.
    /// </para>
    /// </summary>
    private static async Task<(List<Clause> Clauses, Dictionary<int, List<int>> Shown, HashSet<int> More)> Ancestors(
        AppDbContext db,
        IReadOnlyCollection<int> people,
        List<Clause> clauses,
        IReadOnlyDictionary<int, string> slugs,
        IReadOnlyDictionary<int, string?> sexes,
        CancellationToken cancellationToken)
    {
        var reach = await KinFacts.Descendants(db, people, cancellationToken);
        bool Unsaid(int person, int descendant) =>
            reach.BeforeTheFlood.Contains(person) && reach.AfterTheFlood.Contains(descendant);
        clauses = [.. clauses.Where(c => c.Relation != DescriptorRelations.AncestorOf || !Unsaid(c.SubjectId, c.TargetEntityId))];

        var candidates = new Dictionary<int, Dictionary<int, Descendant?>>();
        foreach (var person in people)
        {
            var mine = clauses.Where(c => c.SubjectId == person).ToList();
            var onTheLine = mine.Where(c => c.Relation != DescriptorRelations.AncestorOf).Select(c => c.TargetEntityId).ToHashSet();
            var chosen = new Dictionary<int, Descendant?>();
            foreach (var descendant in reach.Below.GetValueOrDefault(person) ?? [])
            {
                if (descendant.Depth >= 2 && !onTheLine.Contains(descendant.Id) && !Unsaid(person, descendant.Id))
                {
                    chosen[descendant.Id] = descendant;
                }
            }

            foreach (var written in mine.Where(c => c.Relation == DescriptorRelations.AncestorOf))
            {
                chosen.TryAdd(written.TargetEntityId, null);
            }

            if (chosen.Count > 0)
            {
                candidates[person] = chosen;
            }
        }

        var named = await KinFacts.VerseCounts(db, [.. candidates.Values.SelectMany(c => c.Keys)], cancellationToken);
        var shown = new Dictionary<int, List<int>>();
        var more = new HashSet<int>();
        foreach (var (person, chosen) in candidates)
        {
            var ranked = chosen.Keys
                .OrderByDescending(id => named.GetValueOrDefault(id))
                .ThenBy(id => chosen[id]?.Depth ?? int.MaxValue)
                .ThenBy(id => id)
                .ToList();
            shown[person] = [.. ranked.Take(MostDescendants)];
            if (ranked.Count > MostDescendants)
            {
                more.Add(person);
            }
        }

        var reached = shown
            .SelectMany(p => p.Value
                .Where(id => candidates[p.Key][id] is not null)
                .Select(id => (Person: p.Key, Descendant: candidates[p.Key][id]!)))
            .ToList();
        var known = await Persons(db, reached.Select(r => r.Descendant.Id), cancellationToken);
        var derived = reached
            .Where(r => known.ContainsKey(r.Descendant.Id))
            .Select(r => Derived(
                slugs[r.Person],
                r.Person,
                sexes.GetValueOrDefault(r.Person),
                DescriptorRelations.AncestorOf,
                known[r.Descendant.Id],
                [.. new[] { r.Descendant.Tie, r.Descendant.Named }.OfType<VerseRefResponse>()],
                r.Descendant.Confidence,
                ByTheTree))
            .ToLookup(c => c.SubjectId);

        // The tree's beside the record's own, where the record has some, so the line keeps one place for them.
        var all = new List<Clause>(clauses.Count);
        foreach (var person in clauses.Select(c => c.SubjectId).Concat(derived.Select(g => g.Key)).Distinct())
        {
            var mine = clauses.Where(c => c.SubjectId == person).ToList();
            var at = mine.FindLastIndex(c => c.Relation == DescriptorRelations.AncestorOf && c.Stated is null);
            mine.InsertRange(at < 0 ? mine.FindLastIndex(c => c.Stated is null) + 1 : at + 1, derived[person]);
            all.AddRange(mine);
        }

        return (all, shown, more);
    }

    /// <summary>The most members of a company a teacher's line names; more are said as <em>and others</em>.</summary>
    private const int MostMembers = 4;

    /// <summary>
    /// The companies these lines say someone taught — the Twelve, the Seventy — with the members each
    /// line names among them, and how many disciples each teacher had beyond them. The members are the
    /// ones the records say are <em>one of</em> the company, in the order of the verses that say so and
    /// then by how many verses name them; the other disciples, everyone a record says was the
    /// teacher's disciple, or whom he taught, outside every company he taught.
    /// </summary>
    private static async Task<Taught> Companies(
        AppDbContext db,
        IReadOnlyList<Clause> clauses,
        CancellationToken cancellationToken)
    {
        var taught = clauses
            .Where(c => c.Relation == DescriptorRelations.TeacherOf && c.TargetKind == EntityKind.People)
            .ToList();
        if (taught.Count == 0)
        {
            return Taught.Nothing;
        }

        var companies = taught.Select(c => c.TargetEntityId).Distinct().ToList();
        var teachers = taught.Select(c => c.SubjectId).Distinct().ToList();
        var rows = await db.EntityRelationships
            .Where(r => (r.Type == DescriptorRelations.MemberOf && companies.Contains(r.ToEntityId))
                        || (r.Type == DescriptorRelations.DiscipleOf && teachers.Contains(r.ToEntityId))
                        || (r.Type == DescriptorRelations.TeacherOf && teachers.Contains(r.FromEntityId)))
            .Select(r => new
            {
                r.Type,
                From = r.FromEntityId,
                FromKind = r.From!.Kind,
                FromSlug = r.From.Slug,
                FromName = r.From.Name,
                FromDistinguisher = r.From.Distinguisher,
                FromSex = r.From.Sex,
                To = r.ToEntityId,
                ToKind = r.To!.Kind,
                r.CanonicalBook,
                r.CanonicalChapter,
                r.CanonicalVerse,
            })
            .ToListAsync(cancellationToken);

        var joined = rows
            .Where(r => r.Type == DescriptorRelations.MemberOf && r.FromKind == EntityKind.Person)
            .ToList();
        var named = await KinFacts.VerseCounts(db, [.. joined.Select(r => r.From).Distinct()], cancellationToken);
        var members = joined
            .GroupBy(r => r.To)
            .ToDictionary(
                company => company.Key,
                company => (IReadOnlyList<Person>)
                [
                    .. company
                        .OrderBy(r => r.CanonicalBook).ThenBy(r => r.CanonicalChapter).ThenBy(r => r.CanonicalVerse)
                        .ThenByDescending(r => named.GetValueOrDefault(r.From))
                        .ThenBy(r => r.From)
                        .DistinctBy(r => r.From)
                        .Select(r => new Person(r.From, r.FromKind, r.FromSlug, r.FromName, r.FromDistinguisher, r.FromSex)),
                ]);

        var others = new Dictionary<int, int>();
        foreach (var teacher in teachers)
        {
            var within = taught
                .Where(c => c.SubjectId == teacher)
                .SelectMany(c => members.GetValueOrDefault(c.TargetEntityId) ?? [])
                .Select(person => person.Id)
                .ToHashSet();
            var disciples = rows
                .Where(r => r.Type == DescriptorRelations.DiscipleOf && r.To == teacher && r.FromKind == EntityKind.Person)
                .Select(r => r.From)
                .Concat(rows
                    .Where(r => r.Type == DescriptorRelations.TeacherOf && r.From == teacher && r.ToKind == EntityKind.Person)
                    .Select(r => r.To))
                .Where(id => id != teacher && !within.Contains(id))
                .Distinct()
                .Count();
            if (disciples > 0)
            {
                others[teacher] = disciples;
            }
        }

        return new Taught(members, others);
    }

    /// <summary>
    /// The companies a teacher's line names and the members of each, by the company's id; and by the
    /// teacher's id, how many disciples the records give him beyond them.
    /// </summary>
    private sealed record Taught(
        IReadOnlyDictionary<int, IReadOnlyList<Person>> Members,
        IReadOnlyDictionary<int, int> Others)
    {
        public static readonly Taught Nothing = new(new Dictionary<int, IReadOnlyList<Person>>(), new Dictionary<int, int>());

        /// <summary>The members called by the name most of their verses use, as every other name in a line is.</summary>
        public Taught HeadedAs(IReadOnlyDictionary<int, RenamedHeading> headings) =>
            headings.Count == 0 || Members.Count == 0
                ? this
                : this with
                {
                    Members = Members.ToDictionary(
                        company => company.Key,
                        company => (IReadOnlyList<Person>)
                        [
                            .. company.Value.Select(person =>
                                headings.TryGetValue(person.Id, out var heading) ? person with { Name = heading.Name } : person),
                        ]),
                };
    }

    /// <summary>The people a line names that no clause brought along, as a clause needs them.</summary>
    private static async Task<Dictionary<int, Person>> Persons(
        AppDbContext db,
        IEnumerable<int> ids,
        CancellationToken cancellationToken)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        return await db.Entities
            .Where(e => wanted.Contains(e.Id))
            .Select(e => new Person(e.Id, e.Kind, e.Slug, e.Name, e.Distinguisher, e.Sex))
            .ToDictionaryAsync(e => e.Id, cancellationToken);
    }

    private sealed record Person(int Id, EntityKind Kind, string Slug, string Name, string? Distinguisher, string? Sex);

    /// <summary>A clause no record wrote and the line works out, with the verses it rests on.</summary>
    private static Clause Derived(
        string slug,
        int subject,
        string? subjectSex,
        string relation,
        Person target,
        IReadOnlyList<VerseRefResponse> verses,
        double? confidence,
        string source)
    {
        var distinct = verses.DistinctBy(v => (v.BookOrdinal, v.Chapter, v.Verse)).ToList();
        var first = distinct.FirstOrDefault();
        return new Clause(
            slug,
            0,
            relation,
            target.Id,
            target.Kind,
            target.Slug,
            target.Name,
            target.Distinguisher,
            first?.BookOrdinal ?? 0,
            first?.Chapter ?? 0,
            first?.Verse ?? 0,
            LinkMethod.RuleBased,
            confidence,
            source)
        {
            SubjectId = subject,
            SubjectSex = subjectSex,
            TargetSex = target.Sex,
            Verses = distinct.Count > 1 ? distinct : null,
        };
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
    /// The line comes in a fixed order: offices, parents, spouses, children, siblings, then everything
    /// else in the order its clauses were written. Names that follow one another under one phrase share
    /// it — <em>son of Isaac and Rebekah</em> — and more than three children, or more than three
    /// siblings under one phrase, are counted rather than listed: <em>father of twelve sons and a
    /// daughter</em>. Children no verse counts are counted as the named ones (<em>father of seven named
    /// sons</em>); where a verse counts more than are named, its count is said and the named are named
    /// among them; a child's place among a parent's children is said where a verse states it.
    /// </para>
    /// </summary>
    private static EntityDescriptorResponse Render(
        string language,
        IReadOnlyList<Clause> clauses,
        IReadOnlyDictionary<string, Phrasing> phrasings,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> forms,
        LineFacts facts)
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

            phrasing = DescriptorPhrasings.OfSubject(language, clause.Relation, facts.Sex) ?? phrasing;
            var plain = phrasing;

            var key = (clause.Relation, clause.TargetEntityId);
            if (!index.TryGetValue(key, out var item))
            {
                phrasing = DescriptorPhrasings.ByTheSharedParent(language, clause.Relation, clause.SharedParent) ?? phrasing;
                var place = clause.Relation is DescriptorRelations.SonOf or DescriptorRelations.DaughterOf
                    ? facts.Places.GetValueOrDefault(clause.TargetEntityId)
                    : null;
                var ordinal = place is null
                    ? null
                    : DescriptorPhrasings.Ordinal(language, phrasing, clause.Relation == DescriptorRelations.DaughterOf, place.Ordinal);
                item = new Item(clause, ordinal ?? phrasing, plain) { Place = ordinal is null ? null : place };
                index[key] = item;
                items.Add(item);
            }

            item.Clauses.Add(clause);
        }

        var parts = new List<DescriptorPartResponse>();
        var counted = new List<DescriptorCountResponse>();
        var claimed = 0;

        // A clause the API says in its own words is handed to a client that draws the line from the
        // claims as the words of its relation's run, with the names in it and the verses it cites;
        // where the line has no claim of that relation, with the place among the claims it stands at.
        void Said(string relation, int from, IReadOnlyList<VerseRefResponse>? verses, bool placed)
        {
            var words = parts.Skip(from).SkipWhile(p => p.Text == DescriptorPhrasings.Separator && p.Entity is null).ToList();
            counted.Add(new DescriptorCountResponse(relation, string.Concat(words.Select(p => p.Text)))
            {
                Parts = words,
                References = verses is { Count: > 0 } ? [.. verses] : null,
                At = placed ? claimed : null,
            });
        }

        var children = facts.Children is null;
        foreach (var run in Runs(items, (a, b) => a.First.Relation == b.First.Relation))
        {
            var relation = run[0].First.Relation;
            if (!children && FamilyTies.Rank(relation) > FamilyTies.ChildrenRank)
            {
                Children([]);
                children = true;
            }

            var from = parts.Count;
            if (relation is DescriptorRelations.FatherOf or DescriptorRelations.MotherOf && facts.Children is not null)
            {
                Children(run);
                children = true;
            }
            else if (relation == DescriptorRelations.AncestorOf)
            {
                var order = facts.Ancestors.Select((id, at) => (id, at)).ToDictionary(p => p.id, p => p.at);
                var shown = order.Count == 0
                    ? run
                    : [.. run.Where(item => order.ContainsKey(item.First.TargetEntityId)).OrderBy(item => order[item.First.TargetEntityId])];
                Separate(parts);
                parts.AddRange(Listed(language, shown, forms, closed: !facts.MoreAncestors));
                if (facts.MoreAncestors)
                {
                    parts.Add(new DescriptorPartResponse(DescriptorPhrasings.AndOthers(language)));
                }

                Said(relation, from, [.. shown.Select(item => VerseOf(item.First)).DistinctBy(v => (v.BookOrdinal, v.Chapter, v.Verse))], placed: false);
            }
            else if (relation == DescriptorRelations.TeacherOf
                     && run.Any(item => facts.Taught.Members.ContainsKey(item.First.TargetEntityId)))
            {
                Separate(parts);
                foreach (var part in Listed(language, run, forms, closed: facts.OtherDisciples == 0))
                {
                    parts.Add(part);
                    var company = run.FirstOrDefault(item => part.Entity is not null && item.First.TargetSlug == part.Entity.Slug);
                    if (company is not null && facts.Taught.Members.TryGetValue(company.First.TargetEntityId, out var members))
                    {
                        parts.AddRange(Members(language, members, forms));
                    }
                }

                if (facts.OtherDisciples > 0)
                {
                    parts.Add(new DescriptorPartResponse(DescriptorPhrasings.OtherDisciples(language, facts.OtherDisciples)));
                }

                Said(relation, from, [.. run.Select(item => VerseOf(item.First)).DistinctBy(v => (v.BookOrdinal, v.Chapter, v.Verse))], placed: false);
            }
            else if (FamilyTies.Counts(relation) && run.Count > (FamilyTies.Siblings.Contains(relation) ? MostSiblingsListed : MostListed))
            {
                Separate(parts);
                var shared = run.Select(item => item.Phrasing).Distinct().Count() == 1 ? run[0].Phrasing : run[0].Unshared;
                var named = !FamilyTies.Siblings.Contains(relation);
                var text = shared.Before + DescriptorPhrasings.Counted(language, Kin(run), named);
                parts.Add(new DescriptorPartResponse(text) { Doubtful = run.All(item => item.Doubtful) });
                counted.Add(new DescriptorCountResponse(relation, text));
            }
            else
            {
                Separate(parts);
                parts.AddRange(Listed(language, run, forms, closed: true));
                if (run.Any(item => item.Place is not null))
                {
                    Said(relation, from, [.. run.SelectMany(item => item.Place?.Verses ?? [])], placed: false);
                }
            }

            claimed += run.Sum(item => item.Clauses.Count);
        }

        if (!children)
        {
            Children([]);
        }

        // The children a person had: the ones a verse counts, and among them the ones the records
        // name; or the ones named, and more than them where a verse says so. Then the grandsons a
        // verse counts.
        void Children(List<Item> run)
        {
            var stated = facts.Children;
            var relation = run.Count > 0 ? run[0].First.Relation
                : facts.Sex == SharedParents.Female ? DescriptorRelations.MotherOf
                : facts.Sex == SharedParents.Male ? DescriptorRelations.FatherOf
                : null;
            if (relation is null)
            {
                return;
            }

            var lead = phrasings[relation].Before;
            var named = run.Count;
            var kin = named == 0 ? [] : Kin(run);
            var daughters = kin.Count > 0 && kin.All(k => k.Kin == Endpoints.Kin.Daughter || k.Count == 0);
            var counts = stated is { Counts: true } ? Stated(stated, kin, named) : null;
            var from = parts.Count;
            var doubtful = run.Count > 0 && run.All(item => item.Doubtful);
            var own = false;
            if (counts is not null && counts.Sum(c => c.Count) > named)
            {
                Separate(parts);
                parts.Add(new DescriptorPartResponse(lead + DescriptorPhrasings.Counted(language, counts)) { Doubtful = doubtful });
                if (named is > 0 and <= MostListed)
                {
                    parts.Add(new DescriptorPartResponse(DescriptorPhrasings.AmongThem(language, named, daughters)));
                    parts.AddRange(Listed(language, run, forms, closed: true, bare: true));
                }
                else if (named > MostListed)
                {
                    parts.Add(new DescriptorPartResponse(DescriptorPhrasings.OfThemNamed(language, named, daughters)));
                }

                own = true;
            }
            else if (named == 0)
            {
                if (stated is not { More: true })
                {
                    return;
                }

                Separate(parts);
                parts.Add(new DescriptorPartResponse(lead + DescriptorPhrasings.Unnamed(language, stated.MoreSons, stated.MoreDaughters)));
                own = true;
            }
            else if (named <= MostListed)
            {
                var more = stated is { More: true };
                Separate(parts);
                parts.AddRange(Listed(language, run, forms, closed: !more));
                if (more)
                {
                    parts.Add(new DescriptorPartResponse(DescriptorPhrasings.Others(language, stated!.MoreSons, stated.MoreDaughters, afterCount: false)));
                }

                own = more;
            }
            else
            {
                var text = lead + DescriptorPhrasings.Counted(language, kin, named: counts is null);
                if (stated is { More: true })
                {
                    text += DescriptorPhrasings.Others(language, stated.MoreSons, stated.MoreDaughters, afterCount: true);
                }

                Separate(parts);
                parts.Add(new DescriptorPartResponse(text) { Doubtful = doubtful });
                own = true;
            }

            if (own)
            {
                Said(relation, from, stated?.Verses, placed: run.Count == 0);
            }

            if (stated?.Grandsons is { } grandsons && grandsons > 0)
            {
                var grand = relation == DescriptorRelations.MotherOf ? DescriptorRelations.GrandmotherOf : DescriptorRelations.GrandfatherOf;
                var at = parts.Count;
                Separate(parts);
                parts.Add(new DescriptorPartResponse(phrasings[grand].Before
                    + DescriptorPhrasings.Counted(language, [(Endpoints.Kin.Grandson, grandsons)])));

                // It stands after the children's claims, which the run has not counted yet.
                var before = claimed;
                claimed += run.Sum(item => item.Clauses.Count);
                Said(grand, at, stated.Verses, placed: true);
                claimed = before;
            }
        }

        var claims = items
            .SelectMany(item => item.Clauses.Select(clause => Claim(clause, forms)))
            .ToList();

        return new EntityDescriptorResponse(language, parts, claims)
        {
            Counted = counted.Count == 0 ? null : counted,
            Sex = facts.Sex,
        };
    }

    /// <summary>The most names of children under one phrase that are listed; more are counted.</summary>
    private const int MostListed = 3;

    /// <summary>
    /// The most brothers or sisters under one phrase that are listed. Four, because a verse that names
    /// a man's brothers names them so — <em>James, and Joses, and Simon, and Judas</em> (MAT 13:55) —
    /// and the owner asked for them by name; Jacob's eleven and David's six stay counted.
    /// </summary>
    private const int MostSiblingsListed = 4;

    /// <summary>A name with no words before it, in the nominative: the names after <em>з них названі</em>.</summary>
    private static readonly Phrasing Bare = new(string.Empty, GrammaticalCases.Nominative);

    /// <summary>
    /// A run of names under their phrases: <em>син Ісака та Ревеки</em>. Names that follow one another
    /// under one phrase share it. An open list ends without the conjunction, for the words that
    /// follow it to close: <em>батько Каїна, Авеля, Сифа</em> and other sons and daughters.
    /// </summary>
    private static List<DescriptorPartResponse> Listed(
        string language,
        IReadOnlyList<Item> run,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> forms,
        bool closed,
        bool bare = false)
    {
        var parts = new List<DescriptorPartResponse>();
        var said = run.Select(item => (Item: item, Phrasing: bare ? Bare : item.Phrasing)).Select(one => (one.Item, one.Phrasing,
            Said: DescriptorPhrasings.Say(language, one.Phrasing, one.Item.First.TargetKind,
                forms.GetValueOrDefault(one.Item.First.TargetEntityId), one.Item.First.TargetEnglishName))).ToList();
        var lists = Runs(said, (a, b) => a.Phrasing == b.Phrasing
                                         && a.Phrasing.After.Length == 0
                                         && b.Said.Before.StartsWith(b.Phrasing.Before, StringComparison.Ordinal)).ToList();
        for (var at = 0; at < lists.Count; at++)
        {
            var list = lists[at];
            Separate(parts);
            for (var place = 0; place < list.Count; place++)
            {
                var (item, phrasing, (before, name)) = list[place];
                var target = Target(item.First, forms.GetValueOrDefault(item.First.TargetEntityId));
                if (place == 0)
                {
                    if (before.Length > 0)
                    {
                        parts.Add(new DescriptorPartResponse(before) { Doubtful = list.All(one => one.Item.Doubtful) });
                    }
                }
                else
                {
                    parts.Add(new DescriptorPartResponse(place == list.Count - 1 && (closed || at < lists.Count - 1)
                        ? $" {DescriptorPhrasings.Conjunction(language)} "
                        : DescriptorPhrasings.Separator));
                    if (before.Length > phrasing.Before.Length)
                    {
                        parts.Add(new DescriptorPartResponse(before[phrasing.Before.Length..]) { Doubtful = item.Doubtful });
                    }
                }

                parts.Add(new DescriptorPartResponse(name) { Entity = target, Doubtful = item.Doubtful });
                if (phrasing.After.Length > 0)
                {
                    parts.Add(new DescriptorPartResponse(phrasing.After) { Doubtful = item.Doubtful });
                }
            }
        }

        return parts;
    }

    /// <summary>
    /// Some members of a company after its name, in the nominative whatever case the company's own
    /// phrase put it in: <em>Дванадцятьох (Петро, Іван, Яків, Андрій та інші)</em>.
    /// </summary>
    private static List<DescriptorPartResponse> Members(
        string language,
        IReadOnlyList<Person> members,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> forms)
    {
        var shown = members.Take(MostMembers).ToList();
        var more = members.Count > shown.Count;
        var parts = new List<DescriptorPartResponse> { new(" (") };
        for (var at = 0; at < shown.Count; at++)
        {
            if (at > 0)
            {
                parts.Add(new DescriptorPartResponse(at == shown.Count - 1 && !more
                    ? $" {DescriptorPhrasings.Conjunction(language)} "
                    : DescriptorPhrasings.Separator));
            }

            var member = shown[at];
            var cases = forms.GetValueOrDefault(member.Id);
            var (_, name) = DescriptorPhrasings.Say(language, Bare, member.Kind, cases, member.Name);
            parts.Add(new DescriptorPartResponse(name)
            {
                Entity = new DescriptorTargetResponse(
                    EnumSpelling.Of(member.Kind),
                    member.Slug,
                    cases?.GetValueOrDefault(GrammaticalCases.Nominative) ?? member.Name,
                    member.Name)
                {
                    Forms = cases is { Count: > 0 } ? new Dictionary<string, string>(cases, StringComparer.Ordinal) : null,
                    Distinguisher = member.Distinguisher,
                },
            });
        }

        if (more)
        {
            parts.Add(new DescriptorPartResponse(DescriptorPhrasings.AndTheOthers(language)));
        }

        parts.Add(new DescriptorPartResponse(")"));
        return parts;
    }

    /// <summary>
    /// The children a line counts where a verse counts them: by sex as the verses count them, the
    /// named ones where more are named than counted, and those the records name without a sex as
    /// children where no count by sex holds them.
    /// </summary>
    private static List<(Kin Kin, int Count)> Stated(ChildrenCount stated, IReadOnlyList<(Kin Kin, int Count)> named, int total)
    {
        int Named(Kin kin) => named.Where(n => n.Kin == kin).Sum(n => n.Count);
        if (Named(Endpoints.Kin.Child) > 0)
        {
            var all = (stated.Sons ?? 0) + (stated.Daughters ?? 0) + (stated.Children ?? 0);
            return all >= total
                ? [(Endpoints.Kin.Son, stated.Sons ?? 0), (Endpoints.Kin.Daughter, stated.Daughters ?? 0), (Endpoints.Kin.Child, stated.Children ?? 0)]
                : [(Endpoints.Kin.Child, total)];
        }

        if (stated.Sons is null && stated.Daughters is null)
        {
            return [(Endpoints.Kin.Child, Math.Max(stated.Children ?? 0, total))];
        }

        return
        [
            (Endpoints.Kin.Son, Math.Max(stated.Sons ?? 0, Named(Endpoints.Kin.Son))),
            (Endpoints.Kin.Daughter, Math.Max(stated.Daughters ?? 0, Named(Endpoints.Kin.Daughter))),
            (Endpoints.Kin.Child, stated.Children ?? 0),
        ];
    }

    /// <summary>The first verse a clause rests on.</summary>
    private static VerseRefResponse VerseOf(Clause clause) =>
        clause.Verses is { Count: > 0 } verses
            ? verses[0]
            : BookReferences.At(clause.Book, clause.Chapter, clause.Verse)!;

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
            Verses = clause.Verses is { } verses ? [.. verses] : Relationships.Verses(clause.Citation),
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

        /// <summary>For a parent, the child's place among their children where a verse states it.</summary>
        public PlaceAmongChildren? Place { get; init; }

        public bool Doubtful => Clauses.All(c => c.Doubtful);
    }

    /// <summary>
    /// What a person's line says beyond the clauses: the sex it says a parent's phrase by, the
    /// children the verses count, the person's place among a parent's children by the parent's id,
    /// and the descendants it names, in order, with whether there are more.
    /// </summary>
    private sealed record LineFacts(
        string? Sex,
        ChildrenCount? Children,
        IReadOnlyDictionary<int, PlaceAmongChildren> Places,
        IReadOnlyList<int> Ancestors,
        bool MoreAncestors)
    {
        /// <summary>The companies the line's teacher taught, with the members it names in each.</summary>
        public Taught Taught { get; init; } = Taught.Nothing;

        /// <summary>How many disciples the records give the line's teacher beyond the companies he taught.</summary>
        public int OtherDisciples { get; init; }
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
        string Source)
    {
        public string? Citation { get; init; }

        /// <summary>The verses a clause the line works out rests on, where no citation of a record holds them.</summary>
        public IReadOnlyList<VerseRefResponse>? Verses { get; init; }

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

    /// <summary>
    /// The sex of the one the line is about, <c>male</c> or <c>female</c>, where the corpus states it:
    /// a client drawing the line from the claims needs it for what agrees with her, <em>похована</em>
    /// rather than <em>похований</em>. Null where it is not stated.
    /// </summary>
    public string? Sex { get; init; }
}

/// <param name="Relation">The relation whose claims the count stands for: <c>father-of</c>.</param>
/// <param name="Text">The whole clause, the phrase and the count, in the language rendered.</param>
internal record DescriptorCountResponse(string Relation, string Text)
{
    /// <summary>
    /// The same words as pieces, each name with the entity it is: <em>батько сімдесяти синів, з них
    /// названі</em> Єтер… Null where the words name nobody.
    /// </summary>
    public IList<DescriptorPartResponse>? Parts { get; init; }

    /// <summary>The verses that state what the words say beyond the claims: the count, a child's place.</summary>
    public IList<VerseRefResponse>? References { get; init; }

    /// <summary>
    /// Where the line has no claim of <see cref="Relation"/> to put the words in place of — Jair's
    /// thirty sons, none of them named — how many of the claims come before them. Null otherwise.
    /// </summary>
    public int? At { get; init; }
}

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
/// <remarks>
/// The clause's reason is not served: it is what a model, an agent or the owner wrote while settling
/// the clause, and none of it was written for a reader.
/// </remarks>
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
