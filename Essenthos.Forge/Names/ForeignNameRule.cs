using System.Globalization;
using System.Text;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Products;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Names;

/// <summary>
/// The foreign-names rule of the load (<see cref="ForeignNames"/>) asked of the projection: a person's name carried over a
/// link onto a word that is somebody else's name is not carried. It refuses only where all five hold:
/// <list type="number">
/// <item>the base word is the person's own name: its Strong number is one the person's names bear, or it is spelled like one
/// of them;</item>
/// <item>the word it lands on is written as a name (a capital where the sentence does not open);</item>
/// <item>the word is no spelling of the person: not alike any spelling the records hold for them, nor the base word, nor a
/// part of one of their spellings;</item>
/// <item>the text writes words of the same normalised form as the name of another record at least
/// <see cref="ForeignNames.Least"/> times and <see cref="ForeignNames.Outnumbering"/> times as often as for this person or
/// anyone who shares a name, a number or a title with them, and the base's verse names that record;</item>
/// <item>the text does not write the form for this person in <see cref="ForeignNames.Elsewhere"/> verses or more where the
/// other record goes unnamed.</item>
/// </list>
/// A name a person or a source settled, carried over a stated link (no confidence), is left alone.
/// </summary>
internal sealed class ForeignNameRule
{
    /// <summary>How alike two folded spellings must be (trigram similarity) to be taken for one name.</summary>
    public const double Spelled = 0.3;

