using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the published register says, read as the reader will see it.
///
/// <see cref="PlaceRegisterLoader"/> titles a page with the name on the line and annotates the words
/// its number stands at, so a line naming something that is not a place is a page that is not about
/// one. Twenty-three of them were: <em>fire</em>, <em>hell</em>, <em>market(-place)</em>, <em>place
/// that was far off</em>, and a slug made out of a lexicographer's aside about an orthographic
/// variation. None of that is catchable in the loader — it is what the pass in
/// <c>scripts/places.py</c> decided — so it is checked where it was decided, in the file.
///
/// <para>
/// The folder is this repository's own, not <c>Dataset:ResourcesPath</c>. What is under test is
/// what was published here rather than what a deployment happens to be pointed at, and those are
/// two different questions.
/// </para>
/// </summary>
public sealed class PlaceRegisterFileTests
{
    private const string SolutionFile = "Essenthos.Core.sln";

    /// <summary>Strong's aleph and ayin, which stand in front of the first letter without being it.</summary>
    private const string Modifiers = "ʼʻʾʿ'’‘`";

    private static readonly char[] Brackets = ['(', ')', '[', ']', '{', '}'];

    private static readonly Lazy<IReadOnlyList<PlaceRegisterRecord>> Published = new(() =>
        PlaceRegisterFiles.Read(
            Path.Combine(Repository(), "Resources", PlaceRegisterFiles.DefaultFolder)));

    private static IEnumerable<PlaceRegisterRecord> Records =>
        Published.Value.Where(record => record.Kept);

    private static PlaceRegisterRecord Entry(string number) =>
        Published.Value.Single(record => record.Number == number);

    /// <summary>
    /// A name is a name, which is the one thing the lexicon says about this in either language: the
    /// King James prints a place with a capital and a common noun without one. Nine entries reached
    /// the register with a lower-case gloss for a name, and the two the Greek gate did not stop —
    /// <em>fire</em> and <em>palace</em> — show that nothing would have stopped a Hebrew one.
    /// </summary>
    [Fact]
    public void Every_record_is_named_with_a_name()
    {
        var words = Records
            .Where(record => !char.IsUpper(First(record.Name)))
            .Select(record => $"{record.Number} \"{record.Name}\"")
            .ToList();

        words.Should().BeEmpty("a page titled with a common noun is not a place");
    }

    /// <summary>
    /// The lexicographer's aside is not part of the name. <em>Laish (from the margin)</em>,
    /// <em>Ittahkazin (by including directive enclitic)</em> and a whole sentence about an
    /// orthographic variation were page titles, and the last of them was a slug fifty characters
    /// long with a Strong number and two Hebrew words in it.
    /// </summary>
    [Fact]
    public void No_record_is_named_with_a_bracket_or_a_sentence()
    {
        Records.Where(record => record.Name.IndexOfAny(Brackets) >= 0)
            .Select(record => record.Number).Should().BeEmpty();

        Records.Where(record => record.Name.Split(' ').Length > 6)
            .Select(record => record.Number).Should().BeEmpty();
    }

    /// <summary>
    /// The aside comes off and the name under it stands. H4709 is Strong's Mitspah with a note
    /// about the pause form attached, and the note was the page.
    /// </summary>
    [Theory]
    [InlineData("H4709", "Mitspah")]
    [InlineData("H3889", "Laish")]
    [InlineData("H5121", "Naioth")]
    [InlineData("H7831", "Shahazimah")]
    [InlineData("H6278", "Ittahkazin")]
    [InlineData("H1023", "Beth-ham-Merchak")]
    [InlineData("H1181", "Baale-Bamoth")]
    [InlineData("H2051", "Vedan")]
    [InlineData("H2052", "Vaheb")]
    public void An_entry_whose_rendering_carries_prose_is_named_by_the_name_under_it(
        string number,
        string expected)
    {
        var record = Entry(number);
        record.Kept.Should().BeTrue();
        record.Name.Should().Be(expected);
    }

    /// <summary>
    /// A bracket that offers a second spelling is two names and not one word. Strong writes
    /// <em>Ataroth-adar(-addar)</em> for one place, and concatenated it met neither the gazetteer's
    /// <em>Ataroth-addar</em> nor anything else.
    /// </summary>
    [Theory]
    [InlineData("H5853", "Ataroth-adar", "Ataroth-addar")]
    [InlineData("H2334", "Havoth-jair", "Bashan-Havoth-jair")]
    [InlineData("H4076", "Mede", "Medes")]
    public void A_bracketed_alternative_is_a_second_spelling(string number, string one, string other)
    {
        var names = Entry(number).Names ?? [];
        names.Should().Contain(one);
        names.Should().Contain(other);
    }

    /// <summary>
    /// The entries whose headword the lexicon renders as an ordinary word. Every one of them was a
    /// page a reader could open.
    /// </summary>
    [Theory]
    [InlineData("H217")]
    [InlineData("H2038")]
    [InlineData("G58")]
    [InlineData("G1067")]
    [InlineData("G3674")]
    [InlineData("G3735")]
    [InlineData("G4864")]
    [InlineData("G5083")]
    [InlineData("G5117")]
    public void A_common_noun_is_not_a_record(string number)
    {
        var record = Entry(number);
        record.Kept.Should().BeFalse();
        record.Why.Should().Contain("name", "a refusal says why, and this one is about the naming");
    }

    /// <summary>
    /// Every Greek record carries the lexicon's spelling of its name, because the annotation pass
    /// will not take a Greek number the encyclopedia cannot spell — the gate that refuses Ἰωδά the
    /// number of Ἰούδας. Eight records were refused by it for having nothing to compare.
    /// </summary>
    [Fact]
    public void Every_Greek_record_carries_the_spelling_its_number_is_annotated_on()
    {
        Records
            .Where(record => record.Number.StartsWith('G') && string.IsNullOrWhiteSpace(record.Lemma))
            .Select(record => record.Number)
            .Should().BeEmpty();

        // The eight the gate refused for having nothing to compare. Judaea is 173 occurrences of
        // the 199 they are worth between them.
        foreach (var number in
                 new[] { "G494", "G962", "G2802", "G5410", "G2449", "G3194", "G4010", "G4558" })
        {
            var record = Entry(number);
            record.Kept.Should().BeTrue();
            char.IsUpper(First(record.Lemma ?? string.Empty)).Should()
                .BeTrue($"{number} is a name the lexicon writes with a capital");
        }
    }

    private static char First(string name)
    {
        foreach (var character in name)
        {
            if (!Modifiers.Contains(character))
            {
                return character;
            }
        }

        return '\0';
    }

    /// <summary>
    /// The checkout this test is running out of, found by walking up to the solution file rather
    /// than by counting the segments the build configuration puts under <c>bin</c>.
    /// </summary>
    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFile)))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException(
                $"No {SolutionFile} above {AppContext.BaseDirectory}, so the published register "
                + "cannot be found. Run the tests from inside the essenthos-core checkout.");
        }

        return directory.FullName;
    }
}
