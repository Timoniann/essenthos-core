using System.Text;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Endpoints;

/// <summary>One word of a query as typed: in quotation marks or not, and where it stands.</summary>
/// <param name="Phrase">The quoted phrase it stands in, counted from 0; null outside quotation marks.</param>
internal sealed record TypedTerm(string Text, int? Phrase);

/// <summary>
/// What a reader typed into a verse search, as words.
///
/// <para>
/// Words in quotation marks — straight, «angled» or „low“ — are a phrase: each is matched as typed, the
/// form and nothing else, and the verse must hold them side by side. Words outside them are matched
/// through their stem.
/// </para>
/// </summary>
internal static class TypedQuery
{
    /// <summary>A query longer than this is a passage, not a search; the rest is not read.</summary>
    public const int MostTerms = 12;

    private static readonly Dictionary<char, char> Quotes = new()
    {
        ['"'] = '"',
        ['«'] = '»',
        ['„'] = '“',
        ['“'] = '”',
    };

    private static readonly char[] Trimmed = ['.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '«', '»', '„', '“', '”', '—', '–'];

    public static IReadOnlyList<TypedTerm> Parse(string? query)
    {
        var terms = new List<TypedTerm>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return terms;
        }

        var phrase = 0;
        var at = 0;
        while (at < query.Length && terms.Count < MostTerms)
        {
            if (Quotes.TryGetValue(query[at], out var closing))
            {
                var end = query.IndexOf(closing, at + 1);
                if (end > at)
                {
                    var inside = Words(query[(at + 1)..end]);
                    if (inside.Count > 0)
                    {
                        terms.AddRange(inside.Select(word => new TypedTerm(word, phrase)));
                        phrase++;
                    }

                    at = end + 1;
                    continue;
                }
            }

            var next = query.IndexOfAny([.. Quotes.Keys], at + 1);
            var stretch = next < 0 ? query[at..] : query[at..next];
            terms.AddRange(Words(stretch).Select(word => new TypedTerm(word, null)));
            at = next < 0 ? query.Length : next;
        }

        return [.. terms.Take(MostTerms)];
    }

    private static List<string> Words(string stretch) =>
        [.. stretch.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim(Trimmed))
            .Where(word => word.Length > 0)];
}

/// <summary>
/// How one term of a query is looked for: the words it matches, and whether a verse must hold it.
/// </summary>
/// <param name="Forms">The folded forms that match it, by <see cref="Column"/>.</param>
/// <param name="Column">
/// <c>normalised_text</c> for a word, <c>graphical_text</c> for a word printed as several rows, or
/// <c>like</c> for part of a word, whose one pattern is <see cref="Forms"/>' only entry.
/// </param>
/// <param name="Joint">A joint of the language (<see cref="SearchWords.Joint"/>), which a verse need not hold.</param>
/// <param name="Count">How many words of the text it matches, for weighing a near spelling.</param>
internal sealed record ResolvedTerm(
    TypedTerm Typed,
    string Folded,
    TermMatching Matching,
    IReadOnlyList<string> Forms,
    string Column,
    bool Joint,
    string? Stem,
    long Count);

/// <summary>A near spelling the text prints where it does not print what was typed, or prints it rarely.</summary>
internal sealed record NearSpelling(string Term, string Instead);

/// <summary>
/// The verse search over a text whose words are printed apart.
///
/// <para>
/// A word typed outside quotation marks matches every form of the text that shares its stem
/// (<see cref="SearchWords"/>): <em>воскресіння</em> finds <em>воскресінням</em>, <em>працює</em> finds
/// <em>працювати</em>. A joint of the language — <em>і</em>, <em>й</em>, <em>у</em>, <em>в</em>,
/// <em>та</em> — need not be in the verse, and its spellings are one: a reader's <em>і</em> is the
/// text's <em>й</em>. <em>Не</em> is no joint and stays required. Words in quotation marks are matched
/// as typed and must stand together.
/// </para>
///
/// <para>
/// Verses come in order of how much of the query they hold: the words side by side as typed first,
/// then every word including the joints, then every required word, and inside each the closest
/// together first, then canonical order. When no verse holds every required word of a query of three
/// or more, the verses holding most of them are offered instead, each marked as only like the query.
/// Nothing is matched loosely without saying so: a word the text does not print, or prints rarely
/// beside a near spelling it prints often, is answered with that spelling as a suggestion, and the
/// verses are what was typed.
/// </para>
/// </summary>
internal static class VerseSearch
{
    /// <summary>How the verses were matched to the query, per hit.</summary>
    public const string Phrase = "phrase";

