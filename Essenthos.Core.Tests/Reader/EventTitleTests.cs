using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>An event's title as the list, its page and the timeline show it.</summary>
public sealed class EventTitleTests
{
    [Theory]
    [InlineData("general anaesthesia", "General anaesthesia")]
    [InlineData("загальна анестезія", "Загальна анестезія")]
    [InlineData("Abram departed Ur*", "Abram departed Ur")]
    [InlineData("«and the start of the year»", "«And the start of the year»")]
    [InlineData("62 Pompeii earthquake", "62 Pompeii earthquake")]
    public void OpensWithACapitalAndLosesTheSourcesAsterisk(string name, string shown) =>
        EventMarks.Unstarred(name).Should().Be(shown);
}
