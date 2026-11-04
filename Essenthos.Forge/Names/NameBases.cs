using Essenthos.Core.Loading;

namespace Essenthos.Core.Names;

/// <summary>
/// Which reference text each text takes its names from, book by book (DOC-0224 §7.1: <c>base(T)</c> per book range from the
/// relations between texts). Written once into <c>names/bases</c>, which is then kept by hand.
/// </summary>
internal static class NameBases
{
    /// <summary>The relations that say what a text was made from, the surest first; a collation is not one.</summary>
    private static readonly string[] Making = ["translated-from", "revised-from", "same-family-as"];

    /// <summary>
    /// For every book of a text, the reference text it takes names from: the one a relation names (or the reference text a
    /// relation's text is itself related to), else a reference text that names the text as its own family, else one in the
    /// text's own language, else the default of the book's testament (BHSA, Nestle 1904, Brenton's Septuagint). Only a
    /// reference text the text has links with in that book is chosen; where the default has none, the one with the most.
    /// Every other reference text the text has links with in the book follows, for the words the ones before it do not link.
    /// </summary>
    /// <param name="languages">Every text's language.</param>
    /// <param name="linkedBooks">For each (text, reference text) pair of the mapping, the books and how many links of the text's words it has in each.</param>
    public static IReadOnlyList<NameBase> Seed(
        IReadOnlyDictionary<string, string> languages,
        IReadOnlyDictionary<(string Text, string Reference), IReadOnlyDictionary<int, long>> linkedBooks,
        IReadOnlyList<TextRelationEntry> relations)
    {
        var rows = new List<NameBase>();
        foreach (var text in languages.Keys.Where(t => !NameProducts.IsReference(t)).Order(StringComparer.Ordinal))
        {
            var books = linkedBooks.Where(p => p.Key.Text == text).SelectMany(p => p.Value.Keys).Distinct().Order().ToList();
            var levels = books.Select(book =>
            {
                var first = Choose(text, book, languages, linkedBooks, relations);
                var rest = NameProducts.References
                    .Where(r => r != first.Base && linkedBooks.TryGetValue((text, r), out var counts) && counts.GetValueOrDefault(book) > 0)
                    .OrderBy(r => r == Default(book) ? 0 : 1).ThenByDescending(r => linkedBooks[(text, r)][book])
                    .Select(r => (Base: r, Why: $"for the words {first.Base} does not link"));
                return (Book: book, Choices: (List<(string Base, string Why)>)[first, .. rest]);
            }).ToList();
            for (var level = 0; levels.Any(l => l.Choices.Count > level); level++)
            {
                var chosen = levels.Where(l => l.Choices.Count > level).Select(l => (l.Book, Choice: l.Choices[level])).ToList();
                var start = 0;
                for (var i = 1; i <= chosen.Count; i++)
                {
                    if (i < chosen.Count && chosen[i].Choice == chosen[start].Choice && chosen[i].Book == chosen[i - 1].Book + 1)
                    {
                        continue;
                    }

                    var (from, through) = (chosen[start].Book, chosen[i - 1].Book);
                    rows.Add(new NameBase(text, from == through ? $"{from}" : $"{from}-{through}", chosen[start].Choice.Base, chosen[start].Choice.Why));
                    start = i;
                }
            }
        }

        return rows;
    }

    private static (string Base, string Why) Choose(
        string text,
        int book,
        IReadOnlyDictionary<string, string> languages,
        IReadOnlyDictionary<(string Text, string Reference), IReadOnlyDictionary<int, long>> linkedBooks,
        IReadOnlyList<TextRelationEntry> relations)
    {
        bool Linked(string reference) =>
            linkedBooks.TryGetValue((text, reference), out var counts) && counts.GetValueOrDefault(book) > 0;

        var mine = relations
            .Where(r => r.From == text && Covers(r.Scope, book) && Making.Contains(r.Relation))
            .OrderBy(r => Array.IndexOf(Making, r.Relation))
            .ToList();
        foreach (var relation in mine)
        {
            if (NameProducts.IsReference(relation.To) && Linked(relation.To))
            {
                return (relation.To, $"{relation.Relation} {relation.To}");
            }
        }

        foreach (var relation in mine.Where(r => !NameProducts.IsReference(r.To)))
        {
            var kin = relations
                .Where(r => Covers(r.Scope, book) && Making.Contains(r.Relation)
                            && ((r.From == relation.To && NameProducts.IsReference(r.To) && Linked(r.To))
                                || (r.To == relation.To && NameProducts.IsReference(r.From) && Linked(r.From))))
                .Select(r => (Reference: r.From == relation.To ? r.To : r.From, r.Relation))
                .FirstOrDefault();
            if (kin.Reference is not null)
            {
                return (kin.Reference, $"{relation.Relation} {relation.To}, which is {kin.Relation} {kin.Reference} or the other way");
            }
        }

        var family = relations.FirstOrDefault(r => r.To == text && Covers(r.Scope, book) && Making.Contains(r.Relation)
                                                   && NameProducts.IsReference(r.From) && Linked(r.From));
        if (family is not null)
        {
            return (family.From, $"{family.From} is {family.Relation} {text}");
        }

        var language = languages[text];
        var testament = Default(book);
        var sameLanguage = NameProducts.References
            .Where(r => languages.TryGetValue(r, out var theirs) && theirs == language && Linked(r))
            .OrderBy(r => r == testament ? 0 : 1)
            .FirstOrDefault();
        if (sameLanguage is not null)
        {
            return (sameLanguage, $"the reference text in {language}");
        }

        if (Linked(testament))
        {
            return (testament, "the default for the book");
        }

        var most = NameProducts.References
            .Where(Linked)
            .OrderByDescending(r => linkedBooks[(text, r)][book])
            .First();
        return (most, $"{testament} has no links of it in the book; the reference text with the most");
    }

    /// <summary>The reference text a book's testament defaults to.</summary>
    public static string Default(int book) => book <= 39 ? "BHSA" : book <= 66 ? "NESTLE1904" : "GRCBRENT";

    private static bool Covers(string? scope, int book)
    {
        if (scope is null)
        {
            return true;
        }

        foreach (var part in scope.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dash = part.IndexOf('-');
            var (first, last) = dash < 0 ? (int.Parse(part), int.Parse(part)) : (int.Parse(part[..dash]), int.Parse(part[(dash + 1)..]));
            if (book >= first && book <= last)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The bases of each book of a text, from the rows of <c>names/bases</c>, in the order of the file: the first is the book's
    /// base; a later one speaks only for the words no earlier one links.
    /// </summary>
    public static IReadOnlyDictionary<int, List<string>> Of(string text, IEnumerable<NameBase> bases)
    {
        var books = new Dictionary<int, List<string>>();
        foreach (var row in bases.Where(b => b.Text == text))
        {
            var (first, last) = row.Range;
            for (var book = first; book <= last; book++)
            {
                if (!books.TryGetValue(book, out var list))
                {
                    books[book] = list = [];
                }

                if (!list.Contains(row.Base, StringComparer.Ordinal))
                {
                    list.Add(row.Base);
                }
            }
        }

        return books;
    }
}
