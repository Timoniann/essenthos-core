using Essenthos.Core.Corpus;
using Essenthos.Core.XmlBible;

namespace Essenthos.Core.Loading.Links;

/// <param name="Module">The SWORD module the numbers are read from, as its configuration names it.</param>
/// <param name="Slug">The corpus text the numbers are laid onto.</param>
/// <param name="Credit">What every link drawn from them begins with, and the dataset declaration claims.</param>
/// <param name="Witnesses">The texts the numbers are matched against, each a run of its own.</param>
internal sealed record SwordNumbering(string Module, string Slug, string Credit, IReadOnlyList<string> Witnesses);

/// <summary>
/// The Strong numbers on the CrossWire modules beyond the Chinese Union Version, matched to the
/// Hebrew and the Greek the way FHL's are (<see cref="UnionStrongLinkLoader"/>): read from the module
/// for the length of one run, laid onto the words the corpus holds, and never stored. The links are
/// what reaches the database, each credited to its numbering.
///
/// <para>
/// **The Segond's numbers go onto a text loaded from elsewhere.** <c>FreSegond1910</c> is Richard
/// Lemay's digitisation of the 1910 Segond, and the corpus loads eBible's; the two are laid together
/// verse by verse and word by word (<see cref="SynodalStrongLayer"/>), and a verse whose words do not
/// agree gets no number rather than a neighbour's. The other three are loaded from the module they
/// are numbered in, so every word finds itself.
/// </para>
///
/// <para>
/// Each numbering is matched against the witnesses its translation was made from, as near as the
/// corpus holds them: the Received Text for the Segond, the Schlachter and the King James revision,
/// which print what it prints, and the critical texts for Darby, whose New Testament follows them.
/// </para>
/// </summary>
internal sealed class CrossWireStrongLinkLoader(
    UnionStrongLinkLoader layer,
    TaggedTextLinkLoader tagged,
    ILogger<CrossWireStrongLinkLoader> logger)
{
    private static readonly string Scrivener = Sources.ScrivenerSlug;
    private static readonly string Stephanus = Sources.StephanusSlug;

    public static readonly IReadOnlyList<SwordNumbering> All =
    [
        new("FreSegond1910", EbibleTextSource.Segond, Sources.SegondStrongCredit,
            [BhsaTextSource.Slug, Scrivener, NestleTextSource.Slug]),
        new("FreJND", SwordTextSource.DarbyFrench, Sources.DarbyFrenchStrongCredit,
            [BhsaTextSource.Slug, NestleTextSource.Slug, WestcottHortTextSource.Slug]),
        new("GerSch", SwordTextSource.Schlachter, Sources.SchlachterStrongCredit,
            [BhsaTextSource.Slug, Scrivener, NestleTextSource.Slug]),
        new("RLT", SwordTextSource.RevisedLiteral, Sources.RevisedLiteralStrongCredit,
            [BhsaTextSource.Slug, Scrivener, Stephanus]),
    ];

    /// <summary>
    /// The numberings an argument list names, by module or by the slug they are laid onto; every one
    /// where it names none. Flags are passed over.
    /// </summary>
    public static IReadOnlyList<SwordNumbering> Named(IEnumerable<string> arguments)
    {
        var named = arguments.Where(argument => !argument.StartsWith("--", StringComparison.Ordinal)).ToList();
        if (named.Count == 0)
        {
            return All;
        }

        return
        [
            .. named.Select(name => All.FirstOrDefault(numbering =>
                    string.Equals(numbering.Module, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(numbering.Slug, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException(
                    $"No CrossWire numbering is called {name}. Name a module ({string.Join(", ", All.Select(n => n.Module))}) "
                    + "or the text it is laid onto, or name none to run them all.")),
        ];
    }

    /// <summary>The module root a numbering is read from, under the corpus sources.</summary>
    public static string Folder(string resources, SwordNumbering numbering) =>
        Path.Combine(
            resources,
            (SwordTextSource.Texts.GetValueOrDefault(numbering.Module)
             ?? SwordTextSource.Numberings[numbering.Module]).Folder);

    public async Task<IReadOnlyList<TaggedTextLinkOutcome>> Load(
        string resources,
        IReadOnlyList<SwordNumbering> numberings,
        CancellationToken cancellationToken = default)
    {
        var outcomes = new List<TaggedTextLinkOutcome>();
        foreach (var numbering in numberings)
        {
            var numbers = await layer.Numbers(
                numbering.Slug, Folder(resources, numbering), numbering.Credit, cancellationToken);
            foreach (var witness in numbering.Witnesses)
            {
                var outcome = await tagged.Load(numbering.Slug, witness, numbers, cancellationToken);
                logger.LogInformation("{Module} against {Witness}: {Outcome}", numbering.Module, witness, outcome);
                outcomes.Add(outcome);
            }
        }

        return outcomes;
    }
}
