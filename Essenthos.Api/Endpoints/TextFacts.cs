using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// What the corpus can count about each text: its size, what its words carry, and which other texts
/// its words are linked to and by what.
///
/// Every one of these is a sweep of a table millions of rows long — the link table alone takes a
/// second and a half to group — and none of it changes while the process runs, because the corpus
/// is read-only here. So it is counted once, for every text together, and each query runs on its own
/// connection so the first reader waits for the slowest of them rather than for their sum.
///
/// A word the edition prints no letters for is counted nowhere, so every share of the words is a
/// share of the words a reader can see.
/// </summary>
internal sealed class TextFacts(IServiceScopeFactory scopes)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyDictionary<int, TextTally>? _tallies;

    public async Task<TextTally> Of(int textId, CancellationToken cancellationToken)
    {
        if (_tallies is null)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                _tallies ??= await Count(cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }

        return _tallies.GetValueOrDefault(textId) ?? TextTally.Empty;
    }

    private async Task<IReadOnlyDictionary<int, TextTally>> Count(CancellationToken cancellationToken)
    {
        var words = Query(db => db.Words
            .GroupBy(w => w.TextId)
            .Select(g => new WordRow(
                g.Key,
                g.Count(w => !w.Elided),
                g.Count(w => !w.Elided && w.Lemma != null),
                g.Count(w => !w.Elided && w.StrongNumber != null),
                g.Count(w => !w.Elided && w.Gloss != null),
                g.Count(w => !w.Elided && w.Morphology != null),
                g.Count(w => !w.Elided && EF.Functions.JsonExists(w.Morphology!, TransliterationFeature))))
            .ToListAsync(cancellationToken));

        // A Strong number can also be carried beside the word rather than on it, which is how a
        // number that has to say who assigned it is stored — the Septuagint's are all of that kind.
        var besideStrong = Query(db => db.WordStrongs
            .Where(s => s.Word!.StrongNumber == null && !s.Word.Elided)
            .GroupBy(s => s.Word!.TextId)
            .Select(g => new CountRow(g.Key, g.Select(s => s.WordId).Distinct().Count()))
            .ToListAsync(cancellationToken));

        var chapters = Query(db => db.Chapters
            .GroupBy(c => c.TextId)
            .Select(g => new CountRow(g.Key, g.Count()))
            .ToListAsync(cancellationToken));

        var verses = Query(db => db.Verses
            .GroupBy(v => v.TextId)
            .Select(g => new CountRow(g.Key, g.Count()))
            .ToListAsync(cancellationToken));

        var named = Query(db => db.WordEntities
            .Where(e => !e.Word!.Elided)
            .GroupBy(e => e.Word!.TextId)
            .Select(g => new CountRow(g.Key, g.Select(e => e.WordId).Distinct().Count()))
            .ToListAsync(cancellationToken));

        var groups = Query(db => db.WordGroups
            .GroupBy(g => new { g.TextId, Supplied = g.Kind == WordGroupKind.Supplied })
            .Select(g => new GroupRow(g.Key.TextId, g.Key.Supplied, g.Count()))
            .ToListAsync(cancellationToken));

        var notes = Query(db => db.VerseNotes
            .GroupBy(n => n.Verse!.TextId)
            .Select(g => new CountRow(g.Key, g.Count()))
            .ToListAsync(cancellationToken));

        var links = Query(db => db.Links
            .GroupBy(l => new { l.FromTextId, l.ToTextId, l.Method })
            .Select(g => new LinkRow(g.Key.FromTextId, g.Key.ToTextId, g.Key.Method, g.Count()))
            .ToListAsync(cancellationToken));

        // Only the links somebody stated carry a source worth naming; the aligner's are this
        // project's own inference and their source strings number in the thousands.
        var stated = Query(db => db.Links
            .Where(l => l.Method != LinkMethod.Aligner)
            .GroupBy(l => new { l.FromTextId, l.ToTextId, l.Source })
            .Select(g => new SourceRow(g.Key.FromTextId, g.Key.ToTextId, g.Key.Source))
            .ToListAsync(cancellationToken));

        await Task.WhenAll(words, besideStrong, chapters, verses, named, groups, notes, links, stated);

        var texts = await Query(db => db.Texts.Select(t => new { t.Id, t.Slug }).ToListAsync(cancellationToken));
        var slugs = texts.ToDictionary(t => t.Id, t => t.Slug);

        var chapterCount = chapters.Result.ToDictionary(row => row.TextId, row => row.Count);
        var verseCount = verses.Result.ToDictionary(row => row.TextId, row => row.Count);
        var namedCount = named.Result.ToDictionary(row => row.TextId, row => row.Count);
        var noteCount = notes.Result.ToDictionary(row => row.TextId, row => row.Count);
        var beside = besideStrong.Result.ToDictionary(row => row.TextId, row => row.Count);
        var wordRows = words.Result.ToDictionary(row => row.TextId);

        var tallies = new Dictionary<int, TextTally>();
        foreach (var (id, _) in slugs)
        {
            var word = wordRows.GetValueOrDefault(id) ?? new WordRow(id, 0, 0, 0, 0, 0, 0);
            var textGroups = groups.Result.Where(row => row.TextId == id).ToList();

            tallies[id] = new TextTally(
                new TextCountsResponse(
                    chapterCount.GetValueOrDefault(id),
                    verseCount.GetValueOrDefault(id),
                    word.Words),
                new TextFeaturesResponse(
                    word.Lemmas,
                    word.Strong + beside.GetValueOrDefault(id),
                    word.Glosses,
                    word.Morphology,
                    word.Transliterated,
                    namedCount.GetValueOrDefault(id),
                    textGroups.Where(row => !row.Supplied).Sum(row => row.Count),
                    textGroups.Where(row => row.Supplied).Sum(row => row.Count),
                    noteCount.GetValueOrDefault(id)),
                Linked(id, slugs, links.Result, stated.Result));
        }

        return tallies;
    }

    /// <summary>
    /// The texts this one is linked to, busiest first. Direction is dropped: a link says that words
    /// of two texts correspond, and a reader asking what this text is joined to means both ends.
    /// </summary>
    private static IList<TextLinkResponse> Linked(
        int id,
        IReadOnlyDictionary<int, string> slugs,
        IEnumerable<LinkRow> links,
        IEnumerable<SourceRow> stated)
    {
        var mine = links
            .Where(row => row.From == id || row.To == id)
            .Select(row => new { Other = row.From == id ? row.To : row.From, row.Method, row.Count })
            .Where(row => row.Other != id && slugs.ContainsKey(row.Other))
            .ToList();

        var credited = stated
            .Where(row => row.From == id || row.To == id)
            .Select(row => new { Other = row.From == id ? row.To : row.From, Dataset = Datasets.Match(row.Source) })
            .Where(row => row.Dataset is not null && row.Dataset.Id != Datasets.Own)
            .ToLookup(row => row.Other, row => row.Dataset!);

        return [.. mine
            .GroupBy(row => row.Other)
            .Select(group => new TextLinkResponse(
                slugs[group.Key],
                group.Sum(row => row.Count),
                [.. group
                    .GroupBy(row => row.Method)
                    .Select(byMethod => new TextLinkMethodResponse(
                        EnumSpelling.Of(byMethod.Key), byMethod.Sum(row => row.Count)))
                    .OrderByDescending(row => row.Links)],
                [.. credited[group.Key]
                    .DistinctBy(dataset => dataset.Id)
                    .Select(dataset => new TextCreditResponse(dataset.Name, dataset.Author))]))
            .OrderByDescending(row => row.Links)
            .ThenBy(row => row.Text, StringComparer.Ordinal)];
    }

    private async Task<T> Query<T>(Func<AppDbContext, Task<T>> query)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>The feature a BHSA word keeps its pronunciation under.</summary>
    private const string TransliterationFeature = "phono";

    private sealed record WordRow(
        int TextId, int Words, int Lemmas, int Strong, int Glosses, int Morphology, int Transliterated);

    private sealed record CountRow(int TextId, int Count);

    private sealed record GroupRow(int TextId, bool Supplied, int Count);

    private sealed record LinkRow(int From, int To, LinkMethod Method, int Count);

    private sealed record SourceRow(int From, int To, string Source);
}

internal sealed record TextTally(
    TextCountsResponse Counts,
    TextFeaturesResponse Features,
    IList<TextLinkResponse> Links)
{
    public static readonly TextTally Empty = new(
        new TextCountsResponse(0, 0, 0),
        new TextFeaturesResponse(0, 0, 0, 0, 0, 0, 0, 0, 0),
        []);
}