    public const string Every = "all";

    public const string Most = "most";

    /// <summary>The fewest required words a query has before verses holding only most of them are offered.</summary>
    private const int FewestForMost = 3;

    /// <summary>How much more often a near spelling must be printed than what was typed to be suggested.</summary>
    private const int NearOutweighs = 2;

    /// <summary>
    /// The most words a typed word may match and still be second-guessed: a word the text prints this
    /// often is what the reader meant, however much more often a near one is printed.
    /// </summary>
    private const int RareEnough = 25;

    /// <summary>The trigram neighbours of a word read before their distances are measured.</summary>
    private const int Neighbours = 30;

    public static async Task<SearchResultsResponse> Search(
        AppDbContext db,
        int textId,
        string textSlug,
        string language,
        IReadOnlyList<TypedTerm> typed,
        SearchScope scope,
        SearchWidening widening,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var counted = await Scalar<bool>(
            connection, "SELECT EXISTS (SELECT 1 FROM text_form WHERE text_id = @text)", cancellationToken, ("text", textId));

        var terms = new List<ResolvedTerm>(typed.Count);
        foreach (var term in typed)
        {
            terms.Add(await Resolve(db, connection, textId, language, term, widening, counted, cancellationToken));
        }

        // A query of nothing but joints asks for them.
        if (terms.All(term => term.Joint))
        {
            terms = [.. terms.Select(term => term with { Joint = false })];
        }

        var required = terms.Count(term => !term.Joint);
        var (total, page) = await Find(connection, textId, terms, scope, required, skip, take, cancellationToken);
        var partial = false;
        if (total == 0 && required >= FewestForMost && terms.All(term => term.Typed.Phrase is null))
        {
            (total, page) = await Find(
                connection, textId, terms, scope, (required + 1) / 2, skip, take, cancellationToken);
            partial = total > 0;
        }

        var near = counted ? await NearSpellings(connection, textId, terms, cancellationToken) : [];
        var verses = await Verses(db, page.Select(hit => hit.Verse).ToList(), cancellationToken);
        var marked = terms.Where(term => term.Forms.Count > 0).ToList();

        return new SearchResultsResponse(
            total,
            skip,
            page.Count,
            [
                .. page.Where(hit => verses.ContainsKey(hit.Verse)).Select(hit =>
                {
                    var verse = verses[hit.Verse];
                    return new SearchHitResponse(
                        textSlug,
                        new BookRefResponse(verse.Ordinal, BookReferences.Name(verse.Ordinal), BookReferences.Slug(verse.Ordinal)),
                        verse.Chapter,
                        verse.Number,
                        Snippets.Build(verse.Words.Select(word => (word.Surface, word.Trailer, Marked(word, marked)))),
                        verse.Label)
                    {
                        Match = partial ? Most : hit.Phrase ? Phrase : Every,
                    };
                }),
            ],
            SearchTerms.Matching([.. terms.Select(term => new SearchTerm(term.Typed.Text, term.Matching, term.Folded))]),
            [
                .. terms.Select(term => new SearchTermResponse(term.Typed.Text, SearchTerms.Name(term.Matching))
                {
                    Optional = term.Joint,
                    Instead = near.FirstOrDefault(n => n.Term == term.Typed.Text)?.Instead,
                }),
            ])
        {
            Partial = partial,
            Suggestion = near.Count > 0 ? Rewritten(typed, near) : null,
        };
    }

