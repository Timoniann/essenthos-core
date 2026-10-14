using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the shipped files say once the names read on the wrong record are read again: Hezekiah's Joah
/// is a man of his own and David's has one father, Shuppim is not Huppim nor Muppim, a people is struck
/// and not killed, Cozbi was slain by Phinehas, and the Salt Sea is the Dead Sea.
/// </summary>
public sealed class NamesReadAgainTests
{
    private static readonly Lazy<IReadOnlyDictionary<string, DescriptorRecord>> Records = new(() =>
        DescriptorFiles.Read(TestResources.Shipped(Path.Combine("Essenthos", "descriptors"))).Records
            .ToDictionary(record => record.Entity, StringComparer.Ordinal));

    private static IReadOnlyList<DescriptorClaimRecord> Claims(string entity) =>
        Records.Value[entity].Claims ?? [];

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Folded = new(() =>
        DuplicateRecordLoader.Read().Merges.ToDictionary(m => m.Folds, m => m.Keeps, StringComparer.Ordinal));

    private static string Kept(string slug) => Folded.Value.TryGetValue(slug, out var kept) ? kept : slug;

    /// <summary>
    /// Joah son of Zimmah of 1 Chronicles 6:21, who is Ethan of 6:42, has one father, Zimmah of 1
    /// Chronicles 6, and no son Eden: Eden and the later Zimmah of 2 Chronicles 29:12 are the later
    /// Joah's.
    /// </summary>
    [Fact]
    public void DavidsJoahHasOneFatherAndHezekiahsJoahHasEden()
    {
        var fathers = Records.Value.Values
            .SelectMany(record => (record.Claims ?? []).Select(claim => (Subject: Kept(record.Entity), claim)))
            .Where(x => (x.claim.Relation == "father-of" && Kept(x.claim.Target!) == "joah-2")
                        || (x.claim.Relation == "son-of" && x.Subject == "joah-2"))
            .Select(x => x.claim.Relation == "father-of" ? x.Subject : Kept(x.claim.Target!))
            .Distinct()
            .ToList();

        fathers.Should().Equal("zimmah");
        Records.Value.Values.SelectMany(r => r.Claims ?? [])
            .Should().NotContain(c => c.Target == "joah-2" && c.Reference == "2CH 29:12");
        Claims("joah-2").Should().NotContain(c => c.Target == "eden" || c.Target == "zimmah-the-later-gershonite");
        Claims("joah-the-later-gershonite").Should().Contain(c => c.Relation == "son-of" && c.Target == "zimmah-the-later-gershonite")
            .And.Contain(c => c.Relation == "father-of" && c.Target == "eden");
        Claims("eden").Should().ContainSingle(c => c.Relation == "son-of").Which.Target.Should().Be("joah-the-later-gershonite");
        Claims("zimmah").Single(c => c.Target == "joah-2").Reference.Should().Be("1CH 6:20-21");

        var splits = DuplicateRecordLoader.Read().Splits!;
        splits.Should().ContainSingle(s => s.From == "joah-2").Which.Should()
            .Match<DuplicateRecordSplit>(s => s.To == "joah-the-later-gershonite" && s.Verses.SequenceEqual(new[] { "2CH 29:12" }));
        splits.Should().Contain(s => s.From == "zimmah" && s.To == "zimmah-the-later-gershonite" && s.Verses.Contains("2CH 29:12"));
    }

    /// <summary>
    /// 1 Chronicles 7:12 and 7:15 print Shuppim beside Huppim: Maachah is his sister, not Muppim's, and
    /// the verses the dataset listed under Muppim are his.
    /// </summary>
    [Fact]
    public void ShuppimIsNeitherHuppimNorMuppim()
    {
        Claims("maacah-5").Should().NotContain(c => c.Target == "muppim")
            .And.Contain(c => c.Relation == "sister-of" && c.Target == "shuppim-the-benjamite" && c.DecidedBy != null);
        Claims("shuppim-the-benjamite").Should().Contain(c => c.Relation == "brother-of" && c.Target == "huppim");
        DuplicateRecordLoader.Read().Splits!.Should().ContainSingle(s => s.From == "muppim")
            .Which.Verses.Should().Equal("1CH 7:12", "1CH 7:15");

        var rulings = SenseReadingFiles.AllRulings().SelectMany(file => file.Rulings)
            .Where(r => r.Reference is "1CH 7:12" or "1CH 7:15" && (r.Create?.Slug ?? r.Existing) == "shuppim-the-benjamite")
            .ToList();
        rulings.Select(r => (r.Text, r.Reference)).Should().BeEquivalentTo(new[]
        {
            ("BHSA", "1CH 7:12"), ("BHSA", "1CH 7:15"), ("GRCBRENT", "1CH 7:12"), ("GRCBRENT", "1CH 7:15"),
            ("SWETE", "1CH 7:15"),
        });
        rulings.Should().OnlyContain(r => r.Corrects == "huppim");
    }

