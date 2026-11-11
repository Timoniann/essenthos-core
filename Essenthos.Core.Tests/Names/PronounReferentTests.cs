using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Names;

/// <summary>
/// What a word is, as the names projection asks before it lets a pronoun show the person it refers to (the owner's
/// ruling of 2026-10-07 on the Berean's <em>him</em> for Moses): a pronoun in any of the texts' languages, and not a name
/// or a noun.
/// </summary>
public sealed class PronounReferentTests
{
    [Theory]
    [InlineData("eng", "Him", true)]
    [InlineData("ukr", "його", true)]
    [InlineData("rus", "твоей", true)]
    [InlineData("rus", "своей", true)]
    [InlineData("ukr", "своєї", true)]
    [InlineData("ukr", "твоєї", true)]
    [InlineData("rus", "жены", false)]
    [InlineData("deu", "ihm", true)]
    [InlineData("eng", "Moses", false)]
    [InlineData("grc", "αὐτόν", true)]
    [InlineData("grc", "αὐτὸν", true)]
    [InlineData("grc", "ἐγὼ", true)]
    [InlineData("grc", "ἑαυτοῦ", true)]
    [InlineData("grc", "τοῦτο", true)]
    [InlineData("grc", "Μωυσῆς", false)]
    [InlineData("grc", "γυνή", false)]
    public void APronounIsKnownForWhatItIs(string language, string word, bool pronoun) =>
        Pronouns.Is(language, word.ToLowerInvariant()).Should().Be(pronoun);
}
