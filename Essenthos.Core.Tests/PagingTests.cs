using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

public class PagingTests
{
    [Fact]
    public void DefaultsToTheFirstPage()
    {
        Paging.Normalize(null, null).Should().Be((0, Paging.DefaultPageSize));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(120, 120)]
    public void NegativeSkipBecomesTheFirstPage(int skip, int expected)
    {
        Paging.Normalize(skip, null).Skip.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(500, 500)]
    [InlineData(5000, Paging.MaxPageSize)]
    public void TakeIsClampedToTheServableRange(int take, int expected)
    {
        Paging.Normalize(null, take).Take.Should().Be(expected);
    }
}