    /// <summary>
    /// 2 Samuel 21:1 says some of a people were slain, which is a defeat; Belshazzar was slain by no one
    /// the verse names; Cozbi by Phinehas; Ezer and Elead by the men of Gath, and Zabad by nobody. A
    /// people killing a man is said from the man's side, where Hophni's stays.
    /// </summary>
    [Fact]
    public void APeopleIsStruckAndTheSlainAreSlainByWhomTheVerseSays()
    {
        Claims("gibeonites").Should().ContainSingle(c => c.Relation == "struck-by" && c.Target == "saul")
            .And.NotContain(c => c.Relation == "killed-by");
        Claims("cozbi").Where(c => c.Relation == "killed-by").Select(c => c.Target).Should().Equal("phinehas");
        Claims("belshazzar").Should().NotContain(c => c.Relation == "killed-by");
        Claims("elead").Where(c => c.Relation == "killed-by").Select(c => c.Target).Should().Equal("gittites");
        Claims("ezer-3").Where(c => c.Relation == "killed-by").Select(c => c.Target).Should().Equal("gittites");
        Claims("zabad-2").Should().NotContain(c => c.Relation == "killed-by");
        Claims("amalekites").Should().NotContain(c => c.Relation == "killer-of");
        Claims("cushites").Should().NotContain(c => c.Relation == "killer-of");
        Claims("hophni").Should().Contain(c => c.Relation == "killed-by" && c.Target == "philistines");
    }

    /// <summary>
    /// Judges 1:4 is not said of the Canaanites at all, neither as killed nor as struck by Judah: the
    /// owner took the line away, and no record says anything of the two of them, from either end.
    /// </summary>
    [Fact]
    public void NothingIsSaidOfTheCanaanitesAndJudah()
    {
        static bool Between(string subject, string? target) =>
            (subject is "canaanites" && target is "judah" or "judahites")
            || (subject is "judah" or "judahites" && target is "canaanites");

        Records.Value.Values
            .SelectMany(record => (record.Claims ?? []).Select(claim => (record.Entity, claim)))
            .Where(x => Between(x.Entity, x.claim.Target))
            .Select(x => $"{x.Entity} {x.claim.Relation} {x.claim.Target} ({x.claim.Reference})")
            .Should().BeEmpty();
        Claims("canaanites").Should().ContainSingle().Which.Relation.Should().Be("from-place");
    }

    /// <summary>
    /// The Salt Sea is the Dead Sea, and its line says so in every language the interface speaks; the
    /// Vale of Siddim is a place of its own, with the name Siddim the sea's record held.
    /// </summary>
    [Fact]
    public void TheSaltSeaIsTheDeadSeaAndTheValeOfSiddimIsItsOwnPlace()
    {
        var line = DistinguisherLoader.ReadOwnLines().Records.Single(r => r.Slug == "saltsea");

        line.English.Should().Contain("Dead Sea");
        line.Ukr.Should().Contain("Мертве море");
        line.Deu.Should().Contain("Tote Meer");
        line.Spa.Should().Contain("mar Muerto");

        var vale = SenseReadingFiles.AllRulings().SelectMany(file => file.Rulings)
            .Single(r => r.Create?.Slug == "vale-of-siddim");
        vale.Create!.Kind.Should().Be("place");
        vale.Reference.Should().Be("GEN 14:3");
        vale.Corrects.Should().Be("saltsea");
        DuplicateRecordLoader.Read().Splits!.Should().ContainSingle(s => s.From == "saltsea")
            .Which.Should().Match<DuplicateRecordSplit>(s => s.To == "vale-of-siddim" && s.Name == "Siddim"
                && s.Verses.SequenceEqual(new[] { "GEN 14:8", "GEN 14:10" }));
    }

    /// <summary>
    /// The rulings that papered over the shifted numbers of Psalm 52 and 60's titles are gone: Doeg and
    /// the valley of salt are named from their own numbers once those are put right.
    /// </summary>
    [Fact]
    public void ThePsalmTitlesNoLongerRuleWhatTheirNumbersNowName()
    {
        var rulings = SenseReadingFiles.AllRulings().SelectMany(file => file.Rulings).ToList();

        rulings.Should().NotContain(r => r.Existing == "doeg" && r.Reference == "PSA 52:2");
        rulings.Should().NotContain(r => r.Existing == "valleyofsalt" && r.Reference == "PSA 60:2");
    }
}
