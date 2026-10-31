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
        ["Рамсес"] = ["rameses"],
        ["Раамсес"] = ["rameses"],
        ["Сокхот"] = ["succoth-2"],

        // The modern spellings a Spanish or German name form had before it followed the spelling
        // its main text prints: nothing else holds them any more, and a reader still types them.
        ["Aditajim"] = ["adithaim"],
        ["Adonja"] = ["adonijah-2"],
        ["Ahoahiter"] = ["ahohites"],
        ["Ahusam"] = ["ahuzzam"],
        ["Ahusath"] = ["ahuzzath"],
        ["Alammelech"] = ["allammelech"],
        ["Aphia"] = ["apphia"],
        ["Arjoch"] = ["arioch"],
        ["Arpachsad"] = ["arpachshad"],
        ["Asalja"] = ["azaliah"],
        ["Asan"] = ["azzan"],
        ["Asdodeos"] = ["ashdodites"],
        ["Asnot-Tabor"] = ["aznothtabor"],
        ["Asterathiter"] = ["ashterathites"],
        ["Asur"] = ["azzur", "azzur-2", "azzur-3"],
        ["Atarot-Addar"] = ["atarothaddar"],
        ["Baera"] = ["baara"],
        ["Bedeja"] = ["bedeiah"],
        ["Beerot-Bne-Jaakan"] = ["beerothbenejaakan"],
        ["Beth-Kerem"] = ["bethhaccherem"],
        ["Biftha"] = ["biztha"],
        ["Bisjotja"] = ["bizjothjah"],
        ["Briiter"] = ["beerites"],
        ["Cam"] = ["ham"],
        ["Chelluhi"] = ["cheluhi"],
        ["Dabbaseth"] = ["dabbesheth"],
        ["Diesar"] = ["iezer"],
        ["Eljakim"] = ["eliakim-4", "eliakim-5"],
        ["Ezequías"] = ["hezekiah-2", "hezekiah-3"],
        ["Geguel"] = ["geuel"],
        ["Gemali"] = ["gemalli"],
        ["Gideel"] = ["giddel", "giddel-2"],
        ["Gisriter"] = ["gezrites"],
        ["Hagith"] = ["haggith"],
        ["Hallohesch"] = ["hallohesh"],
        ["Hasschub"] = ["hasshub-3"],
        ["Helequitas"] = ["helkites"],
        ["Hor-Gidgad"] = ["horhaggidgad"],
        ["Horasan"] = ["chorashan"],
        ["Hoschea"] = ["hoshea"],
        ["Hupim"] = ["huppim"],
        ["Jaesija"] = ["jaaziah"],
        ["Jahasja"] = ["jahzeiah"],
        ["Jarach"] = ["jerah"],
        ["Jaschen"] = ["jashen"],
        ["Jaschuwiter"] = ["jashubites"],
        ["Jatba"] = ["jotbah"],
        ["Jehija"] = ["jehiah"],
        ["Jephté"] = ["jephthah-2"],
        ["Jerimoth"] = ["jeremoth-6"],
        ["Jesija"] = ["izziah"],
        ["Jeswiter"] = ["jesuites"],
        ["Jigal"] = ["igal-3"],
        ["Jireon"] = ["yiron"],
        ["Jischbah"] = ["ishbah"],
        ["Jischi"] = ["ishi", "ishi-2"],
        ["Jischwi"] = ["ishvi-2"],
        ["Jissija"] = ["isshiah-4", "isshijah"],
        ["Jitran"] = ["ithran-2"],
        ["Jizrahja"] = ["izrahiah"],
        ["Jokthel"] = ["joktheel"],
        ["Josabad"] = ["jozacar"],
        ["Josef"] = ["joseph", "joseph-10", "joseph-2", "joseph-3"],
        ["Kaphthorim"] = ["caphtorim-son-of-mizraim"],
        ["Karsena"] = ["carshena"],
        ["Kasluhim"] = ["casluhim-son-of-mizraim"],
        ["Kauda"] = ["cauda"],
        ["Kefar-Ammonai"] = ["chefarhaamonai"],
        ["Kelubai"] = ["chelubai"],
        ["Kisi"] = ["kishi"],
        ["Kislot-Thabor"] = ["chislothtabor"],
        ["Kosba"] = ["cozeba"],
        ["Lykien"] = ["lycia"],
        ["Magpiask"] = ["magpiash"],
        ["Mahasiot"] = ["mahazioth"],
        ["Mahseja"] = ["mahseiah"],
        ["Malothi"] = ["mallothi"],
        ["Manahathiter"] = ["manahethites"],
        ["Matnai"] = ["mattenai", "mattenai-3"],
        ["Matthata"] = ["mattattah"],
        ["Mecherathiter"] = ["mecherathites"],
        ["Mischael"] = ["mishael-3"],
        ["Mordechai"] = ["mordecai-2"],
        ["Musiten"] = ["mushites"],
        ["Naftuh"] = ["naphtuh"],
        ["Nehuschta"] = ["nehushta"],
        ["Otoniel"] = ["othniel"],
        ["Pahat-Moab"] = ["pahathmoab"],
        ["Parziter"] = ["pharzites"],
        ["Pealtai"] = ["peullethai"],
        ["Phasur"] = ["pashhur"],
        ["Pinhas"] = ["phinehas-2", "phinehas-3"],
        ["Priszilla"] = ["priscilla"],
        ["Raquel"] = ["rachel"],
        ["Rehobot-Ir"] = ["rehobothir"],
        ["Sabud"] = ["zabbud"],
        ["Sacharja"] = ["shehariah"],
        ["Sahazuma"] = ["shahazimah", "shahazumah"],
        ["Samothrake"] = ["samothrace"],
        ["Samscherai"] = ["shamsherai"],
        ["Saschai"] = ["shashai"],
        ["Schallecheth"] = ["shallecheth"],
        ["Schetar"] = ["shethar"],
        ["Schetar-Bosnai"] = ["shetharbozenai"],
        ["Scheva"] = ["sheva"],
        ["Selomoth"] = ["shelomoth", "shelomoth-2"],
        ["Sifa"] = ["ziphah"],
        ["Sifjon"] = ["ziphion"],
        ["Sikkeron"] = ["shicron", "shikkeron"],
        ["Simeon"] = ["shimon"],
        ["Simschai"] = ["shimshai"],
        ["Skeuas"] = ["sceva"],
        ["Tachpanhes"] = ["tahpanhes"],
        ["Talasar"] = ["telassar"],
        ["Thirathiter"] = ["tirathites"],
        ["Thirhana"] = ["tirhanah"],
        ["Tikwa"] = ["tikvah"],
        ["Tirsa"] = ["tirzah-2"],
        ["Togorma"] = ["togarmah-2"],
        ["Usen-Scheera"] = ["uzzensheerah"],
        ["Zefanja"] = ["zephaniah-2", "zephaniah-3", "zephaniah-4"],
        ["Zidim"] = ["ziddim"],
        ["Zurisadai"] = ["zurishaddai"],
    };

    private static readonly IReadOnlyDictionary<string, string[]> ByFolded = Records
        .GroupBy(name => NameFolding.Fold(name.Key))
        .ToDictionary(same => same.Key, same => same.SelectMany(name => name.Value).Distinct().ToArray());

    /// <summary>Each name's words folded, the whole name among them: what a part typed has to open.</summary>
    private static readonly IReadOnlyList<(string[] Words, string[] Slugs)> Worded =
    [
        .. Records.Select(name => (
            Words: name.Key.Split('-', ' ').Select(NameFolding.Fold).Append(NameFolding.Fold(name.Key)).ToArray(),
            Slugs: name.Value)),
    ];

    /// <summary>The records this name is the familiar form of; empty where it is none.</summary>
    public static IReadOnlyList<string> Of(string typed) =>
        ByFolded.GetValueOrDefault(NameFolding.Fold(typed)) ?? [];

    /// <summary>
    /// The records a word of whose familiar name opens with what was typed, the rule every other name
    /// is found by: <em>Raqu</em> finds what <em>Raquel</em> does, <em>Tabor</em> what <em>Asnot-Tabor</em> does.
    /// </summary>
    public static IEnumerable<string> Opening(string typed)
    {
        var folded = NameFolding.Fold(typed);
        return folded.Length == 0
            ? []
            : Worded
                .Where(name => name.Words.Any(word => word.StartsWith(folded, StringComparison.Ordinal)))
                .SelectMany(name => name.Slugs)
                .Distinct();
    }
}
