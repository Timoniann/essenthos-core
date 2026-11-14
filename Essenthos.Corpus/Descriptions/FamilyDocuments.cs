using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Descriptions;

/// <summary>
/// One connected family (<see cref="FamilyGraph"/>) as the document a family tree is drawn from, in a language of words and a
/// language of names. A tree asks for its person's family once and walks it locally to any depth: the largest family the
/// corpus holds, from Adam to Jesus, is some nine hundred people.
///
/// <para>
/// The body, format 1, compact JSON:
/// <code>
/// { "format": 1, "family": 12,
///   "people": [{ "slug": "adam", "name": "Adam", "sex": "male", "line": "…", "quoted": true, "alsoCalled": "…",
///                "roles": ["king-of"], "ties": 4 }],
///   "ties": [[0, 1, "father-of", 1, 4, 1], [2, 0, "wife-of"]] }
/// </code>
/// People in ordinal order of slugs. <c>line</c> is the one line a reader is shown under the name (<see cref="RecordCard.Line"/>),
/// <c>quoted</c> true where it is the source's words; <c>roles</c> every other relation the person's own records state of
/// them (a tree marks a king by <c>king-of</c>), <c>ties</c> how many relationships of every kind they have, which orders whose
/// name is given room first. A tie is the indexes of its two people, its relation read from the first, and the verse it was
/// read from where it names one. Absent fields are null or empty.
/// </para>
/// </summary>
internal static class FamilyDocuments
{
    /// <summary>Raise with any change to what a document holds or how it is written: a stored one is made again only when it moves.</summary>
    public const int Version = 1;

    public const int Format = 1;

    /// <summary>The languages a reader's page says its words in, for which the build makes every family's document.</summary>
    public static readonly string[] ProseLanguages = ["eng", "ukr", "deu", "spa"];

    /// <summary>How many records a card is composed for at once.</summary>
    public const int Batch = 400;

    /// <summary>A person of a family with what the document says of them beyond their card.</summary>
    public sealed record Person(string Slug, string? Sex, IReadOnlyList<string> Roles, int Ties);

    /// <summary>A family tie by its two people's slugs.</summary>
    public sealed record Tie(string From, string Relation, string To, int? Book, int? Chapter, int? Verse);

