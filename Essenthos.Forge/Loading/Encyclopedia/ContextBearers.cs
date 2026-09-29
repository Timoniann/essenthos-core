using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>A canonical address: book, chapter and verse.</summary>
internal readonly record struct Address(int Book, int Chapter, int Verse);

/// <summary>An original word this corpus already settled on an entity, and where it stands.</summary>
internal readonly record struct Attestation(int Entity, Address At);

/// <summary>One name row: a Strong number a record is called by, and the name it is called.</summary>
internal readonly record struct BearerName(int Entity, EntityKind Kind, string Number, string Label);

/// <summary>
/// The records a Strong number may name: those that bear it, and those called by the same name under
/// another number of it.
///
/// <para>
/// One man is often written under two numbers. Saul's son is יְהוֹנָתָן in 1 Samuel 20 and יוֹנָתָן in
/// 1 Samuel 14, Strong heads the two apart, and the encyclopedia holds him under the second. A name the
/// original writes under the first is his as much as anyone's, so the candidates are every record
/// called by a name any bearer of the number is called by.
/// </para>
///
/// <para>
/// What BHSA marks the word — a person, a place, a people — narrows them to the kinds it allows. The
/// Greek marks nothing, so there every person, place and people stays.
/// </para>
/// </summary>
internal sealed class NameBearers
{
    private static readonly Dictionary<string, EntityKind> Marked = new(StringComparer.Ordinal)
    {
        ["pers"] = EntityKind.Person,
        ["topo"] = EntityKind.Place,
        ["gens"] = EntityKind.People,
    };

    private static readonly EntityKind[] Named = [EntityKind.Person, EntityKind.Place, EntityKind.People];

    private readonly Dictionary<string, HashSet<int>> _byNumber = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<int>> _byLabel = new(StringComparer.Ordinal);
    private readonly Dictionary<int, HashSet<string>> _labels = [];
    private readonly Dictionary<int, EntityKind> _kinds = [];

    public NameBearers(IEnumerable<BearerName> names)
    {
        foreach (var name in names)
        {
            _kinds[name.Entity] = name.Kind;
            var label = name.Label.Trim().ToLowerInvariant();
            if (label.Length > 0)
            {
                Add(_byLabel, label, name.Entity);
                if (!_labels.TryGetValue(name.Entity, out var labels))
                {
                    _labels[name.Entity] = labels = new HashSet<string>(StringComparer.Ordinal);
                }

                labels.Add(label);
            }

            if (!string.IsNullOrEmpty(name.Number))
            {
                Add(_byNumber, name.Number, name.Entity);
            }
        }
    }

    /// <summary>Every Strong number some record is called by.</summary>
    public IEnumerable<string> Numbers => _byNumber.Keys;

    /// <param name="marking">BHSA's <c>nameType</c>, comma-separated; null or empty where nothing marks the word.</param>
    public IReadOnlySet<int> Of(string number, string? marking)
    {
        if (!_byNumber.TryGetValue(number, out var bearers))
        {
            return new HashSet<int>();
        }

        var allowed = string.IsNullOrEmpty(marking)
            ? Named
            : marking.Split(',', StringSplitOptions.TrimEntries).Where(Marked.ContainsKey)
                .Select(kind => Marked[kind]).ToArray();

        var candidates = new HashSet<int>();
        foreach (var bearer in bearers)
        {
            candidates.Add(bearer);
            foreach (var label in _labels.GetValueOrDefault(bearer) ?? [])
            {
                candidates.UnionWith(_byLabel[label]);
            }
        }

        candidates.RemoveWhere(entity => !allowed.Contains(_kinds[entity]));
        return candidates;
    }

    private static void Add(Dictionary<string, HashSet<int>> index, string key, int entity)
    {
        if (!index.TryGetValue(key, out var set))
        {
            index[key] = set = [];
        }

        set.Add(entity);
    }
}

/// <summary>
/// Which of a name's bearers a book means, read off the words of the same book this corpus has
/// already settled.
///
/// <para>
/// A name several men bear is a question of which one, and a book almost always answers it the same
/// way throughout: 1 Samuel has one Jonathan, Judges 17–18 one Micah, 2 Chronicles one Asa. Where the
/// book's settled words name exactly one of the candidates, and name him densely enough near the word
/// — twice within <see cref="NearVerses"/> verses of it, or <see cref="InBook"/> times in the book
/// with one of them within <see cref="NearChapters"/> chapter — the word is him. Where the book names
/// two of them anywhere, nothing is said: Jeroboam the son of Nebat is named in the reign of the
/// second Jeroboam, and a book that names both has not told us which one a bare name is.
/// </para>
///
/// <para>
/// The word's own verse is never evidence for it. That is what makes the rule measurable on the
/// words already settled — each is asked with its verse left out, exactly as an unsettled one is.
/// </para>
/// </summary>
internal sealed class ContextBearers
{
    /// <summary>How far, in verses of the same chapter, an attestation counts as near the word.</summary>
    public const int NearVerses = 10;

    /// <summary>How many near attestations settle the word on their own.</summary>
    public const int NearAtLeast = 2;

    /// <summary>How many attestations in the book settle the word when none are that near.</summary>
    public const int InBook = 5;

    /// <summary>How far, in chapters, one of those must stand from the word.</summary>
    public const int NearChapters = 1;

    private readonly Dictionary<int, List<Attestation>> _byBook = [];

    public ContextBearers(IEnumerable<Attestation> attestations)
    {
        foreach (var attestation in attestations)
        {
            if (!_byBook.TryGetValue(attestation.At.Book, out var book))
            {
                _byBook[attestation.At.Book] = book = [];
            }

            book.Add(attestation);
        }
    }

    /// <summary>The one candidate the book means at this address, or null where it does not say.</summary>
    public int? Bearer(Address at, IReadOnlySet<int> candidates)
    {
        if (candidates.Count == 0 || !_byBook.TryGetValue(at.Book, out var book))
        {
            return null;
        }

        int? only = null;
        int inBook = 0, near = 0;
        var nearChapter = false;
        foreach (var attestation in book)
        {
            if (!candidates.Contains(attestation.Entity) || attestation.At == at)
            {
                continue;
            }

            if (only is { } one && one != attestation.Entity)
            {
                return null;
            }

            only = attestation.Entity;
            inBook++;
            var chapters = Math.Abs(attestation.At.Chapter - at.Chapter);
            nearChapter |= chapters <= NearChapters;
            if (chapters == 0 && Math.Abs(attestation.At.Verse - at.Verse) <= NearVerses)
            {
                near++;
            }
        }

        return near >= NearAtLeast || (inBook >= InBook && nearChapter) ? only : null;
    }
}
