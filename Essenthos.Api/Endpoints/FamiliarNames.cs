using Essenthos.Core.Corpus;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The names readers call a few records by that no text in the corpus prints: the form a school
/// atlas or a modern translation uses for a place the Bible here spells otherwise. A reader typing
/// <em>Рамсес</em> is looking for the city the Ohienko Bible calls <em>Рамесес</em>, and a search that
/// answers nothing tells them the site does not know it.
///
/// <para>
/// Kept by hand and short: each line is a spelling a reader was seen to type and not find. Every
/// spelling a text of the corpus prints is found without being listed here, through the renderings.
/// A longer list, or one proposed by a model, waits for the owner's word.
/// </para>
/// </summary>
internal static class FamiliarNames
{
    private static readonly IReadOnlyDictionary<string, string[]> Records = new Dictionary<string, string[]>
    {
        [NameFolding.Fold("Рамсес")] = ["rameses"],
        [NameFolding.Fold("Раамсес")] = ["rameses"],
        [NameFolding.Fold("Сокхот")] = ["succoth-2"],
    };

    /// <summary>The records this name is the familiar form of; empty where it is none.</summary>
    public static IReadOnlyList<string> Of(string typed) =>
        Records.GetValueOrDefault(NameFolding.Fold(typed)) ?? [];
}
