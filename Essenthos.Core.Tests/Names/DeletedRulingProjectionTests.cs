using System.Collections.Concurrent;
using Essenthos.Core.Conversion;
using Essenthos.Core.Names;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Names;

/// <summary>
/// The word rulings of the deleted rulings files (<see cref="DeletedRulingFileTests"/>) against the corpus: each ruled word
/// still reads as the ruling read it, and the names this checkout's code projects for it over the corpus's products name what
/// was ruled and not what was ruled out. A translation's names are projected afresh, so a change to the projection that
/// drops a ruling fails here before any build.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class DeletedRulingProjectionTests
{
    private static readonly ProductStore Corpus = new(TestResources.Folder("."));

    [Fact]
    public void EveryRuledWordReadsAsRuledAndNamesWhatTheRulingNames()
    {
        var projection = new NamesProjection(Corpus);
        var failures = new ConcurrentBag<string>();
        Parallel.ForEach(DeletedRuling.All.GroupBy(r => r.Text), new ParallelOptions { MaxDegreeOfParallelism = 6 }, text =>
        {
            var words = Words(text.Key).ToDictionary(w => w.Key.Own, w => w.Surface, StringComparer.Ordinal);
            var named = projection.Project(text.Key).Names
                .GroupBy(n => n.Own.Own)
                .ToDictionary(g => g.Key, g => g.Select(n => n.Record).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
            foreach (var ruling in text)
            {
                var at = $"{ruling.Text} {ruling.Own} {ruling.Surface} ({ruling.File}, {ruling.Status})";
                if (!words.TryGetValue(ruling.Own, out var surface) || surface != ruling.Surface)
                {
                    failures.Add($"{at}: the word there reads {surface ?? "nothing"}");
                    continue;
                }

                var records = named.TryGetValue(ruling.Own, out var held) ? held : [];
                foreach (var name in ruling.Names.Where(name => !records.Contains(name)))
                {
                    failures.Add($"{at}: does not name {name}; names {string.Join(", ", records.DefaultIfEmpty("nobody"))}");
                }

                if (ruling.Refuses is { } refused && records.Contains(refused))
                {
                    failures.Add($"{at}: names {refused}, which the ruling ruled out");
                }
            }
        });

        failures.Should().BeEmpty();
    }

    /// <summary>
    /// Pashhur's other name, Magor-missabib (Jeremiah 20:3), is the man where the verse gives it him; the same words where the
    /// text means terror on every side carry nobody.
    /// </summary>
    [Fact]
    public void MagorMissabibNamesPashhurAtJeremiah20_3AndNowhereElse()
    {
        var words = Words("BHSA").Where(w => w.Strong is "H4032" or "H4036").Select(w => w.Key.Own).ToHashSet(StringComparer.Ordinal);
        using var reader = Corpus.Open(NameProducts.Settled("BHSA"), SettledName.Schema("BHSA"));

        words.Should().HaveCountGreaterThanOrEqualTo(10);
        reader.Rows().Where(r => words.Contains(r.Own.Own)).Select(r => (r.Own.Own, r.Record))
            .Should().BeEquivalentTo([("24.20:3.24", "pashhur-2"), ("24.20:3.26", "pashhur-2")]);
    }

    private static List<TextWord> Words(string text)
    {
        using var reader = Corpus.Open(TextWord.ProductOf(text), TextWord.Schema(text));
        return [.. reader.Rows()];
    }
}
