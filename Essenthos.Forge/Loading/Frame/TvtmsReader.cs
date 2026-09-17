using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Frame;

/// <param name="Traditions">
/// The numbering schemes this row is about. One row often serves several — <c>Eng-KJV+Latin+Greek</c>
/// — and the names are the data's own, so a scheme it distinguishes and this corpus does not is
/// still told apart here.
/// </param>
/// <param name="Sources">
/// Where the verse stands in those schemes, or nothing where it stands in a book this corpus does
/// not hold. Such a row still belongs to its passage and still counts towards its scheme.
/// </param>
/// <param name="Tests">
/// What has to be true of an edition for this row to be about it, or null when the cell names
/// something this corpus cannot answer.
/// </param>
internal sealed record TvtmsRow(
    IReadOnlyList<string> Traditions,
    IReadOnlyList<CanonicalReference> Sources,
    IReadOnlyList<CanonicalReference> Standards,
    VersificationConditions? Tests);

/// <summary>
/// Reads Tyndale's TVTMS — Translators Versification Traditions with Methodology for
/// Standardisation — into the rules behind one <see cref="VersificationFrame"/> per tradition.
///
/// The frame is imported rather than invented, and that is the whole point of this task: a frame
/// worked out from the texts themselves passes every test and is wrong exactly where a reader is
/// comparing, because that is where the traditions disagree.
///
/// CC BY 4.0, Tyndale House Cambridge, credited on the text rows that use it.
/// </summary>
internal static class TvtmsReader
{
    private const string ExpandedSectionMarker = "#DataStart(Expanded)";

    private const string SectionEndMarker = "#DataEnd";

    private const char TraditionSeparator = '+';

    private const char CommentMarker = '\'';

    private const string HeadingRow = "SourceType";

    private const int TestsColumn = 8;

