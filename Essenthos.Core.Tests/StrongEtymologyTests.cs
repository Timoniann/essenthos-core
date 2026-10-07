using Essenthos.Core.Strong;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

public class StrongEtymologyTests
{
    private static IReadOnlyList<StatedRelation> Read(string number, params object[] pieces) =>
        StrongEtymology.Read(number, [.. pieces.Select(p => p is EtymologyPart part ? part : new EtymologyPart((string)p))]);

    private static EtymologyPart H(int number) => new($"x (H{number})", $"H{number}");

    private static EtymologyPart G(int number) => new($"G{number}", $"G{number}");

    [Fact]
    public void The_same_as_is_one_word_under_two_numbers()
    {
        var relation = Read("H1006", "the same as ", H(1004), ";").Should().ContainSingle().Subject;

        relation.Should().Be(new StatedRelation("H1006", "H1004", StrongRelationKinds.SameAs, false, 1, "the same as x (H1004)"));
    }

    [Fact]
    public void Each_part_of_a_compound_is_a_derivation_and_keeps_its_place()
    {
        var relations = Read("H1007", "from ", H(1004), " and ", H(205), "; house of vanity;");

        relations.Select(r => (r.ToNumber, r.Kind, r.Position)).Should().Equal(
            ("H1004", StrongRelationKinds.From, 1),
            ("H205", StrongRelationKinds.From, 2));
        relations.Should().OnlyContain(r => r.Statement == "from x (H1004) and x (H205)");
    }

    [Fact]
    public void From_the_same_as_is_a_sibling_not_a_parent()
    {
        Read("H3", "from the same as ", H(24), ";").Single().Kind.Should().Be(StrongRelationKinds.SameRootAs);
    }

    [Fact]
    public void A_hedge_is_kept_on_every_reference_it_qualifies()
    {
        var relations = Read("H9", "probably from ", H(6), " and ", H(7), "; but see ", H(8));

        relations.Select(r => (r.ToNumber, r.Kind, r.Hedged)).Should().Equal(
            ("H6", StrongRelationKinds.From, true),
            ("H7", StrongRelationKinds.From, true),
            ("H8", StrongRelationKinds.Compare, false));
    }

    [Fact]
    public void A_primitive_root_derives_from_nothing_and_a_comparison_beside_it_is_only_a_comparison()
    {
        var relations = Read("H55", "a primitive root (compare ", H(56), ");");

        relations.Select(r => (r.ToNumber, r.Kind)).Should().Equal(
            ((string?)null, StrongRelationKinds.Primitive),
            ("H56", StrongRelationKinds.Compare));
    }

    [Fact]
    public void A_bare_primitive_root_is_stated_once()
    {
        Read("H6", "a primitive root;").Should().ContainSingle()
            .Which.Should().Be(new StatedRelation("H6", null, StrongRelationKinds.Primitive, false, 0, "a primitive root"));
    }

    [Theory]
    [InlineData("H2", "(Aramaic) corresponding to ", StrongRelationKinds.CorrespondsTo)]
    [InlineData("H430", "plural of ", StrongRelationKinds.FormOf)]
    [InlineData("H5", "feminine passive participle of ", StrongRelationKinds.FormOf)]
    [InlineData("H5", "feminine from ", StrongRelationKinds.From)]
    [InlineData("H5", "contracted from ", StrongRelationKinds.ContractedFrom)]
    [InlineData("H5", "a variation of ", StrongRelationKinds.Variant)]
    [InlineData("H5", "an orthographical variation for ", StrongRelationKinds.Variant)]
    [InlineData("H5", "for ", StrongRelationKinds.Variant)]
    [InlineData("H5", "only as denominative from ", StrongRelationKinds.From)]
    [InlineData("H5", "in the sense of ", StrongRelationKinds.Unclassified)]
    public void Hebrew_words_before_a_reference_name_its_kind(string number, string lead, string kind)
    {
        Read(number, lead, H(1), ";").Single(r => r.ToNumber == "H1").Kind.Should().Be(kind);
    }

