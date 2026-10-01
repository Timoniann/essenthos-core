using Essenthos.Core.ClearBible;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The shape of Clear Bible's Young's Literal Translation set, which differs from the others in both
/// of its files: the target marks punctuation in an <c>isPunc</c> column rather than <c>exclude</c>,
/// and the source is the Westminster Leningrad Codex word by word, whose pronominal suffix is a
/// pronoun told apart from a free one only by its morphology.
/// </summary>
public sealed class ClearBibleYoungTokenTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"essenthos-ylt-{Guid.NewGuid():N}");

    public ClearBibleYoungTokenTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void ATokenTheFileMarksAsPunctuationIsLeftOut()
    {
        var path = Path.Combine(_folder, "target.tsv");
        File.WriteAllLines(path,
        [
            "id\taltId\ttext\ttransType\tisPunc\tisPrimary",
            "01001001001\tIn-1\tIn\tk\tFalse\tTrue",
            "01001001002\t,-1\t,\t\tTrue\tFalse",
        ]);

        ClearBibleAlignment.Tokens(path).Select(token => token.Excluded).Should().Equal(false, true);
    }

    [Fact]
    public void APronounTheMorphologyCallsASuffixIsOne()
    {
        var path = Path.Combine(_folder, "source.tsv");
        File.WriteAllLines(path,
        [
            "id\taltId\ttext\tstrongs\tgloss\tgloss2\tlemma\tpos\tmorph",
            "o010010110151\tזַרְע-1\tזַרְע\tH2233\tseed\t\tזֶרַע\tnoun\tncmsc",
            "o010010110152\tוֹ־-1\tוֹ־\tH9023\tits\t\t\tpron\tpsn3ms",
            "o010010110161\tהוּא-1\tהוּא\tH1931\the\t\tהוּא\tpron\tpp3ms",
        ]);

        ClearBibleAlignment.Tokens(path).Select(token => token.Part)
            .Should().Equal("noun", ClearBibleAlignment.Suffix, "pron");
    }
}

/// <summary>
/// A Young-shaped set loaded over a database: the suffix the Westminster Leningrad Codex writes as a
/// row of its own lands on the one word BHSA writes, and the English comma the file numbers is no
/// word of the translation.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ClearBibleYoungLoadTests : IDisposable
{
    private const string Statement = "Clear Bible Young test set";

    private readonly AppDbContext _db;
    private readonly ClearBibleLinkLoader _loader;
    private readonly string _folder;
    private readonly ClearBibleSet _set;

    public ClearBibleYoungLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _loader = new ClearBibleLinkLoader(_db, NullLogger<ClearBibleLinkLoader>.Instance);

        Corpus.Add(_db, "YOUNG", TextKind.Translation, "eng", (1, 11, ["its", "seed,", "in", "it"]));
        Corpus.Add(_db, "HEBREW", TextKind.CriticalEdition, "hbo", (1, 11, ["זרעו", "בו"]));
        _db.SaveChanges();

        _folder = Path.Combine(Path.GetTempPath(), $"essenthos-ylt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
        File.WriteAllLines(Path.Combine(_folder, "target.tsv"),
        [
            "id\taltId\ttext\ttransType\tisPunc\tisPrimary",
            "01001011001\tits-1\tits\t\tFalse\tFalse",
            "01001011002\tseed-1\tseed\tk\tFalse\tTrue",
            "01001011003\t,-1\t,\t\tTrue\tFalse",
            "01001011004\tin-1\tin\t\tFalse\tFalse",
            "01001011005\tit-1\tit\t\tFalse\tFalse",
        ]);
        File.WriteAllLines(Path.Combine(_folder, "source.tsv"),
        [
            "id\taltId\ttext\tstrongs\tgloss\tgloss2\tlemma\tpos\tmorph",
            "o010010110151\t\tזַרְע\tH2233\tseed\t\tזֶרַע\tnoun\tncmsc",
            "o010010110152\t\tוֹ־\tH9023\tits\t\t\tpron\tpsn3ms",
            "o010010110161\t\tב\tH0871a\tin\t\t\tprep\tPp",
            "o010010110162\t\tוֹ\tH9023\tit\t\t\tpron\tpsn3ms",
        ]);
        File.WriteAllText(Path.Combine(_folder, "alignment.json"),
            """
            {"records": [
              {"source": ["o010010110151"], "target": ["01001011002"]},
              {"source": ["o010010110152"], "target": ["01001011001"]},
              {"source": ["o010010110161", "o010010110162"], "target": ["01001011004", "01001011005"]}
            ]}
            """);
        _set = new ClearBibleSet(
            "YOUNG", "HEBREW", "alignment.json", "target.tsv", "source.tsv", ClearBibleJoin.Letters, Statement);
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task TheSuffixLandsOnTheWordThatCarriesIt()
    {
        var outcome = await _loader.Load(_folder, _set);

        (outcome.Records, outcome.Added, outcome.Unresolved).Should().Be((3, 3, 0));
        var links = await _db.Links.Where(link => link.Source == Statement)
            .Select(link => new
            {
                English = link.Words.Where(word => word.Side == LinkSide.From).Select(word => word.Word!.Surface).Order().ToList(),
                Hebrew = link.Words.Where(word => word.Side == LinkSide.To).Select(word => word.Word!.Surface).ToList(),
            })
            .ToListAsync();

        links.Should().ContainEquivalentOf(new { English = new List<string> { "seed," }, Hebrew = new List<string> { "זרעו" } });
        links.Should().ContainEquivalentOf(new { English = new List<string> { "its" }, Hebrew = new List<string> { "זרעו" } });
        links.Should().ContainEquivalentOf(new { English = new List<string> { "in", "it" }, Hebrew = new List<string> { "בו" } });
    }
}
