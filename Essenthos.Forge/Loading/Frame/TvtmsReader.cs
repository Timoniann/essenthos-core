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

    /// <summary>
    /// A mark the data leaves after a scheme's name on a few rows: the Latin rules for 1 Maccabees 1,
    /// 12 and 13 are written for <c>Latin=</c>, which no other row names, and read as they stand they
    /// belong to no scheme and are never applied.
    /// </summary>
    private const char StraySchemeMark = '=';

    private const string HeadingRow = "SourceType";

    private const int TestsColumn = 8;

    public static VersificationRules Read(string path)
    {
        var blocks = new List<IReadOnlyList<TvtmsRow>>(512);
        var passage = new List<TvtmsRow>(64);
        var corrected = new HashSet<TvtmsCorrection>();
        var additions = new Dictionary<AdditionVerse, CanonicalReference>();
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

            ReadAddition(line, additions);

            if (TryReadRow(line, out var row))
            {
                passage.Add(TvtmsCorrections.Apply(row, out var correction));
                if (correction is not null)
                {
                    corrected.Add(correction);
                }

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

        var supplemented = TvtmsSupplements.Join(blocks);
        return new VersificationRules(blocks, corrected, supplemented, additions);
    }

    /// <summary>
    /// Where a verse of an addition to Esther stands, from the rows that name it by its lettered
    /// chapter. Those rows are the Latin's and place no edition here, but what they say about the
    /// standard numbering is what the Greek editions' lettered verses are placed by.
    /// </summary>
    private static void ReadAddition(string line, Dictionary<AdditionVerse, CanonicalReference> additions)
    {
        var columns = line.Split('	', 4);
        if (columns.Length >= 3 &&
            AdditionVerse.TryParse(columns[1], out var addition) &&
            CanonicalReference.ParseAll(columns[2]) is [var standard, ..])
        {
            additions.TryAdd(addition, standard);
        }
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
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(scheme => scheme.TrimEnd(StraySchemeMark)),
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
internal sealed class VersificationRules(
    IReadOnlyList<IReadOnlyList<TvtmsRow>> blocks,
    IReadOnlySet<TvtmsCorrection> corrected,
    IReadOnlySet<TvtmsSupplement> supplemented,
    IReadOnlyDictionary<AdditionVerse, CanonicalReference> additions)
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

    /// <summary>The undivided form of a scheme, and the divided scheme it is written against.</summary>
    private static readonly Dictionary<string, string> UndividedVariants = new()
    {
        ["GreekUndivided"] = "Greek",
        ["Greek2Undivided"] = "Greek2",
    };

    public bool Covers(Versification tradition) => Schemes.ContainsKey(tradition);

    /// <summary>
    /// Which of <see cref="TvtmsCorrections.All"/> still found the row they correct. One that did not
    /// is about a rule this release of the data no longer states.
    /// </summary>
    public IReadOnlySet<TvtmsCorrection> Corrected { get; } = corrected;

    /// <summary>
    /// Which of <see cref="TvtmsSupplements.All"/> found the passage of the data they were written to
    /// join. One that did not stands as a passage of its own, where the data's rules for the same
    /// verses would compete with it.
    /// </summary>
    public IReadOnlySet<TvtmsSupplement> Supplemented { get; } = supplemented;

    /// <summary>
    /// Which of <see cref="TvtmsSupplements.All"/> stand apart from a passage of the data that places
    /// some of the same verses by a scheme of the supplement's own tradition. The two are then chosen
    /// between separately and the verse is given the addresses of both.
    /// </summary>
    public IReadOnlySet<TvtmsSupplement> Competing { get; } = Competitors(blocks, supplemented);

    /// <summary>Where each verse of the additions to Esther stands in the standard numbering.</summary>
    public IReadOnlyDictionary<AdditionVerse, CanonicalReference> Additions { get; } = additions;

    /// <summary>
    /// The frame for a tradition, taking the scheme it is named after everywhere. It is what the
    /// data says about the tradition rather than about any edition of it, which is all there is to
    /// go on when the edition's own shape is not to hand.
    /// </summary>
    public VersificationFrame Frame(Versification tradition) =>
        Build(tradition, printed: null, passage => Named(passage, Schemes[tradition][0]));

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
    /// Before any of the data's schemes, a passage written down for one edition in
    /// <see cref="TvtmsSupplements"/> is taken if the edition answers every test of it.
    ///
    /// Where no scheme holds, the edition is not placed by a scheme it contradicts throughout: its
    /// tradition's own scheme is kept if what fails is only whether a verse is printed in pieces,
    /// and otherwise the scheme whose tests it answers most, as long as more of them hold than fail.
    /// Brenton leaves out Exodus 25:6 and numbers the rest as the Hebrew does, failing one Hebrew
    /// test and all thirty-one Greek ones; taking the Greek because it is Brenton's tradition put
    /// every verse of the chapter beside the Hebrew of the next. An English edition keeps its own
    /// fallback instead of the first of these, because there a failing piece names an addition it
    /// does not print.
    ///
    /// Where nothing can be decided even so, the tradition's own scheme is used, so a passage the
    /// tests say nothing about is placed exactly as it was before there were any tests to read.
    ///
    /// An edition whose lettered verses were read one by one (<see cref="LetteredEditions"/>) has them
    /// placed as read, ahead of every scheme.
    /// </summary>
    public VersificationFrame Frame(Versification tradition, EditionShape edition)
    {
        var own = Schemes[tradition];
        var others = Schemes.Where(scheme => scheme.Key != tradition).SelectMany(scheme => scheme.Value).ToArray();
        var supplements = TvtmsSupplements.Schemes(tradition);

        return Build(tradition, LetteredEditions.For(edition, Additions), passage =>
            Chosen(passage, supplements, edition, requireEvidence: true) ??
            Chosen(passage, own, edition, requireEvidence: false) ??
            Chosen(passage, others, edition, requireEvidence: true) ??
            (tradition == Versification.English
                ? Closest(passage, [.. own, .. others], edition) ??
                  Named(passage, own[0]).Where(row => row.Tests?.FailsPieceExistence(edition) is not true)
                : ApartFromPieces(passage, own[0], edition) ??
                  Closest(passage, [.. own, .. others], edition) ??
                  Named(passage, own[0])));
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

    /// <summary>
    /// The scheme, when the only tests of it the edition fails ask whether a verse is printed in
    /// pieces. An edition that prints Exodus 38:11 whole still numbers the chapter as the scheme that
    /// divides it does.
    /// </summary>
    private static IEnumerable<TvtmsRow>? ApartFromPieces(
        IReadOnlyList<TvtmsRow> passage,
        string scheme,
        EditionShape edition)
    {
        var rows = Named(passage, scheme).ToList();
        return rows.Count > 0 && rows.All(row =>
            row.Tests?.Answer(edition) is not false || row.Tests.FailsOnlyPieceExistence(edition))
            ? rows
            : null;
    }

    /// <summary>
    /// The scheme whose tests the edition answers most, counted as the rows that hold less the rows
    /// that fail, or null where no scheme has more of the one than the other.
    /// </summary>
    private static IEnumerable<TvtmsRow>? Closest(
        IReadOnlyList<TvtmsRow> passage,
        IReadOnlyList<string> schemes,
        EditionShape edition)
    {
        string? best = null;
        var margin = 0;

        foreach (var scheme in schemes)
        {
            var answers = Named(passage, scheme).Select(row => row.Tests?.Answer(edition)).ToList();
            var held = answers.Count(answer => answer is true) - answers.Count(answer => answer is false);
            if (held > margin)
            {
                best = scheme;
                margin = held;
            }
        }

        return best is null ? null : Named(passage, best);
    }

    /// <summary>
    /// A scheme's rows in the passage. The undivided form of a scheme is written as the rows that
    /// differ from the divided one — in Exodus 40 three rows about 38:27 and nothing about the
    /// renumbering of the chapter — so it is the divided scheme's rows with its own in place of the
    /// ones about the same verses.
    /// </summary>
    private static IEnumerable<TvtmsRow> Named(IReadOnlyList<TvtmsRow> passage, string scheme)
    {
        var rows = passage.Where(row => row.Traditions.Contains(scheme)).ToList();
        if (rows.Count == 0 || !UndividedVariants.TryGetValue(scheme, out var divided))
        {
            return rows;
        }

        var covered = rows.SelectMany(row => row.Sources).ToHashSet();
        return
        [
            .. rows,
            .. passage.Where(row =>
                row.Traditions.Contains(divided) &&
                !row.Traditions.Contains(scheme) &&
                !row.Sources.Any(covered.Contains)),
        ];
    }

    private static HashSet<TvtmsSupplement> Competitors(
        IReadOnlyList<IReadOnlyList<TvtmsRow>> blocks,
        IReadOnlySet<TvtmsSupplement> supplemented)
    {
        var placed = Schemes.ToDictionary(
            tradition => tradition.Key,
            tradition => blocks.SelectMany(block => block)
                .Where(row => row.Traditions.Any(tradition.Value.Contains))
                .SelectMany(row => row.Sources)
                .ToHashSet());

        return
        [
            .. TvtmsSupplements.All.Where(supplement =>
                !supplemented.Contains(supplement) &&
                supplement.Rows.Any(row => row.Sources.Any(placed[supplement.Tradition].Contains))),
        ];
    }

    private VersificationFrame Build(
        Versification tradition,
        IReadOnlyDictionary<PrintedAddress, IReadOnlyList<CanonicalReference>>? printed,
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

        return new VersificationFrame(tradition, rules, printed);
    }
}
