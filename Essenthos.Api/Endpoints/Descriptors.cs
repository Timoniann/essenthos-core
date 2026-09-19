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
    /// The descriptions of a set of entities, in one query pair. A page of search results asks for
    /// forty of these at once, so it is two indexed reads rather than two per entity.
    /// </summary>
    public static async Task<Dictionary<string, EntityDescriptorResponse>> Of(
        AppDbContext db,
        IEnumerable<string> slugs,
        string? language,
        CancellationToken cancellationToken)
    {
        var wanted = slugs.Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var spoken = DescriptorPhrasings.Spoken(language);
        var phrasings = DescriptorPhrasings.For(spoken)!;

        // Everything the target contributes is taken along this join rather than looked up per
        // clause: a subquery in a projection is evaluated once per joined row, which is what took
        // /v1/corpora from a millisecond to 46 seconds.
        var clauses = await db.EntityDescriptors
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
                d.Note))
            .ToListAsync(cancellationToken);

        if (clauses.Count == 0)
        {
            return [];
        }

        var targets = clauses.Select(c => c.TargetEntityId).Distinct().ToList();
        var rows = await db.EntityNameForms
            .Where(f => targets.Contains(f.EntityId) && f.Language == spoken)
            .Select(f => new { f.EntityId, f.GrammaticalCase, f.Form })
            .ToListAsync(cancellationToken);

        var forms = rows
            .GroupBy(f => f.EntityId)
            .ToDictionary(
                target => target.Key,
                target => (IReadOnlyDictionary<string, string>)target.ToDictionary(
                    f => f.GrammaticalCase, f => f.Form, StringComparer.Ordinal));

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
        CancellationToken cancellationToken)
    {
        var found = await Of(db, [slug], language, cancellationToken);
        return found.GetValueOrDefault(slug);
    }

    /// <summary>
    /// The clauses of one entity as a line and as the claims that made it. Both, because they are
    /// two different questions: the line is what a reader is shown, and the claims are what lets
    /// them check it — the verse each clause was read from, and who read it.
    /// </summary>
    private static EntityDescriptorResponse Render(
        string language,
        IReadOnlyList<Clause> clauses,
        IReadOnlyDictionary<string, Phrasing> phrasings,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> forms)
    {
        var parts = new List<DescriptorPartResponse>();
        var claims = new List<DescriptorClaimResponse>();

        foreach (var clause in clauses)
        {
            // A relation with no phrasing cannot be rendered and must not be printed as itself.
            // The loader refuses these, so reaching one here means the vocabulary and the phrasings
            // have drifted apart — which the test over them exists to catch first.
            if (!phrasings.TryGetValue(clause.Relation, out var phrasing))
            {
                continue;
            }

            var doubtful = clause.Confidence is { } confidence && confidence < Plain;
            var cases = forms.GetValueOrDefault(clause.TargetEntityId);
            var target = Target(clause, cases);

            if (parts.Count > 0)
            {
                parts.Add(new DescriptorPartResponse(DescriptorPhrasings.Separator));
            }

            var written = Name(clause, phrasing.Case, cases);
            parts.Add(new DescriptorPartResponse(
                DescriptorPhrasings.AgreeWithWhatFollows(phrasing.Before, written))
            {
                Doubtful = doubtful,
            });
            parts.Add(new DescriptorPartResponse(written)
            {
                Entity = target,
                Doubtful = doubtful,
            });

            if (phrasing.After.Length > 0)
            {
                parts.Add(new DescriptorPartResponse(phrasing.After) { Doubtful = doubtful });
            }

            claims.Add(new DescriptorClaimResponse(
                clause.Ordinal,
                clause.Relation,
                target,
                new VerseRefResponse(
                    clause.Book,
                    BookReferences.Name(clause.Book),
                    BookReferences.Slug(clause.Book),
                    clause.Chapter,
                    clause.Verse),
                EnumSpelling.Of(clause.Method),
                clause.Confidence,
                clause.Source,
                doubtful)
            {
                Note = clause.Note,
            });
        }

        return new EntityDescriptorResponse(language, parts, claims);
    }

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
    /// The target's name in the form the phrase puts it in, and the English name where the pass
    /// produced no such form.
    ///
    /// The fallback is the contract's and it is deliberately not an inflection: a stemmer guessing
    /// the genitive of a Hebrew proper name is wrong often and silently, and a reader cannot tell.
    /// <em>тесть Moses</em> is visibly a gap; <em>тесть Мойсей</em> looks like Ukrainian and is not.
    /// </summary>
    private static string Name(
        Clause clause,
        string grammaticalCase,
        IReadOnlyDictionary<string, string>? cases) =>
        cases?.GetValueOrDefault(grammaticalCase) ?? clause.TargetEnglishName;

    /// <summary>One clause with its target flattened, so the render is a loop and not a join.</summary>
    private sealed record Clause(
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
        string? Note);
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
    IList<DescriptorClaimResponse> Claims);

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
}