    [Theory]
    [InlineData("of Hebrew origin (", StrongRelationKinds.LoanFrom)]
    [InlineData("of Chaldee origin (compare ", StrongRelationKinds.Compare)]
    [InlineData("third person singular present indicative of ", StrongRelationKinds.FormOf)]
    [InlineData("middle voice from ", StrongRelationKinds.FormOf)]
    [InlineData("from a derivative of ", StrongRelationKinds.From)]
    [InlineData("from the base of ", StrongRelationKinds.From)]
    [InlineData("a presumed derivative of ", StrongRelationKinds.From)]
    [InlineData("akin to ", StrongRelationKinds.From)]
    public void Greek_words_before_a_reference_name_its_kind(string lead, string kind)
    {
        Read("G5", lead, G(1), ");").Single().Kind.Should().Be(kind);
    }

    [Fact]
    public void A_bracketed_aside_between_compound_parts_does_not_break_the_compound()
    {
        var relations = Read("G5", "from ", G(1), " (as a negative particle) and ", G(2), ";");

        relations.Select(r => r.Kind).Should().Equal(StrongRelationKinds.From, StrongRelationKinds.From);
    }

    [Fact]
    public void A_presumed_derivative_is_hedged()
    {
        Read("G5", "a presumed derivative of ", G(1), ";").Single().Hedged.Should().BeTrue();
    }
}

