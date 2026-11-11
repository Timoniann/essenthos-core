using Essenthos.Core.ClearBible;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

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
