using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Swete;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// The verses settled one by one against the page: a running head and lines of the apparatus read into
/// the text, and the chapter openings the transcription lost.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SweteSettledTests(Swete swete) : IClassFixture<Swete>
{
    private const int Numbers = 4;
    private const int Judges = 7;
    private const int FirstSamuel = 9;

    [Theory]
    [InlineData(FirstSamuel, 8, 2, "καὶ ταῦτα τὰ ὀνόματα τῶν υἱῶν αὐτοῦ· πρωτότοκος Ἰωήλ, καὶ ὄνομα τοῦ δευτέρου Ἀβιά, δικασταὶ ἐν Βηρσάβεε.")]
    [InlineData(FirstSamuel, 11, 11, "καὶ ἐγενήθη μετὰ τὴν οὔριον καὶ ἔθετο Σαοὺλ τὸν λαὸν εἰς τρεῖς ἀρχάς, καὶ εἰσπορεύονται μέσον τῆς "
                                     + "παρεμβολῆς ἐν φυλακῇ τῇ ἑωθινῇ, κοὶ ἔτυπτον τοὺς υἱοὺς Ἄμμων ἕως διεθερμάνθη ἡ ἡμέρα· καὶ "
                                     + "ἐγενήθησαν, οἱ ὑπολελιμμένοι διεσπάρησαν, καὶ οὐχ ὑπελείφθησαν ἐν αὐτοῖς δύο κατὰ τὸ αὐτό.")]
    [InlineData(Numbers, 16, 50, "καὶ ἐπέστρεψεν Ἀαρὼν πρὸς Μωυσῆν ἐπὶ τὴν θύραν τῆς σκηνῆς τοῦ μαρτυρίου, καὶ ἐκόπασεν ἡ θραῦσις.")]
    public void TheVerseReadsAsThePagePrintsIt(int book, int chapter, int verse, string printed) =>
        Swete.Text(swete.Verse(book, chapter, verse)).Should().Be(printed);

    /// <summary>
    /// Judges 18:8 runs over a page, and the transcription read the apparatus of Alexandrinus at the
    /// page's foot into it: the verse is its own words again, ending where Swete's does.
    /// </summary>
    [Fact]
    public void TheApparatusIsNotTheText()
    {
        var text = Swete.Text(swete.Verse(Judges, 18, 8));

        text.Should().StartWith("καὶ ἦλθον οἱ πέντε ἄνδρες πρὸς τοὺς ἀδελφοὺς αὐτῶν εἰς Σαραὰ καὶ Ἐσθαόλ, καὶ εἶπον τοῖς ἀδελφοῖς αὐτῶν Τί ὑμεῖς κάθησθε");
        swete.Verse(Judges, 18, 8).Words.Should().HaveCount(21);
    }

    [Fact]
    public void EverySettlementChangesSomethingAndCitesItsPage() =>
        SweteSettled.All.Should().NotBeEmpty().And.OnlyContain(r =>
            r.Digitised != r.Printed && r.Why.StartsWith("Printed in Swete, vol. ", StringComparison.Ordinal)
                                     && r.Why.Contains("scan leaf", StringComparison.Ordinal));
}
