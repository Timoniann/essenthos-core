using Essenthos.Core.Mapping;
using Essenthos.Core.Names;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Names;

/// <summary>
/// The word rulings the old name loaders read from 65 rulings files, deleted with them in 98d5b22a: each still stands in the
/// names layer's hand files as this checkout has them. A ruling on a reference text is a row of <c>names/&lt;REF&gt;</c>; a
/// ruling on a translation the projection no longer carried is an override of <c>names/overrides/&lt;TEXT&gt;</c>. What the
/// projection carries by itself is held by <see cref="DeletedRulingProjectionTests"/>, which reads the corpus.
/// </summary>
public sealed class DeletedRulingFileTests
{
    private static readonly ProductStore Checkout = new(TestResources.Shipped("."));

    /// <summary>
    /// The 1,371 rulings are all here and each is accounted for: carried, restored as an override, or replaced by a later dated
    /// decision that says which, so a ruling cannot drop out of the list unnoticed.
    /// </summary>
    [Fact]
    public void EveryDeletedRulingIsAccountedFor()
    {
        var rulings = DeletedRuling.All;

        rulings.Should().HaveCount(1371);
        rulings.Select(r => r.File).Distinct().Should().HaveCount(60);
        rulings.Count(r => r.Status == DeletedRuling.Holds).Should().Be(1301);
        rulings.Count(r => r.Status == DeletedRuling.Restored).Should().Be(41);
        rulings.Count(r => r.Status == DeletedRuling.Superseded).Should().Be(29);
        rulings.Should().OnlyContain(r => r.Names.Count > 0 && r.Names.All(name => name.Length > 0) && !r.Names.Contains(r.Refuses));
        rulings.Where(r => r.Status != DeletedRuling.Holds).Should().OnlyContain(r => r.Why != null);
        rulings.Where(r => r.Status == DeletedRuling.Superseded).Should().OnlyContain(r => NameProducts.IsReference(r.Text));
    }

    /// <summary>A ruling on a reference text is a decision of its hand file: the word names what was ruled and not what was ruled out.</summary>
    [Fact]
    public void EveryRulingOnAReferenceTextIsARowOfItsHandFile()
    {
        var failures = new List<string>();
        foreach (var text in DeletedRuling.All.Where(r => NameProducts.IsReference(r.Text)).GroupBy(r => r.Text))
        {
            var settled = Settled(text.Key);
            foreach (var ruling in text)
            {
                var records = settled.TryGetValue(ruling.Own, out var held) ? held : [];
                failures.AddRange(ruling.Names.Where(name => !records.Contains(name)).Select(name => $"{ruling.Text} {ruling.Own} {ruling.Surface}: no row names {name} ({ruling.File})"));
                if (ruling.Refuses is { } refused && records.Contains(refused))
                {
                    failures.Add($"{ruling.Text} {ruling.Own} {ruling.Surface}: a row names {refused}, which {ruling.File} ruled out");
                }
            }
        }

        failures.Should().BeEmpty();
    }

    /// <summary>
    /// A translation's ruling the projection had stopped carrying is an override adding each record it names, and suppressing the
    /// record the link crossed onto the word where there was one.
    /// </summary>
    [Fact]
    public void EveryRestoredRulingOnATranslationIsAnOverride()
    {
        var failures = new List<string>();
        foreach (var text in DeletedRuling.All.Where(r => r.Status == DeletedRuling.Restored).GroupBy(r => r.Text))
        {
            var overrides = Overrides(text.Key);
            foreach (var ruling in text)
            {
                failures.AddRange(ruling.Names.Where(name => !overrides.Contains((ruling.Own, name, OverrideActions.Add)))
                    .Select(name => $"{ruling.Text} {ruling.Own} {ruling.Surface}: no override adds {name}"));
            }
        }

        failures.Should().BeEmpty();
        Overrides("KJV").Should().Contain([("13.7:30.7", "ishvi", OverrideActions.Suppress), ("13.7:30.9", "ishvah", OverrideActions.Suppress)]);
        Overrides("UBIO").Should().Contain([("13.11:44.1", "uzzia", OverrideActions.Suppress), ("13.11:44.2", "ashterathites", OverrideActions.Suppress),
            ("13.27:4.6", "dodai", OverrideActions.Suppress)]);
    }