    /// <summary>The words one typed term matches in the text, and how.</summary>
    internal static async Task<ResolvedTerm> Resolve(
        AppDbContext db,
        NpgsqlConnection connection,
        int textId,
        string language,
        TypedTerm term,
        SearchWidening widening,
        bool counted,
        CancellationToken cancellationToken)
    {
        var folded = WordFolding.Fold(term.Text, language);
        if (term.Phrase is null && SearchWords.Joint(folded, language) is { } spellings)
        {
            return new ResolvedTerm(term, folded, TermMatching.Folded, spellings, Normalised, true, null, 0);
        }

        if (widening == SearchWidening.PartOfAWord)
        {
            return new ResolvedTerm(
                term, folded, TermMatching.Substring, [LikePatterns.Containing(folded)], Like, false, null, 0);
        }

        if (counted && term.Phrase is null && SearchWords.Stems(language))
        {
            var stemmed = await FormsOfStem(connection, textId, language, folded, cancellationToken);
            var forms = stemmed.Forms.Count > 0 ? stemmed.Forms : [folded];
            var matching = forms.Count > 1 || forms[0] != folded ? TermMatching.Stemmed : TermMatching.Folded;
            return new ResolvedTerm(term, folded, matching, forms, Normalised, false, stemmed.Stem, stemmed.Count);
        }

        var words = db.Words.Where(w => w.TextId == textId);
        var count = counted
            ? await Scalar<long>(
                connection,
                "SELECT coalesce(sum(occurrences), 0)::bigint FROM text_form WHERE text_id = @text AND form = @form",
                cancellationToken, ("text", textId), ("form", folded))
            : await words.LongCountAsync(w => w.NormalisedText == folded, cancellationToken);
        if (count > 0 || term.Phrase is not null)
        {
            return new ResolvedTerm(term, folded, TermMatching.Folded, [folded], Normalised, false, null, count);
        }

        // No row is that word. A row is a morpheme, and Hebrew prints several together: בראשית is
        // two rows and one printed word, so the word as printed is asked before anything wider.
        if (await words.AnyAsync(w => w.GraphicalText == folded, cancellationToken))
        {
            return new ResolvedTerm(term, folded, TermMatching.Printed, [folded], Graphical, false, null, 0);
        }

        // A text nothing here stems keeps the old widening to part of a word, said in the response:
        // a reader of unpointed Hebrew or unaccented Greek types the front of a word.
        if (widening == SearchWidening.AsFarAsNeeded && !SearchWords.Stems(language)
            && folded.Length >= SearchTerms.ShortestPart)
        {
            return new ResolvedTerm(
                term, folded, TermMatching.Substring, [LikePatterns.Containing(folded)], Like, false, null, 0);
        }

        return new ResolvedTerm(term, folded, TermMatching.Folded, [folded], Normalised, false, null, 0);
    }

    private const string Normalised = "normalised_text";

    private const string Graphical = "graphical_text";

    private const string Like = "like";

    /// <summary>The stem of a folded word in the text's language, and every form of the text that has it.</summary>
    private static async Task<(string Stem, IReadOnlyList<string> Forms, long Count)> FormsOfStem(
        NpgsqlConnection connection,
        int textId,
        string language,
        string folded,
        CancellationToken cancellationToken)
    {
        var stem = SearchWords.StemHere(folded, language) ?? await Scalar<string>(
            connection,
            "SELECT coalesce(nullif((ts_lexize(@dictionary::regdictionary, @form))[1], ''), @form)",
            cancellationToken,
            ("dictionary", SearchWords.Dictionary(language)!),
            ("form", folded));

        var forms = new List<string>();
        long count = 0;
        await using var command = new NpgsqlCommand(
            "SELECT form, occurrences FROM text_form WHERE text_id = @text AND stem = @stem", connection);
        command.Parameters.AddWithValue("text", textId);
        command.Parameters.AddWithValue("stem", stem);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            forms.Add(reader.GetString(0));
            count += reader.GetInt32(1);
        }