    private static readonly JsonWriterOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The document's body, and its hash: the first 16 hex of SHA-256 of the body, which is its ETag.</summary>
    public static (string Body, string Hash) Compose(
        int family,
        IReadOnlyList<Person> people,
        IReadOnlyList<Tie> ties,
        IReadOnlyDictionary<string, RecordCard> cards,
        string prose)
    {
        var ordered = people.OrderBy(p => p.Slug, StringComparer.Ordinal).ToList();
        var index = ordered.Select((p, i) => (p.Slug, i)).ToDictionary(p => p.Slug, p => p.i, StringComparer.Ordinal);
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, Compact))
        {
            json.WriteStartObject();
            json.WriteNumber("format", Format);
            json.WriteNumber("family", family);
            json.WriteStartArray("people");
            foreach (var person in ordered)
            {
                var card = cards.GetValueOrDefault(person.Slug);
                json.WriteStartObject();
                json.WriteString("slug", person.Slug);
                json.WriteString("name", card is null ? person.Slug : card.LocalName ?? card.Name);
                if (person.Sex is { } sex)
                {
                    json.WriteString("sex", sex);
                }

                if (card?.Line(prose) is { } line)
                {
                    json.WriteString("line", line);
                    if (card.Quoted(prose))
                    {
                        json.WriteBoolean("quoted", true);
                    }
                }

                if (card?.AlsoCalled is { } also)
                {
                    json.WriteString("alsoCalled", also);
                }

                if (person.Roles.Count > 0)
                {
                    json.WriteStartArray("roles");
                    foreach (var role in person.Roles)
                    {
                        json.WriteStringValue(role);
                    }

                    json.WriteEndArray();
                }

                json.WriteNumber("ties", person.Ties);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("ties");
            foreach (var tie in ties
                         .Where(t => index.ContainsKey(t.From) && index.ContainsKey(t.To))
                         .OrderBy(t => t.From, StringComparer.Ordinal).ThenBy(t => t.Relation, StringComparer.Ordinal)
                         .ThenBy(t => t.To, StringComparer.Ordinal))
            {
                json.WriteStartArray();
                json.WriteNumberValue(index[tie.From]);
                json.WriteNumberValue(index[tie.To]);
                json.WriteStringValue(tie.Relation);
                if (tie is { Book: { } book, Chapter: { } chapter, Verse: { } verse })
                {
                    json.WriteNumberValue(book);
                    json.WriteNumberValue(chapter);
                    json.WriteNumberValue(verse);
                }

                json.WriteEndArray();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        var bytes = buffer.ToArray();
        return (Encoding.UTF8.GetString(bytes), Convert.ToHexStringLower(SHA256.HashData(bytes))[..16]);
    }

    /// <summary>
    /// Every person's other relations and how many relationships they have, as a tree read them from a record's whole list of
    /// ties: every row naming them, either way, once per relation, side and other person, with a family tie touching a deity
    /// left out. Keyed by slug; a person with no relationship is not in it. All records where <paramref name="people"/> is null.
    /// </summary>
    public static async Task<Dictionary<string, (List<string> Roles, int Ties)>> Relations(
        AppDbContext db,
        IReadOnlyCollection<int>? people,
        CancellationToken cancellationToken)
    {
        var family = ChapterFamily.Types;
        var deities = ChapterFamily.Deities;
        var query = db.EntityRelationships.AsQueryable();
        if (people is not null)
        {
            query = query.Where(r => people.Contains(r.FromEntityId) || people.Contains(r.ToEntityId));
        }

        var rows = await query
            .Where(r => r.FromEntityId != r.ToEntityId)
            .Where(r => !family.Contains(r.Type) || !(deities.Contains(r.From!.Slug) || deities.Contains(r.To!.Slug)))
            .Select(r => new { From = r.From!.Slug, r.Type, To = r.To!.Slug })
            .ToListAsync(cancellationToken);
        var types = family.ToHashSet(StringComparer.Ordinal);
        var said = new Dictionary<string, (HashSet<string> Roles, HashSet<(string, bool, string)> Ties)>(StringComparer.Ordinal);

        (HashSet<string> Roles, HashSet<(string, bool, string)> Ties) Of(string slug)
        {
            if (!said.TryGetValue(slug, out var one))
            {
                said[slug] = one = (new HashSet<string>(StringComparer.Ordinal), []);
            }

            return one;
        }

        foreach (var row in rows)
        {
            var from = Of(row.From);
            from.Ties.Add((row.Type, false, row.To));
            if (!types.Contains(row.Type))
            {
                from.Roles.Add(row.Type);
            }

            Of(row.To).Ties.Add((row.Type, true, row.From));
        }

        return said.ToDictionary(
            p => p.Key,
            p => (p.Value.Roles.Order(StringComparer.Ordinal).ToList(), p.Value.Ties.Count),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The document of the family a person is in, composed from the served tables: what a request does for a pair of
    /// languages the build did not make, and what the build composes again to hold a stored one to. Null where the slug is
    /// no person's.
    /// </summary>
    public static async Task<(int Family, string Body, string Hash)?> Read(
        AppDbContext db,
        string slug,
        string? names,
        string prose,
        CancellationToken cancellationToken)
    {
        var family = await db.FamilyMembers.Where(m => m.Slug == slug).Select(m => (int?)m.Component).FirstOrDefaultAsync(cancellationToken);
        if (family is not { } component)
        {
            return null;
        }

        var members = await db.FamilyMembers.Where(m => m.Component == component)
            .Select(m => new { m.EntityId, m.Slug, m.Sex }).ToListAsync(cancellationToken);
        var ties = await db.FamilyTies.Where(t => t.Component == component)
            .Select(t => new Tie(t.From!.Slug, t.Type, t.To!.Slug, t.CanonicalBook, t.CanonicalChapter, t.CanonicalVerse))
            .ToListAsync(cancellationToken);
        var relations = await Relations(db, [.. members.Select(m => m.EntityId)], cancellationToken);
        var cards = new Dictionary<string, RecordCard>(StringComparer.Ordinal);
        foreach (var batch in members.Select(m => m.Slug).Chunk(Batch))
        {
            foreach (var card in await RecordCards.Of(db, batch, names, prose, cancellationToken))
            {
                cards[card.Slug] = card;
            }
        }

        var (body, hash) = Compose(component, People(members.Select(m => (m.Slug, m.Sex)), relations), ties, cards, prose);
        return (component, body, hash);
    }

    public static List<Person> People(
        IEnumerable<(string Slug, string? Sex)> members,
        IReadOnlyDictionary<string, (List<string> Roles, int Ties)> relations) =>
    [
        .. members.Select(m => relations.TryGetValue(m.Slug, out var said)
            ? new Person(m.Slug, m.Sex, said.Roles, said.Ties)
            : new Person(m.Slug, m.Sex, [], 0)),
    ];
}