    /// <summary>
    /// Words whose verse cannot say whom they name stay without a record: Asshur of Genesis 10:11 (the man who went out, or the
    /// land he went into), Canaan as a trader, the two Ariels of Moab, Pul among the far nations, the one word that is Ain and
    /// Rimmon, and Ezekiel 27:19's Javan, which trades from Uzal. Ephratah of Psalm 132:6, which the same reading left open,
    /// was named by a later reading of the verse (2026-11-11).
    /// </summary>
    [Theory]
    [InlineData("1.10:11.7")]
    [InlineData("28.12:8.1")]
    [InlineData("36.1:11.9")]
    [InlineData("10.23:20.16")]
    [InlineData("23.66:19.13")]
    [InlineData("6.15:32.6")]
    [InlineData("26.27:19.3")]
    public void AHebrewWordItsVerseCannotSettleNamesNobody(string own) =>
        Settled("BHSA").Should().NotContainKey(own);

    /// <summary>
    /// The names the Kulish Bible prints in two words where the Hebrew writes one: every word of the name names it, since the
    /// link reaches only one of them, and the land of the king's title after Cushan-rishathaim is not his name.
    /// </summary>
    [Fact]
    public void TheKulishBiblesTwoWordNamesNameEveryWordOfTheName()
    {
        var overrides = Overrides("UKR1871");
        (string Own, string Record)[] named =
        [
            ("1.14:5.19", "ashterothkarnaim"), ("1.14:5.27", "kiriathaim"), ("4.33:46.9", "almondiblathaim"),
            ("7.3:8.14", "cushanrishathaim"), ("7.3:8.15", "cushanrishathaim"), ("7.3:8.23", "cushanrishathaim"),
            ("7.3:10.24", "cushanrishathaim"), ("7.3:10.25", "cushanrishathaim"), ("7.3:10.33", "cushanrishathaim"), ("7.3:10.34", "cushanrishathaim"),
            ("12.4:42.4", "baalshalishah"), ("12.20:12.5", "berodachbaladan"), ("23.39:1.5", "berodachbaladan"),
            ("15.2:57.6", "pocherethhazzebaim"), ("15.2:57.8", "pocherethhazzebaim"),
        ];

        overrides.Should().Contain(named.Select(n => (n.Own, n.Record, OverrideActions.Add)));
        overrides.Should().Contain(("7.3:10.27", "cushanrishathaim", OverrideActions.Suppress));
    }

    /// <summary>The Synodal's Christ, which the aligner once paired with the Greek's Satan beside it, is refused that pairing.</summary>
    [Fact]
    public void TheSynodalsChristIsNeverPairedWithSatan()
    {
        using var reader = Checkout.Open(MappingProducts.Of("RUSV-NESTLE1904", MappingProducts.Rulings), RulingRow.Schema);
        reader.Rows().Where(r => r.Action == RulingRow.Refuse).Select(r => (r.From, r.To))
            .Should().Contain([("46.5:5.15", "46.5:5.5"), ("45.16:20.14", "45.16:20.8")]);
    }

    private static Dictionary<string, HashSet<string>> Settled(string text)
    {
        using var reader = Checkout.Open(NameProducts.Settled(text), SettledName.Schema(text));
        return reader.Rows().GroupBy(r => r.Own.Own).ToDictionary(g => g.Key, g => g.Select(r => r.Record).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static HashSet<(string Own, string Record, string Action)> Overrides(string text)
    {
        using var reader = Checkout.Open(NameProducts.Overrides(text), NameOverride.Schema(text));
        return [.. reader.Rows().Select(r => (r.Own.Own, r.Record, r.Action))];
    }
}