        return (stem, forms, count);
    }

    internal sealed record Hit(int Verse, bool Phrase);

    /// <summary>
    /// The verses holding at least <paramref name="least"/> of the required terms and every quoted
    /// phrase, best first, and how many there are.
    /// </summary>
    private static async Task<(int Total, List<Hit> Page)> Find(
        NpgsqlConnection connection,
        int textId,
        IReadOnlyList<ResolvedTerm> terms,
        SearchScope scope,
        int least,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand { Connection = connection };
        command.Parameters.AddWithValue("text", textId);
        command.Parameters.AddWithValue("least", least);
        command.Parameters.AddWithValue("count", terms.Count);
        command.Parameters.AddWithValue("skip", skip);
        command.Parameters.AddWithValue("take", take);

        var sql = new StringBuilder();
        string Select(int index, ResolvedTerm term, string? within)
        {
            var parameter = $"f{index}";
            var match = term.Column switch
            {
                Graphical => $"w.graphical_text = ANY(@{parameter})",
                Like => $"w.normalised_text LIKE @{parameter}",
                _ => $"w.normalised_text = ANY(@{parameter})",
            };
            command.Parameters.AddWithValue(parameter, term.Column == Like ? term.Forms[0] : term.Forms.ToArray());
            command.Parameters.AddWithValue($"e{index}", term.Folded);
            var exact = term.Column == Normalised ? $"w.normalised_text = @e{index}" : "false";
            return $"SELECT w.verse_id, w.position, {index} AS term, {exact} AS exact FROM word w WHERE w.text_id = @text AND {match}"
                   + (within is null ? string.Empty : $" AND w.verse_id IN (SELECT verse_id FROM {within})");
        }

        var requiredTerms = terms.Select((term, index) => (term, index)).Where(t => !t.term.Joint).ToList();
        var jointTerms = terms.Select((term, index) => (term, index)).Where(t => t.term.Joint).ToList();

        sql.Append("WITH required AS (");
        sql.Append(string.Join(" UNION ALL ", requiredTerms.Select(t => Select(t.index, t.term, null))));
        sql.Append("), scoped AS (SELECT h.* FROM required h JOIN verse v ON v.id = h.verse_id JOIN book b ON b.id = v.book_id WHERE TRUE");
        if (scope.From is { } from)
        {
            command.Parameters.AddWithValue("from", from);
            sql.Append(" AND b.canonical_ordinal >= @from");
        }

        if (scope.To is { } to)
        {
            command.Parameters.AddWithValue("to", to);
            sql.Append(" AND b.canonical_ordinal <= @to");
        }

        if (scope.Books is { Count: > 0 } books)
        {
            command.Parameters.AddWithValue("books", books.ToArray());
            sql.Append(" AND b.canonical_ordinal = ANY(@books)");
        }

        sql.Append("), held AS (SELECT verse_id, count(DISTINCT term) AS held, max(position) - min(position) AS span FROM scoped GROUP BY verse_id HAVING count(DISTINCT term) >= @least)");
        sql.Append(", hits AS (SELECT verse_id, position, term, exact FROM scoped WHERE verse_id IN (SELECT verse_id FROM held)");
        foreach (var (term, index) in jointTerms)
        {
            sql.Append(" UNION ALL ").Append(Select(index, term, "held"));
        }

        sql.Append(')');

        // Side by side as typed: every term at its own distance from the first.
        sql.Append(", phrases AS (SELECT DISTINCT verse_id FROM hits GROUP BY verse_id, position - term HAVING count(DISTINCT term) = @count)");
        sql.Append(", whole AS (SELECT verse_id, count(DISTINCT term) AS terms, count(DISTINCT term) FILTER (WHERE exact) AS exact FROM hits GROUP BY verse_id)");

        var phrases = terms.Select((term, index) => (term.Typed.Phrase, index))
            .Where(t => t.Phrase is not null)
            .GroupBy(t => t.Phrase!.Value, t => t.index)
            .ToList();
        var quoted = new StringBuilder();
        foreach (var phrase in phrases)
        {
            var parameter = $"q{phrase.Key}";
            command.Parameters.AddWithValue(parameter, phrase.ToArray());
            quoted.Append(
                $" AND h.verse_id IN (SELECT verse_id FROM hits WHERE term = ANY(@{parameter}) GROUP BY verse_id, position - term HAVING count(DISTINCT term) = {phrase.Count()})");
        }

        sql.Append(
            $"""
             , ranked AS (
                 SELECT h.verse_id, h.held, h.span, j.terms, j.exact, p.verse_id IS NOT NULL AND @count > 1 AS phrase
                 FROM held h
                 JOIN whole j ON j.verse_id = h.verse_id
                 LEFT JOIN phrases p ON p.verse_id = h.verse_id
                 WHERE TRUE{quoted}
             )
             SELECT r.verse_id, r.phrase, count(*) OVER () AS total
             FROM ranked r
             JOIN verse v ON v.id = r.verse_id
             JOIN book b ON b.id = v.book_id
             ORDER BY r.phrase DESC, r.held DESC, r.terms DESC, r.exact DESC, r.span,
                      b.canonical_ordinal, v.chapter_number, v.sequence, v.number, v.label
             OFFSET @skip LIMIT @take
             """);

        command.CommandText = sql.ToString();
        var page = new List<Hit>();
        var total = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            page.Add(new Hit(reader.GetInt32(0), reader.GetBoolean(1)));
            total = (int)reader.GetInt64(2);
        }

        if (page.Count == 0 && skip > 0)
        {
            await reader.CloseAsync();
            command.Parameters["skip"].Value = 0;
            command.Parameters["take"].Value = 1;
            await using var again = await command.ExecuteReaderAsync(cancellationToken);
            if (await again.ReadAsync(cancellationToken))
            {
                total = (int)again.GetInt64(2);
            }
        }

        return (total, page);
    }

    private sealed record PageWord(string Surface, string Trailer, string? Normalised, string? Graphical);

    private sealed record PageVerse(int Ordinal, int Chapter, int Number, string Label, List<PageWord> Words);

    private static async Task<Dictionary<int, PageVerse>> Verses(
        AppDbContext db, List<int> ids, CancellationToken cancellationToken)
    {
        var verses = await db.Verses
            .Where(v => ids.Contains(v.Id))
            .Select(v => new
            {
                v.Id,
                Ordinal = v.Book!.CanonicalOrdinal,
                v.ChapterNumber,
                v.Number,
                v.Label,
                Words = v.Words.OrderBy(w => w.Position)
                    .Select(w => new PageWord(w.Surface, w.Trailer, w.NormalisedText, w.GraphicalText))
                    .ToList(),
            })
            .ToListAsync(cancellationToken);
        return verses.ToDictionary(v => v.Id, v => new PageVerse(v.Ordinal, v.ChapterNumber, v.Number, v.Label, v.Words));
    }

    private static bool Marked(PageWord word, IReadOnlyList<ResolvedTerm> terms) =>
        terms.Any(term => term.Column switch
        {
            // Every row of the run carries the run's form, so the whole printed word is marked.
            Graphical => word.Graphical is { } graphical && term.Forms.Contains(graphical),
            Like => word.Normalised is { } normalised && normalised.Contains(term.Folded, StringComparison.Ordinal),
            _ => word.Normalised is { } normalised && term.Forms.Contains(normalised),
        });

    /// <summary>
    /// For each word typed outside quotation marks that the text prints rarely or not at all, a spelling it
    /// prints within one or two letters of it — not merely another ending, and only in its vowels where
    /// what was typed is printed at all — with another stem, printed at least
    /// <see cref="NearOutweighs"/> times as often, or at all where what was typed is not printed.
    /// </summary>
    internal static async Task<List<NearSpelling>> NearSpellings(
        NpgsqlConnection connection,
        int textId,
        IReadOnlyList<ResolvedTerm> terms,
        CancellationToken cancellationToken)
    {
        var near = new List<NearSpelling>();
        foreach (var term in terms.Where(t => !t.Joint && t.Typed.Phrase is null && t.Column == Normalised))
        {
            if (term.Folded.Length < SearchTerms.ShortestPart || near.Any(n => n.Term == term.Typed.Text))
            {
                continue;
            }

            var candidates = new List<(string Form, string Stem)>();
            await using (var command = new NpgsqlCommand(
                             """
                             SELECT form, stem FROM text_form
                             WHERE text_id = @text AND form % @form AND form <> @form
                             ORDER BY similarity(form, @form) DESC, occurrences DESC
                             LIMIT @neighbours
                             """,
                             connection))
            {
                command.Parameters.AddWithValue("text", textId);
                command.Parameters.AddWithValue("form", term.Folded);
                command.Parameters.AddWithValue("neighbours", Neighbours);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    candidates.Add((reader.GetString(0), reader.GetString(1)));
                }
            }

            if (term.Count > RareEnough)
            {
                continue;
            }

            var allowed = term.Folded.Length <= 4 ? 1 : 2;
            var close = candidates
                .Select((c, rank) => (c.Form, c.Stem, Rank: rank, Distance: Apart(c.Form, term.Folded)))
                .Where(c => c.Stem != (term.Stem ?? term.Folded) && !term.Forms.Contains(c.Form)
                            && c.Distance <= allowed && !OtherEnding(c.Form, term.Folded)
                            && (term.Count == 0 || c.Form.Contains(term.Folded, StringComparison.Ordinal)
                                || Consonants(c.Form) == Consonants(term.Folded)))
                .ToList();
            if (close.Count == 0)
            {
                continue;
            }

            var weights = new Dictionary<string, long>();
            await using (var command = new NpgsqlCommand(
                             "SELECT stem, sum(occurrences)::bigint FROM text_form WHERE text_id = @text AND stem = ANY(@stems) GROUP BY stem",
                             connection))
            {
                command.Parameters.AddWithValue("text", textId);
                command.Parameters.AddWithValue("stems", close.Select(c => c.Stem).Distinct().ToArray());
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    weights[reader.GetString(0)] = reader.GetInt64(1);
                }
            }

            var best = close
                .Select(c => (c.Form, c.Distance, c.Rank, Weight: weights.GetValueOrDefault(c.Stem)))
                .OrderBy(c => c.Distance)
                .ThenBy(c => c.Rank)
                .First();
            if (term.Count == 0 || best.Weight >= NearOutweighs * term.Count)
            {
                near.Add(new NearSpelling(term.Typed.Text, best.Form));
            }
        }

        return near;
    }

    /// <summary>The query again with each near spelling in place of the word it stands for, quotation marks kept.</summary>
    internal static string Rewritten(IReadOnlyList<TypedTerm> typed, IReadOnlyList<NearSpelling> near)
    {
        var words = new List<string>();
        int? open = null;
        foreach (var term in typed)
        {
            var word = near.FirstOrDefault(n => n.Term == term.Text)?.Instead is { } instead
                ? AsTyped(instead, term.Text)
                : term.Text;
            if (term.Phrase != open)
            {
                if (open is not null)
                {
                    words[^1] += "\"";
                }

                if (term.Phrase is not null)
                {
                    word = "\"" + word;
                }

                open = term.Phrase;
            }

            words.Add(word);
        }

        if (open is not null)
        {
            words[^1] += "\"";
        }

        return string.Join(' ', words);
    }

    /// <summary>
    /// How far apart a near spelling is: letters to insert, delete or change, except that a word the
    /// near one holds whole — <em>Вишнього</em> in <em>Всевишнього</em> — counts as one letter away.
    /// </summary>
    internal static int Apart(string near, string typed) =>
        near.Contains(typed, StringComparison.Ordinal) && near.Length - typed.Length <= LongestFront
            ? 1
            : Distance(near, typed);

    /// <summary>The most letters a word may grow by in front or behind and still be the near spelling of it.</summary>
    private const int LongestFront = 4;

    /// <summary>
    /// A word without its vowels and soft signs. A word the text prints is second-guessed only where
    /// the near spelling differs from it in vowels alone — <em>воскресіння</em> and <em>воскресення</em> —
    /// and never by a consonant: <em>ворони</em> is not offered <em>борони</em>.
    /// </summary>
    internal static string Consonants(string word) =>
        new([.. word.Where(letter => !Vowels.Contains(letter))]);

    private const string Vowels = "аеєиіїоуюяыэёьʼ'’aeiouyäöüéèêáíóúàâîôûœ";

    /// <summary>
    /// Whether two words differ only in their last two letters — <em>їсть</em> and <em>їсти</em>,
    /// <em>ворони</em> and <em>вороги</em> — which is another ending of the same word, or another word
    /// altogether, and never a misspelling worth offering.
    /// </summary>
    internal static bool OtherEnding(string left, string right)
    {
        var shared = 0;
        while (shared < left.Length && shared < right.Length && left[shared] == right[shared])
        {
            shared++;
        }

        return shared >= Math.Max(left.Length, right.Length) - 2;
    }

    /// <summary>A near spelling with a capital where the reader typed one: <em>Рамесес</em> for <em>Рамсес</em>.</summary>
    private static string AsTyped(string instead, string typed) =>
        typed.Length > 0 && char.IsUpper(typed[0]) && instead.Length > 0
            ? char.ToUpperInvariant(instead[0]) + instead[1..]
            : instead;

    /// <summary>The number of letters to insert, delete or change to make one word the other.</summary>
    internal static int Distance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var change = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + change);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static async Task<T> Scalar<T>(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
