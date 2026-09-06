using Essenthos.Core.Database;
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
/// A language the encyclopedia has no phrasings for renders nothing at all. That is deliberate and
/// it is the same judgement <see cref="Annotations"/> makes about a disputed word: showing an
/// English sentence to a Ukrainian reader is exactly the failure this layer replaces, and doing it
/// one language further out is not an improvement.
/// </para>
/// </summary>
internal static class Descriptors
{
    /// <summary>
    /// The confidence at or above which a clause is shown plainly. Below it the clause is kept and
    /// marked rather than dropped, which is DOC-0191's rule: a description that quietly loses its
    /// weakest clause reads as more certain than the corpus is.
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
        var spoken = DescriptorPhrasings.Spoken(language);
        var phrasings = DescriptorPhrasings.For(spoken);
        if (wanted.Count == 0 || phrasings is null)
        {
            return [];
        }

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
        var forms = await db.EntityNameForms
            .Where(f => targets.Contains(f.EntityId) && f.Language == spoken)
            .Select(f => new { f.EntityId, f.GrammaticalCase, f.Form })
            .ToListAsync(cancellationToken);

        var byCase = forms.ToDictionary(f => (f.EntityId, f.GrammaticalCase), f => f.Form);

        return clauses
            .GroupBy(c => c.Slug, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => Render(spoken!, [.. group], phrasings, byCase),
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
        IReadOnlyDictionary<(int, string), string> forms)
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
            var target = new EntityRefResponse(
                EnumSpelling.Of(clause.TargetKind), clause.TargetSlug, clause.TargetName);

            if (parts.Count > 0)
            {
                parts.Add(new DescriptorPartResponse(DescriptorPhrasings.Separator));
            }

            parts.Add(new DescriptorPartResponse(phrasing.Before) { Doubtful = doubtful });
            parts.Add(new DescriptorPartResponse(Name(clause, phrasing.Case, forms))
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
        IReadOnlyDictionary<(int, string), string> forms) =>
        forms.TryGetValue((clause.TargetEntityId, grammaticalCase), out var form)
            ? form
            : clause.TargetName;

    /// <summary>One clause with its target flattened, so the render is a loop and not a join.</summary>
    private sealed record Clause(
        string Slug,
        int Ordinal,
        string Relation,
        int TargetEntityId,
        EntityKind TargetKind,
        string TargetSlug,
        string TargetName,
        int Book,
        int Chapter,
        int Verse,
        LinkMethod Method,
        double? Confidence,
        string Source,
        string? Note);
}

/// <param name="Language">
/// The language actually rendered, which is the one asked for — a language with no phrasings
/// produces no descriptor at all rather than one in some other tongue.
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
    public EntityRefResponse? Entity { get; init; }

    public bool Doubtful { get; init; }
}

/// <param name="Confidence">
/// How sure the method that produced this clause was, and null exactly where a person or a source
/// stated it rather than a process concluding it.
/// </param>
internal record DescriptorClaimResponse(
    int Ordinal,
    string Relation,
    EntityRefResponse Target,
    VerseRefResponse Reference,
    string Method,
    double? Confidence,
    string Source,
    bool Doubtful)
{
    /// <summary>Why, in the words the row carries. Usually the model's own sentence for the clause.</summary>
    public string? Note { get; init; }
}