/// <summary>
/// The reading over the whole of both dictionaries, and against the two readings that came before
/// it: the gentilics and the Greek forms are read out of the same prose, and the table must give the
/// answer they give.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class StrongEtymologyCorpusTests(ITestOutputHelper output)
{
    private static readonly Lazy<List<StrongParsedEntry>> Entries = new(() =>
    {
        var parser = new StrongXmlParser();
        return
        [
            .. parser.ParseHebrew(File.ReadAllText(TestResources.Path("Strong", "StrongHebrew.xml"))),
            .. parser.ParseGreek(File.ReadAllText(TestResources.Path("Strong", "StrongGreek.xml"))),
        ];
    });

    [Fact]
    public void Nearly_every_reference_is_read_as_a_kind_and_the_rest_are_said_to_be_unread()
    {
        var relations = StrongRelationReading.Of(Entries.Value);
        foreach (var kind in relations.GroupBy(r => (Language: r.FromNumber[0], r.Kind)).OrderBy(g => g.Key.Language).ThenByDescending(g => g.Count()))
        {
            output.WriteLine($"{kind.Key.Language} {kind.Key.Kind,-24} {kind.Count(),6}  hedged {kind.Count(r => r.Hedged)}");
        }

        foreach (var unread in relations.Where(r => r.Kind == StrongRelationKinds.Unclassified).Take(40))
        {
            output.WriteLine($"  unclassified {unread.FromNumber} -> {unread.ToNumber}: {unread.Statement}");
        }

        var referenced = relations.Where(r => r.ToNumber is not null).ToList();
        referenced.Count.Should().BeGreaterThan(13_000);
        referenced.Count(r => r.Kind == StrongRelationKinds.Unclassified).Should().BeLessThan(referenced.Count / 50);
        relations.Should().OnlyContain(r => StrongRelationKinds.All.Contains(r.Kind));
    }

    [Fact]
    public void Every_greek_form_the_linker_follows_is_a_form_in_the_table()
    {
        var relations = StrongRelationReading.Of(Entries.Value)
            .Where(r => r.Kind == StrongRelationKinds.FormOf)
            .Select(r => (r.FromNumber, r.ToNumber))
            .ToHashSet();

        var heads = Entries.Value
            .Where(e => e.StrongNumber.StartsWith('G'))
            .Select(e => (e.StrongNumber, Head: GreekFormDerivations.Head(e.Derivation)))
            .Where(e => e.Head is not null)
            .ToList();

        heads.Should().NotBeEmpty();
        heads.Where(e => !relations.Contains((e.StrongNumber, e.Head))).Should().BeEmpty();
    }

    [Fact]
    public void The_gentilic_kinds_are_exactly_the_gentilics_strong_states()
    {
        var stated = Entries.Value
            .Select(e => GentilicDerivations.Read(e.StrongNumber, e.Derivation, out _))
            .OfType<StatedGentilic>()
            .ToList();
        var kept = GentilicDerivations.Together(stated, out _);

        var gentilic = StrongRelationReading.Of(Entries.Value)
            .Where(r => r.Kind is StrongRelationKinds.Patronymic or StrongRelationKinds.Patrial or StrongRelationKinds.PatronymicOrPatrial)
            .Select(r => (r.FromNumber, r.ToNumber, r.Kind))
            .ToList();

        gentilic.Should().BeEquivalentTo(kept.Select(g => (g.StrongNumber, (string?)g.OriginNumber, StrongRelationReading.KindOf(g))));
    }

    [Fact]
    public void The_compilers_file_profiles_every_hebrew_entry_and_places_every_first_verse()
    {
        var compiled = CompiledHebrewStrongs.Read(TestResources.Path("BibleData2026", CompiledHebrewStrongs.File));

        compiled.Profiles.Should().HaveCount(8_674);
        compiled.UnreadFirstVerses.Should().Be(0);
        compiled.Profiles.Count(p => p.Language == "arc").Should().Be(683);
        var father = compiled.Profiles.Single(p => p.StrongNumber == "H1");
        (father.PartOfSpeech, father.Gender, father.FirstBook, father.FirstChapter, father.FirstVerse)
            .Should().Be(("noun", "masculine", 1, 2, 24));
        compiled.Roots.Should().HaveCount(6_439 + 783 + 34);
        compiled.Roots.Where(r => r.FromNumber == "H62").Select(r => (r.ToNumber, r.Position))
            .Should().Equal(("H58", 1), ("H1004", 2), ("H4601", 3));
    }

    /// <summary>
    /// The compiler's roots are his reading of Strong's derivation, so nearly all of them are numbers
    /// Strong's own etymology names for the same entry; the rest are where the two readings part.
    /// </summary>
    [Fact]
    public void Nearly_every_root_the_compiler_lists_is_a_number_strongs_etymology_names()
    {
        var compiled = CompiledHebrewStrongs.Read(TestResources.Path("BibleData2026", CompiledHebrewStrongs.File));
        var named = StrongRelationReading.Of(Entries.Value)
            .Where(r => r.ToNumber is not null)
            .Select(r => (r.FromNumber, r.ToNumber))
            .ToHashSet();

        var apart = compiled.Roots.Where(r => !named.Contains((r.FromNumber, r.ToNumber))).ToList();
        output.WriteLine($"{apart.Count} of {compiled.Roots.Count} compiler roots are not named by Strong's etymology");
        foreach (var root in apart.Take(25))
        {
            output.WriteLine($"  {root.FromNumber} -> {root.ToNumber}: {root.Statement}");
        }

        apart.Count.Should().BeLessThan(compiled.Roots.Count / 20);
    }
}

public class RenderingVarietyTests
{
    [Theory]
    [InlineData("thy god", "god")]
    [InlineData("of the gods", "god")]
    [InlineData("and he said unto him", "said")]
    [InlineData("him", "")]
    [InlineData("the lord god", "lord god")]
    public void An_english_phrase_is_counted_as_its_words_of_content(string phrase, string key)
    {
        var (grammar, stem) = Loading.RenderingVariety.For("eng")!.Value;

        Loading.RenderingVariety.Key(phrase, grammar, stem).Should().Be(key);
    }

    [Fact]
    public void A_ukrainian_pronoun_does_not_make_a_rendering_of_its_own()
    {
        var (grammar, stem) = Loading.RenderingVariety.For("ukr")!.Value;

        Loading.RenderingVariety.Key("твій бог", grammar, stem).Should().Be(Loading.RenderingVariety.Key("бог", grammar, stem));
    }
}