    private readonly Dictionary<string, List<string>> _spellings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _numbers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _keys = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string, string), bool> _kin = new();

    public ForeignNameRule(ProductStore store)
    {
        void Add<T>(Dictionary<string, T> map, string slug, Action<T> add) where T : new()
        {
            if (!map.TryGetValue(slug, out var set))
            {
                map[slug] = set = new T();
            }

            add(set);
        }

        void Spelling(string slug, string? spelling)
        {
            if (spelling is not null && Fold(spelling) is { Length: > 0 } folded)
            {
                Add(_spellings, slug, (List<string> list) => list.Add(folded));
            }
        }

        void Key(string slug, string? key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                Add(_keys, slug, (HashSet<string> set) => set.Add(key));
            }
        }

        if (store.Exists(NamesProjection.RecordsProduct))
        {
            foreach (var row in Rows(store, NamesProjection.RecordsProduct))
            {
                var slug = row["slug"]!;
                var bare = SlugBase(slug);
                Spelling(slug, row["name"]);
                Spelling(slug, bare);
                Key(slug, row["name"]?.ToLowerInvariant());
                Key(slug, bare);
            }
        }

        if (store.Exists("records/names"))
        {
            foreach (var row in Rows(store, "records/names"))
            {
                if (row["kind"] is "title" or "description")
                {
                    continue;
                }

                var slug = row["slug"]!;
                foreach (var column in new[] { "label", "hebrew", "hebrew_transliterated", "greek", "greek_transliterated" })
                {
                    Spelling(slug, row[column]);
                }

                Key(slug, row["label"]?.ToLowerInvariant());
                foreach (var column in new[] { "hebrew_strong", "greek_strong" })
                {
                    foreach (var number in (row[column] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        Add(_numbers, slug, (HashSet<string> set) => set.Add(number));
                        Key(slug, number);
                    }
                }
            }
        }

        var i18n = Path.Combine(store.ResourcesPath, ProductStore.EssenthosFolder, "records", "i18n");
        if (Directory.Exists(i18n))
        {
            foreach (var language in Directory.GetDirectories(i18n).Select(Path.GetFileName).Order(StringComparer.Ordinal))
            {
                if (store.Exists($"records/i18n/{language}/name-forms"))
                {
                    foreach (var row in Rows(store, $"records/i18n/{language}/name-forms"))
                    {
                        Spelling(row["slug"]!, row["form"]);
                    }
                }
            }
        }

        if (store.Exists("records/facts/titles"))
        {
            foreach (var row in Rows(store, "records/facts/titles"))
            {
                Key(row["title"]!, "title " + row["title"]);
                Key(row["bearer"]!, "title " + row["title"]);
            }
        }
    }

    private static IEnumerable<Dictionary<string, string?>> Rows(ProductStore store, string product)
    {
        using var reader = store.Open(product);
        var names = reader.Columns.Select(c => c.Name).ToList();
        foreach (var row in reader.Rows())
        {
            var map = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var i = 0; i < names.Count; i++)
            {
                map[names[i]] = row[i];
            }

            yield return map;
        }
    }

    private static string SlugBase(string slug)
    {
        var dash = slug.LastIndexOf('-');
        return dash > 0 && slug[(dash + 1)..].All(char.IsAsciiDigit) ? slug[..dash] : slug;
    }

    /// <summary>Whether two records are one name's: the same record, namesakes sharing a name or a number, or a title and its bearer.</summary>
    public bool Kin(string one, string other)
    {
        if (one == other)
        {
            return true;
        }

        var key = string.CompareOrdinal(one, other) < 0 ? (one, other) : (other, one);
        return _kin.GetOrAdd(key, k => _keys.TryGetValue(k.Item1, out var mine) && _keys.TryGetValue(k.Item2, out var theirs) && mine.Overlaps(theirs));
    }

    /// <summary>Whether a base word is the person's own name: by its Strong number or by its spelling.</summary>
    public bool IsOwnName(string record, string surface, string? strong) =>
        _own.GetOrAdd((record, surface, strong), k => OwnName(k.Item1, k.Item2, k.Item3));

    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string, string, string?), bool> _own = new();

    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string, string, string), bool> _spelling = new();

    private bool OwnName(string record, string surface, string? strong) =>
        (strong is not null && _numbers.TryGetValue(record, out var numbers) && numbers.Contains(strong))
        || (_spellings.TryGetValue(record, out var spellings) && Fold(surface) is var folded && spellings.Any(s => Similarity(folded, s) >= Spelled));

    /// <summary>Whether a word is a spelling of the person: alike one of their spellings or the base word, or a part of one of their spellings.</summary>
    public bool IsSpelling(string record, string surface, string baseSurface) =>
        _spelling.GetOrAdd((record, surface, baseSurface), k => Spelling(k.Item1, k.Item2, k.Item3));

    private bool Spelling(string record, string surface, string baseSurface)
    {
        var folded = Fold(surface);
        if (Similarity(folded, Fold(baseSurface)) >= Spelled)
        {
            return true;
        }

        return _spellings.TryGetValue(record, out var spellings)
               && spellings.Any(s => Similarity(folded, s) >= Spelled || (folded.Length >= 3 && s.Contains(folded, StringComparison.Ordinal)));
    }

    /// <summary>
    /// A spelling lower-cased, its marks taken off and everything but letters and digits taken out, as the load's
    /// <c>regexp_replace(DiacriticFolding.Expression(...), '[^[:alnum:]]', '')</c> does.
    /// </summary>
    public static string Fold(string spelling)
    {
        var folded = new StringBuilder(spelling.Length);
        foreach (var letter in spelling)
        {
            if (letter is >= 'Ͱ' and <= 'Ͽ' or >= 'ἀ' and <= '῿')
            {
                folded.Append(GreekLetters.Bare(letter.ToString()));
                continue;
            }

            if (char.GetUnicodeCategory(letter) == UnicodeCategory.NonSpacingMark || !char.IsLetterOrDigit(letter))
            {
                continue;
            }

            var lower = char.ToLowerInvariant(letter);
            var at = Accented.IndexOf(lower);
            folded.Append(at >= 0 ? Plain[at] : lower);
        }

        return folded.ToString();
    }

    private const string Accented = "àáâãäåāăąçćčďèéêëēĕėęěìíîïīĭįòóôõöøōŏőùúûüūŭůűųñńňśšşţťýÿźżžјйёї";
    private const string Plain = "aaaaaaaaacccdeeeeeeeeeiiiiiiiooooooooouuuuuuuuunnnsssttyyzzzjиеі";

    /// <summary>pg_trgm's similarity: the trigrams two strings share over the trigrams either has.</summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        var mine = Trigrams(a);
        var theirs = Trigrams(b);
        var shared = mine.Count(theirs.Contains);
        return (double)shared / (mine.Count + theirs.Count - shared);
    }

    private static HashSet<string> Trigrams(string word)
    {
        var padded = "  " + word + " ";
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 3 <= padded.Length; i++)
        {
            set.Add(padded.Substring(i, 3));
        }

        return set;
    }
}