    public static VersificationRules Read(string path)
    {
        var blocks = new List<IReadOnlyList<TvtmsRow>>(512);
        var passage = new List<TvtmsRow>(64);
        var inSection = false;

        foreach (var line in File.ReadLines(path))
        {
            if (!inSection)
            {
                inSection = line.StartsWith(ExpandedSectionMarker, StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith(SectionEndMarker, StringComparison.Ordinal))
            {
                break;
            }

            if (TryReadRow(line, out var row))
            {
                passage.Add(row);
                continue;
            }

            // A blank or commented line ends a passage. Each passage is one place the traditions
            // disagree, described once per numbering scheme, and the choice between those
            // descriptions has to be made for the passage as a whole: one rule from one scheme and
            // the next from another produces a numbering no edition has.
            if (passage.Count > 0)
            {
                blocks.Add(passage);
                passage = [];
            }
        }

        if (passage.Count > 0)
        {
            blocks.Add(passage);
        }

        if (blocks.Count == 0)
        {
            throw new InvalidOperationException(
                $"No versification rules were read from {path}. The file should carry a " +
                $"\"{ExpandedSectionMarker}\" section of tab-separated rows; check that it is the TVTMS " +
                "release and not the spreadsheet export.");
        }

        return new VersificationRules(blocks);
    }

    /// <summary>
    /// One row, or false where the line is not one at all.
    ///
    /// A rule about a book this corpus does not hold is still a row: it is part of the passage it
    /// stands in, and the scheme it belongs to is judged on it. Treating it as the end of the
    /// passage would cut Esther in two at the first rule about Tobit, and each half would then be
    /// answered by a different numbering scheme.
    /// </summary>
    private static bool TryReadRow(string line, out TvtmsRow row)
    {
        row = null!;

        var columns = line.Split('\t');
        if (columns.Length < 3)
        {
            return false;
        }

        var sourceTypes = columns[0].Trim();
        if (sourceTypes.Length == 0 || sourceTypes.StartsWith(CommentMarker) || sourceTypes == HeadingRow)
        {
            return false;
        }

        var sources = CanonicalReference.ParseAll(columns[1].Trim());
        var standards = CanonicalReference.ParseAll(columns[2].Trim());

        row = new TvtmsRow(
            [
                .. sourceTypes.Split(
                    TraditionSeparator,
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            ],
            sources,
            standards,
            VersificationTest.ParseAll(columns.Length > TestsColumn ? columns[TestsColumn].Trim() : string.Empty));
        return true;
    }
}

/// <summary>
/// Every rule the versification data states, before one tradition's have been picked out of them.
///
/// They are kept together because choosing between them is the work: the data names twelve Greek
/// numbering schemes, not one, and which of them an edition follows is answered by the edition
/// rather than by its language. Brenton is the scheme called <c>Greek</c> through most of the Old
/// Testament, is <c>GreekUndivided</c> in Genesis 6, and in Exodus 22 follows none of the Greek
/// columns and exactly the Hebrew one — which is a thing the data says, in the tests it writes
/// beside every rule, and the reader used not to ask.
/// </summary>
internal sealed class VersificationRules(IReadOnlyList<IReadOnlyList<TvtmsRow>> blocks)
{
    /// <summary>
    /// The names this file gives the numbering schemes of each tradition, the first being the one
    /// the tradition is named after and the rest its variants. The Russian schemes are absent from
    /// the file, so a Synodal text cannot be placed from this source and has to say so rather than
    /// fall back to its own numbering and look right.
    /// </summary>
    private static readonly Dictionary<Versification, string[]> Schemes = new()
    {
        [Versification.Original] = ["Hebrew"],
        [Versification.English] = ["Eng-KJV", "EngTitleSeparate", "EngTitleMerged"],
        [Versification.Septuagint] =
        [
            "Greek", "Greek2", "Greek3", "GreekUndivided", "GreekUndivided2", "Greek2Undivided",
            "GreekIntegrated", "GreekIntegrated2", "Greek2-NETS", "GrkTitleSeparate", "GrkTitleSeparate2",
            "GrkTitleMerged",
        ],
        [Versification.Vulgate] = ["Latin", "Latin2"],
    };

    public bool Covers(Versification tradition) => Schemes.ContainsKey(tradition);

    /// <summary>
    /// The frame for a tradition, taking the scheme it is named after everywhere. It is what the
    /// data says about the tradition rather than about any edition of it, which is all there is to
    /// go on when the edition's own shape is not to hand.
    /// </summary>
    public VersificationFrame Frame(Versification tradition) =>
        Build(tradition, passage => Named(passage, Schemes[tradition][0]));

    /// <summary>
    /// The frame for one edition, choosing per passage the scheme whose stated tests this edition
    /// answers to.
    ///
    /// Where every scheme of its own tradition fails a test the edition can answer, another
    /// tradition's column is taken if that one passes — the columns are numbering schemes, and the
    /// test rather than the heading is what says which one an edition follows. Brenton's Exodus 21
    /// runs to verse 37, which is the condition the data writes against the Hebrew column and
    /// against no Greek one.
    ///
    /// Where nothing can be decided the tradition's own scheme is used, so a passage the tests say
    /// nothing about is placed exactly as it was before there were any tests to read.
    /// </summary>
    public VersificationFrame Frame(Versification tradition, EditionShape edition)
    {
        var own = Schemes[tradition];
        var others = Schemes.Where(scheme => scheme.Key != tradition).SelectMany(scheme => scheme.Value).ToArray();

        return Build(tradition, passage =>
            Chosen(passage, own, edition, requireEvidence: false) ??
            Chosen(passage, others, edition, requireEvidence: true) ??
            Named(passage, own[0]));
    }

    /// <summary>
    /// The scheme in this passage that the edition answers to: none of its tests that the edition
    /// can answer fails, and where several qualify, the one that said the most about it.
    /// </summary>
    /// <param name="requireEvidence">
    /// Whether the scheme has to have said anything at all. A tradition's own scheme is the default
    /// and needs no argument for it; another tradition's is taken only on the strength of a test
    /// that actually held.
    /// </param>
    private static IEnumerable<TvtmsRow>? Chosen(
        IReadOnlyList<TvtmsRow> passage,
        IReadOnlyList<string> schemes,
        EditionShape edition,
        bool requireEvidence)
    {
        string? best = null;
        var most = -1;

        foreach (var scheme in schemes)
        {
            var answers = Named(passage, scheme)
                .Select(row => row.Tests?.Answer(edition))
                .ToList();
            if (answers.Count == 0 || answers.Any(answer => answer is false))
            {
                continue;
            }

            var held = answers.Count(answer => answer is true);
            if ((requireEvidence && held == 0) || held <= most)
            {
                continue;
            }

            best = scheme;
            most = held;
        }

        return best is null ? null : Named(passage, best);
    }

    private static IEnumerable<TvtmsRow> Named(IReadOnlyList<TvtmsRow> passage, string scheme) =>
        passage.Where(row => row.Traditions.Contains(scheme));

    private VersificationFrame Build(
        Versification tradition,
        Func<IReadOnlyList<TvtmsRow>, IEnumerable<TvtmsRow>> choose)
    {
        var rules = new Dictionary<CanonicalReference, IReadOnlyList<CanonicalReference>>(6_000);

        foreach (var row in blocks.SelectMany(choose).Where(row => row.Sources.Count > 0 && row.Standards.Count > 0))
        {
            // A source verse split into parts appears once per part, each part placed separately.
            // The parts together are what the verse spans, so they accumulate rather than replace.
            foreach (var source in row.Sources)
            {
                if (!rules.TryGetValue(source, out var existing))
                {
                    rules[source] = row.Standards;
                    continue;
                }

                var merged = new List<CanonicalReference>(existing);
                merged.AddRange(row.Standards.Where(standard => !merged.Contains(standard)));
                rules[source] = merged;
            }
        }

        return new VersificationFrame(tradition, rules);
    }
}
