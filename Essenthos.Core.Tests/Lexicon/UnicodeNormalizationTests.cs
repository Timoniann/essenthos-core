using System.Text;
using System.Text.RegularExpressions;
using Essenthos.Core.Utils;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Lexicon;

/// <summary>
/// NFC and NFD as <see cref="UnicodeNormalization"/> computes them. This project, like every project
/// of the solution, is built with InvariantGlobalization, so these tests run where
/// <see cref="string.Normalize()"/> does nothing — which is the reason the helper exists.
/// </summary>
public sealed partial class UnicodeNormalizationTests
{
    private const string AlphaPsiliOxia = "ἄ";

    /// <summary>The premise: under the build's invariant globalization the framework's normalisation is a no-op.</summary>
    [Fact]
    public void TheFrameworksNormalizeDoesNothingUnderInvariantGlobalization()
    {
        AlphaPsiliOxia.Normalize(NormalizationForm.FormC).Should().HaveLength(3);
        AlphaPsiliOxia.IsNormalized(NormalizationForm.FormC).Should().BeTrue();
    }

    [Theory]
    [InlineData("ἄ", "ἄ")]                    // ἄ from alpha, smooth breathing, acute
    [InlineData("ἐλάλησεν", "ἐλάλησεν")]
    [InlineData("ά", "ά")]                                // oxia is tonos
    [InlineData("·", "·")]                                // ano teleia is the middle dot
    [InlineData("ᾠ", "ᾠ")]                     // the breathing is ordered first, then ᾠ composes stepwise
    [InlineData("ậ", "ậ")]                          // ậ: the dot below is ordered before the circumflex
    [InlineData("ậ", "ậ")]
    [InlineData("क़", "क़")]                     // क़ is excluded from composition
    [InlineData("क़", "क़")]
    [InlineData("각", "각")]                     // 각 from its jamo
    [InlineData("שָּׁמַיִם", "שָּׁמַיִם")]
    [InlineData("plain ASCII", "plain ASCII")]
    public void ComposesAsNfc(string text, string composed) =>
        UnicodeNormalization.Composed(text).Should().Be(composed);

    [Theory]
    [InlineData("ἄ", "ἄ")]
    [InlineData("é", "é")]
    [InlineData("й", "й")]                           // й
    [InlineData("각", "각")]
    [InlineData("ᾠ", "ᾠ")]
    [InlineData("plain", "plain")]
    public void DecomposesAsNfd(string text, string decomposed) =>
        UnicodeNormalization.Decomposed(text).Should().Be(decomposed);

    [Fact]
    public void AStringAlreadyInFormIsReturnedItself()
    {
        const string composed = "ἐν αρχῇ";
        UnicodeNormalization.Composed(composed).Should().BeSameAs(composed);
        UnicodeNormalization.IsComposed(composed).Should().BeTrue();
        UnicodeNormalization.IsComposed(AlphaPsiliOxia).Should().BeFalse();
    }

    /// <summary>
    /// No source file calls the framework's normalisation, which this build turns into a no-op;
    /// <see cref="UnicodeNormalization"/> does the work instead. Strong numbers' own
    /// <c>StrongNumbers.Normalize</c> is another method and is not matched.
    /// </summary>
    [Fact]
    public void NoSourceFileCallsTheFrameworksNormalize()
    {
        var root = TestResources.Checkout();
        var callers = Directory.EnumerateDirectories(root, "Essenthos.*")
            .SelectMany(project => Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                           && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && Path.GetFileName(file) != $"{nameof(UnicodeNormalizationTests)}.cs")
            .Where(file => File.ReadLines(file).Any(line =>
                !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && FrameworkNormalize().IsMatch(line)))
            .Select(file => Path.GetRelativePath(root, file));

        callers.Should().BeEmpty("string.Normalize returns its input unchanged under InvariantGlobalization; use UnicodeNormalization");
    }

    [GeneratedRegex(@"\.(Normalize\(\s*(NormalizationForm\b|\))|IsNormalized\()")]
    private static partial Regex FrameworkNormalize();
}

/// <summary>
/// The helper against Postgres's own <c>normalize()</c>, which composes the lemmas in SQL and is the
/// other implementation a stored string meets: for every character with a decomposition or a
/// combining class, alone, decomposed, and with marks out of canonical order.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class UnicodeNormalizationAgainstPostgresTests(WitnessDatabase database)
{
    [Fact]
    public async Task EveryCharacterNormalisesAsPostgresNormalisesIt()
    {
        var samples = Samples().Distinct().ToArray();

        await using var connection = database.NewConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT s, normalize(s, NFC), normalize(s, NFD) FROM unnest(@samples) AS s", connection);
        command.Parameters.AddWithValue("samples", samples);

        var wrong = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var (sample, nfc, nfd) = (reader.GetString(0), reader.GetString(1), reader.GetString(2));
            if (UnicodeNormalization.Composed(sample) != nfc || UnicodeNormalization.Decomposed(sample) != nfd)
            {
                wrong.Add(string.Join(' ', sample.EnumerateRunes().Select(rune => $"U+{rune.Value:X4}")));
            }
        }

        samples.Length.Should().BeGreaterThan(5_000);
        wrong.Should().BeEmpty();
    }

    private static IEnumerable<string> Samples()
    {
        var marks = new List<int>();
        for (var code = 0xA0; code < 0x110000; code++)
        {
            if (code is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            var character = char.ConvertFromUtf32(code);
            var decomposed = UnicodeNormalization.Decomposed(character);
            var mark = UnicodeNormalization.CombiningClass(code);
            if (mark != 0)
            {
                marks.Add(code);
            }

            if (decomposed != character || mark != 0)
            {
                yield return character;
                yield return decomposed;
                yield return "a" + character;
                yield return "ω" + character;
            }
        }

        // Marks in both orders after a base, which canonical ordering sorts and composition may block.
        foreach (var first in marks.Where(code => code < 0x10000).Take(400))
        {
            foreach (var second in new[] { 0x0301, 0x0313, 0x0323, 0x0345, 0x05B8, 0x05BC, 0x05C1, 0x093C })
            {
                yield return "a" + char.ConvertFromUtf32(first) + char.ConvertFromUtf32(second);
                yield return "ש" + char.ConvertFromUtf32(second) + char.ConvertFromUtf32(first);
            }
        }

        for (var syllable = 0xAC00; syllable <= 0xD7A3; syllable += 97)
        {
            yield return char.ConvertFromUtf32(syllable);
            yield return UnicodeNormalization.Decomposed(char.ConvertFromUtf32(syllable));
        }
    }
}
