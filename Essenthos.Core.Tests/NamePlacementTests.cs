using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The names of a translation placed on the original's names: by whom the text's verses say a word
/// names, by its letters in any script, and by the spelling the text gives an entity where nothing
/// else in the verse could be meant.
/// </summary>
public sealed class NamePlacementTests
{
    private const int Abraham = 1;
    private const int Isaac = 2;
    private const int Egypt = 3;

    private static readonly string Avraham = NameLists.Skeleton("אברהם", "hbo");
    private static readonly string Yitshaq = NameLists.Skeleton("יצחק", "hbo");
    private static readonly string Mitsrayim = NameLists.Skeleton("מצרים", "hbo");

    [Theory]
    [InlineData("अब्राहम", "hin")]
    [InlineData("ആബ്രഹാം", "mal")]
    [InlineData("ஆபிரகாம்", "tam")]
    [InlineData("إبراهيم", "arb")]
    [InlineData("아브라함이", "kor")]
    [InlineData("Авраамові", "ukr")]
    [InlineData("Abrahams", "deu")]
    public void Reads_the_consonants_of_a_name_in_every_script(string written, string language)
    {
        NameLetters.Likeness(NameLetters.Of(written, language), Avraham, written)
            .Should().BeGreaterThanOrEqualTo(NameLists.LeastLikeness, $"{written} is Abraham");
    }

    [Theory]
    [InlineData("Фарра", "rus")]
    [InlineData("Ѳарра", "chu")]
    public void Reads_a_Cyrillic_f_as_the_t_of_a_Greek_theta(string written, string language)
    {
        NameLetters.Likeness(NameLetters.Of(written, language), NameLists.Skeleton("תרח", "hbo"), written)
            .Should().BeGreaterThanOrEqualTo(NameLists.LeastLikeness, $"{written} is Terah");
    }

    [Fact]
    public void A_script_with_no_letters_we_read_has_none()
    {
        NameLetters.Of("亚伯拉罕", "zho").Should().BeEmpty();
    }

    [Fact]
    public void Places_the_names_of_a_verse_by_their_letters_and_whom_they_name()
    {
        var verse = Verse((1, 1, 1),
            [Word(10, "abraham", "eng", Abraham), Word(11, "begat", "eng"), Word(12, "isaac", "eng", Isaac)],
            [Target(20, 0, Avraham, Abraham), Target(21, 2, Yitshaq, Isaac)], 3);

        var placed = NamePlacement.Place([verse]);

        placed.Select(placing => (placing.Source, placing.Target)).Should().BeEquivalentTo([(10L, 20L), (12L, 21L)]);
        placed.Should().OnlyContain(placing => placing.Signals.HasFlag(NameSignal.Entity) && placing.Signals.HasFlag(NameSignal.Letters));
    }

    [Fact]
    public void Learns_a_spelling_where_a_verse_names_one_entity_and_uses_it_where_two_stand()
    {
        // A script whose letters nothing reads: the spelling learned in the first verse is the only
        // signal besides whom the verses say the words name.
        var alone = Verse((1, 12, 10), [Word(1, "埃及", "zho", Egypt), Word(2, "去", "zho")], [Target(30, 0, Mitsrayim, Egypt)], 2);
        var two = Verse((1, 13, 1),
            [Word(3, "亚伯兰", "zho", Abraham), Word(4, "从", "zho"), Word(5, "埃及", "zho", Egypt)],
            [Target(31, 0, Avraham, Abraham), Target(32, 2, Mitsrayim, Egypt)], 3);

        var placed = NamePlacement.Place([alone, two]);

        placed.Should().ContainSingle(placing => placing.Source == 5 && placing.Target == 32)
            .Which.Signals.Should().Be(NameSignal.Entity | NameSignal.Spelling);
        placed.Should().ContainSingle(placing => placing.Source == 3 && placing.Target == 31)
            .Which.Signals.Should().Be(NameSignal.Entity);
    }

    [Fact]
    public void Pairs_an_entity_named_twice_in_the_order_it_is_named()
    {
        var verse = Verse((1, 22, 1),
            [Word(1, "abraham", "eng", Abraham), Word(2, "said", "eng"), Word(3, "abraham", "eng", Abraham)],
            [Target(10, 1, Avraham, Abraham), Target(11, 4, Avraham, Abraham)], 6);

        var placed = NamePlacement.Place([verse]);

        placed.Select(placing => (placing.Source, placing.Target)).Should().BeEquivalentTo([(1L, 10L), (3L, 11L)]);
    }

    [Fact]
    public void Puts_one_translation_word_on_both_words_of_a_name_the_original_writes_as_two()
    {
        var bethlehem = 7;
        var verse = Verse((8, 1, 1),
            [Word(1, "bethlehem", "eng", bethlehem)],
            [Target(10, 0, NameLists.Skeleton("בית", "hbo"), bethlehem), Target(11, 1, NameLists.Skeleton("לחם", "hbo"), bethlehem)], 2);

        var placed = NamePlacement.Place([verse]);

        placed.Select(placing => placing.Target).Should().BeEquivalentTo([10L, 11L]);
        placed.Should().ContainSingle(placing => placing.Beside);
    }

    [Fact]
    public void A_word_the_verses_say_names_the_people_is_not_put_on_their_father_by_its_letters()
    {
        const int israelites = 8;
        const int jacob = 9;
        var verse = Verse((2, 1, 7),
            [Word(1, "israelites", "eng", israelites)],
            [Target(10, 0, NameLists.Skeleton("בני", "hbo"), israelites, proper: false), Target(11, 1, NameLists.Skeleton("ישראל", "hbo"), jacob)], 2);

        NamePlacement.Place([verse]).Should().BeEmpty("the בְּנֵי the word renders is no name, and יִשְׂרָאֵל is the man");
    }

    [Fact]
    public void A_short_name_is_not_read_off_a_capitalised_word_the_text_also_writes_in_lower_case()
    {
        var jesus = 10;
        var verse = Verse((44, 9, 20),
            [new NameSourceWord(1, "son", NameLetters.Of("son", "eng"), "son", new HashSet<int>(), Marked: true, Named: false)],
            [Target(10, 0, NameLists.Skeleton("Ἰησοῦν"), jesus)], 1);

        NamePlacement.Place([verse]).Should().BeEmpty("Ἰησοῦν is SN, and so is Son");
    }

    [Fact]
    public void Families_join_an_entity_and_its_other_name()
    {
        var families = new NameFamilies([(5, 9)]);

        families.Of(9).Should().Be(families.Of(5));
        families.Of(6).Should().Be(6);
    }

    private static NameVerse Verse(
        (int, int, int) address, IReadOnlyList<NameSourceWord> source, IReadOnlyList<NameTargetWord> targets, int length) =>
        new(address, source, targets, length);

    private static NameSourceWord Word(long id, string surface, string language, params int[] entities) =>
        new(id, surface, NameLetters.Of(surface, language), NameLetters.Form(surface, language), entities.ToHashSet(),
            Marked: entities.Length > 0, Named: entities.Length > 0);

    private static NameTargetWord Target(long id, int index, string letters, int entity, bool proper = true) =>
        new(id, index, letters, new HashSet<int> { entity }, EntityKind.Person, letters, proper);
}
