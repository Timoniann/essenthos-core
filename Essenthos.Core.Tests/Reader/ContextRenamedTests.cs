using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// A person Scripture renames is shown in a chapter's context by the name the chapter calls him by (PRB-1770): Genesis 22
/// says Авраам, though the record is filed under Аврам, and Genesis 12 still says Аврам.
/// </summary>
public sealed class ContextRenamedTests
{
    private static readonly ContextLineBody Abraham =
        new("Аврам", null, null, null, new ContextRenamed("Abraham", "Авраам"));

    [Fact]
    public void A_chapter_that_calls_him_by_the_heading_shows_the_heading()
    {
        Abraham.InChapter("Abram", "Abraham").Should().Be(("Авраам", (string?)null));
    }

    [Fact]
    public void A_chapter_that_calls_him_by_the_name_he_is_filed_under_shows_that_name()
    {
        Abraham.InChapter("Abram", null).Should().Be(("Аврам", (string?)null));
    }

    [Fact]
    public void A_person_not_renamed_keeps_the_name_and_the_chapter_s_name()
    {
        new ContextLineBody("Петро", null, null, null).InChapter("Peter", "Simon").Should().Be(("Петро", "Simon"));
    }
}
