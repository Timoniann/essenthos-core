using Essenthos.Core.Corpus;

namespace Essenthos.Core.Descriptions;

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

        // The animals whose Hebrew word the old translations render strangely (the owner's word of
        // 2026-11-16): what the Greek and Latin call them where no text of the reader's
        // language prints it (the sirens of the ostrich and the jackal, the onocentaurs of the iyyim,
        // the griffin, the goat-stag, the camelopard), and what the lexicons take them for (the hyrax,
        // the dugong). Lilith keeps the singular onocentaur, which the Greek gives her at Isaiah 34:14.
        ["Hyrax"] = ["shaphan-animal"],
        ["Rock badger"] = ["shaphan-animal"],
        ["Jerboa"] = ["shaphan-animal"],
        ["Даман"] = ["shaphan-animal"],
        ["Тушканчик"] = ["shaphan-animal"],
        ["Кролик"] = ["shaphan-animal"],
        ["Klippschliefer"] = ["shaphan-animal"],
        ["Klippdachs"] = ["shaphan-animal"],
        ["Damán"] = ["shaphan-animal"],
        ["Борсук"] = ["shaphan-animal", "tachash"],
        ["Sirens"] = ["yaanah", "tan-jackal"],
        ["Сирени"] = ["yaanah", "tan-jackal"],
        ["Сирены"] = ["yaanah", "tan-jackal"],
        ["Sirenen"] = ["yaanah", "tan-jackal"],
        ["Sirenas"] = ["yaanah", "tan-jackal"],
        ["Страус"] = ["yaanah"],
        ["Струсь"] = ["yaanah"],
        ["Dugong"] = ["tachash"],
        ["Sea cow"] = ["tachash"],
        ["Дюгонь"] = ["tachash"],
        ["Seekuh"] = ["tachash"],
        ["Dugongo"] = ["tachash"],
        ["Vaca marina"] = ["tachash"],
        ["Кожі сині"] = ["tachash"],
        ["Кожи синие"] = ["tachash"],
        ["Барсук"] = ["tachash"],
        ["Dachs"] = ["tachash"],
        ["Tejón"] = ["tachash"],
        ["Onocentaurs"] = ["iyyim"],
        ["Онокентаври"] = ["iyyim"],
        ["Онокентавры"] = ["iyyim"],
        ["Onokentauren"] = ["iyyim"],
        ["Onocentauros"] = ["iyyim"],
        ["Arrow snake"] = ["qippoz"],
        ["Стрілозмій"] = ["qippoz"],
        ["Стрелозмей"] = ["qippoz"],
        ["Pfeilnatter"] = ["qippoz"],
        ["Porcupine"] = ["qippod"],
        ["Дикобраз"] = ["qippod"],
        ["Stachelschwein"] = ["qippod"],
        ["Puercoespín"] = ["qippod"],
        ["Gecko"] = ["anaqah"],
        ["Геккон"] = ["anaqah"],
        ["Geco"] = ["anaqah"],
        ["Monitor lizard"] = ["koach"],
        ["Варан"] = ["koach"],
        ["Waran"] = ["koach"],
        ["Varano"] = ["koach"],
        ["Mole rat"] = ["choled"],
        ["Сліпак"] = ["choled"],
        ["Слепыш"] = ["choled"],
        ["Blindmaus"] = ["choled"],
        ["Rata topo"] = ["choled"],
        ["Land crocodile"] = ["tsav"],
        ["Сухопутний крокодил"] = ["tsav"],
        ["Сухопутный крокодил"] = ["tsav"],
        ["Landkrokodil"] = ["tsav"],
        ["Cocodrilo terrestre"] = ["tsav"],
        ["Hoopoe"] = ["dukhiphat"],
        ["Одуд"] = ["dukhiphat"],
        ["Удод"] = ["dukhiphat"],
        ["Goat-stag"] = ["akko"],
        ["Козлоолень"] = ["akko"],
        ["Bockhirsch"] = ["akko"],
        ["Tragélafo"] = ["akko"],
        ["Addax"] = ["dishon-animal"],
        ["Аддакс"] = ["dishon-animal"],
        ["Giraffe"] = ["zemer"],
        ["Жираф"] = ["zemer"],
        ["Jirafa"] = ["zemer"],
        ["Camelopard"] = ["zemer"],
        ["Oryx"] = ["teo"],
        ["Орикс"] = ["teo"],
        ["Órix"] = ["teo"],
        ["Griffin"] = ["peres"],
        ["Грифон"] = ["peres"],
        ["Greif"] = ["peres"],
        ["Grifo"] = ["peres"],
        ["Lammergeier"] = ["peres"],
        ["Bearded vulture"] = ["peres"],
        ["Ягнятник"] = ["peres"],
        ["Bartgeier"] = ["peres"],
        ["Snake fighter"] = ["chargol"],
        ["Змієборець"] = ["chargol"],
        ["Змееборец"] = ["chargol"],
        ["Schlangenkämpfer"] = ["chargol"],
        ["Jackal"] = ["tan-jackal"],
        ["Шакал"] = ["tan-jackal"],
        ["Schakal"] = ["tan-jackal"],
        ["Chacal"] = ["tan-jackal"],

        // What Ezekiel calls them before he knows them for cherubim (Ezekiel 1:5; 10:15, 20): the
        // translations print it of the creatures, not as the record's name, and readers look for it.
        ["Living creatures"] = ["cherubim"],
        ["Живі істоти"] = ["cherubim"],
        ["Живые существа"] = ["cherubim"],
        ["Lebendige Wesen"] = ["cherubim"],
        ["Seres vivientes"] = ["cherubim"],

        // What the Greek translation (the Septuagint) and the Latin call the creatures the Hebrew
        // names: the unicorn for the re'em, the onocentaur and the lamia for Lilith (Isaiah 34:14),
        // demons for the se'irim (Isaiah 13:21; the Synodal's leshie, Isaiah 34:14), the sea monster for Rahab (Job 26:12), the basilisk
        // for the tsepha (Isaiah 11:8), the flying serpents for the saraph (Isaiah 14:29). The dragon
        // is the tannin's (Exodus 7:10), and leads the others called so (Leading).
        ["Dragon"] = ["tannin"],
        ["Dragón"] = ["tannin"],
        ["Дракон"] = ["tannin"],
        ["Drache"] = ["tannin"],
        ["Unicorn"] = ["reem"],
        ["Єдиноріг"] = ["reem"],
        ["Единорог"] = ["reem"],
        ["Unicornio"] = ["reem"],
        ["Onocentaur"] = ["lilith"],
        ["Онокентавр"] = ["lilith"],
        ["Onokentaur"] = ["lilith"],
        ["Onocentauro"] = ["lilith"],
        ["Lamia"] = ["lilith"],
        ["Ламія"] = ["lilith"],
        ["Ламия"] = ["lilith"],
        ["Demons"] = ["seirim"],
        ["Демони"] = ["seirim"],
        ["Демоны"] = ["seirim"],
        ["Dämonen"] = ["seirim"],
        ["Demonios"] = ["seirim"],
        ["Леший"] = ["seirim"],
        ["Лешие"] = ["seirim"],
        ["Лісовик"] = ["seirim"],
        ["Польовик"] = ["seirim"],
        ["Satyr"] = ["seirim"],
        ["Peludo"] = ["seirim"],
        ["Sea monster"] = ["rahab-sea-monster"],
        ["Морське чудовисько"] = ["rahab-sea-monster"],
        ["Морское чудовище"] = ["rahab-sea-monster"],
        ["Seeungeheuer"] = ["rahab-sea-monster"],
        ["Monstruo marino"] = ["rahab-sea-monster"],
        ["Basilisk"] = ["tsepha"],
        ["Василіск"] = ["tsepha"],
        ["Василиск"] = ["tsepha"],
        ["Basilisco"] = ["tsepha"],
        ["Flying serpent"] = ["saraph-serpents"],
        ["Летючий змій"] = ["saraph-serpents"],
        ["Летучий змей"] = ["saraph-serpents"],
        ["Fliegende Schlange"] = ["saraph-serpents"],
        ["Serpiente voladora"] = ["saraph-serpents"],

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
        ["Bisjotja"] = ["biziothiah"],
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
        ["Jephté"] = ["iphtah"],
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
        ["Sahazuma"] = ["shahazumah"],
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
        ["Sikkeron"] = ["shikkeron"],
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

    /// <summary>
    /// The familiar names whose record comes before every other one called so: <em>дракон</em> is a
    /// spelling of Satan in Revelation and of the dragon Daniel killed, but the reader typing it means
    /// the tannin, the general word.
    /// </summary>
    private static readonly HashSet<string> Leads =
        [.. new[] { "Dragon", "Dragón", "Дракон", "Drache" }.Select(NameFolding.Fold)];

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

    /// <summary>The records this name leads to before any other called so; empty where it leads nowhere.</summary>
    public static IReadOnlyList<string> Leading(string typed) =>
        Leads.Contains(NameFolding.Fold(typed)) ? Of(typed) : [];

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
