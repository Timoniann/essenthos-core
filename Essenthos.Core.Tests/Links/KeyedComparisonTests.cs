using Essenthos.Core.Comparison;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>Scopes compared apart and gathered read the same as one comparison of them all.</summary>
public sealed class KeyedComparisonTests
{
    private static KeyedRow Row(string scope, string key, string value, long id = 1) => new(scope, "strong", key, value, id);

    private static readonly (string Scope, KeyedRow[] Before, KeyedRow[] After)[] Scopes =
    [
        ("KJV-BHSA", [Row("KJV-BHSA", "a", "x"), Row("KJV-BHSA", "b", "y"), Row("KJV-BHSA", "c", "z")],
            [Row("KJV-BHSA", "a", "x"), Row("KJV-BHSA", "b", "w"), Row("KJV-BHSA", "d", "z")]),
        ("WEB-BHSA", [Row("WEB-BHSA", "a", "x"), Row("WEB-BHSA", "e", "y")],
            [Row("WEB-BHSA", "a", "x", id: 2), Row("WEB-BHSA", "e", "v")]),
    ];

    [Fact]
    public void Merged_comparisons_equal_one_comparison()
    {
        var whole = new KeyedComparison();
        var gathered = new KeyedComparison();
        foreach (var (_, before, after) in Scopes)
        {
            whole.Add("link", before, after);
            var one = new KeyedComparison();
            one.Add("link", before, after);
            gathered.Merge(one);
        }

        gathered.Summary().Should().Be(whole.Summary());
        gathered.Examples().Should().Be(whole.Examples());
        gathered.Counts["link"].Should().Be((5L, 5L));
        whole.Summary().Should().Contain("link\tKJV-BHSA\tstrong\tchanged\t1").And.Contain("link\tWEB-BHSA\tstrong\tid-only\t1");
    }
}
