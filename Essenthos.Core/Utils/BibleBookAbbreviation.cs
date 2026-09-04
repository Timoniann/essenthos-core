namespace Essenthos.Core.Utils;

public class BibleBookAbbreviation
{
    // The three-letter capitals among the alternatives are the USFM 3.0 book codes, which is how
    // every USFM file in the corpus names its book. Sixteen of them differ from every spelling
    // this table already held, and a file whose book does not resolve is skipped rather than read:
    // the Ukrainian interlinear's LUK went in as 43-LUK.usfm and 505 links it states were never
    // loaded, silently, because nothing here answered to "LUK".
    private static readonly BookAbbreviation[] BookAbbreviations =
    [
        new(1, "Genesis", "Gen", "Gn", ["1M", "1Mose"]),
        new(2, "Exodus", "Exod", "Ex", ["2M", "2Mose", "Exo"]),
        new(3, "Leviticus", "Lev", "Lv", ["3M", "3Mose"]),
        new(4, "Numbers", "Num", "Nm", ["4M", "4Mose", "Numeri"]),
        new(5, "Deuteronomy", "Deut", "Dt", ["5M", "5Mose", "Deuteronomium", "Deu"]),
        new(6, "Joshua", "Josh", "Jo", ["Jos", "Josua"]),
        new(7, "Judges", "Judg", "Jdg", ["Judices"]),
        new(8, "Ruth", "Ruth", "Ru", ["RUT"]),
        new(9, "1 Samuel", "1 Sam", "1Sm", ["I Sam", "1 Sa", "Samuel_I"]),
        new(10, "2 Samuel", "2 Sam", "2Sm", ["II Sam", "2 Sa", "Samuel_II"]),
        new(11, "1 Kings", "1 Kgs", "1Ki", ["I Kgs", "1 Ki", "Reges_I"]),
        new(12, "2 Kings", "2 Kgs", "2Ki", ["II Kgs", "2 Ki", "Reges_II"]),
        new(13, "1 Chronicles", "1 Chr", "1Ch", ["I Chr", "1 Ch", "Chronica_I"]),
        new(14, "2 Chronicles", "2 Chr", "2Ch", ["II Chr", "2 Ch", "Chronica_II"]),
        new(15, "Ezra", "Ezra", "Ezr", ["Esra"]),
        new(16, "Nehemiah", "Neh", "Ne", ["Nehemia"]),
        new(17, "Esther", "Esth", "Es", ["Est"]),
        new(18, "Job", "Job", "Jb", ["Iob"]),
        new(19, "Psalms", "Pss", "Ps", ["Psalmi", "Psalm", "PSA"]),   // "Psalm" is how the Berean edition names it, and the whole book went missing without it
        new(20, "Proverbs", "Prov", "Prv", ["Proverbia", "PRO"]),
        new(21, "Ecclesiastes", "Eccles", "Eccl", ["Ec", "ECC"]),
        new(22, "Song of Solomon", "Song", "Sg", ["Sg", "Songs of Solomon", "Songs", "Canticum", "SNG"]),
        new(23, "Isaiah", "Isa", "Is", ["Jesaia"]),
        new(24, "Jeremiah", "Jer", "Je", ["Jeremia"]),
        new(25, "Lamentations", "Lam", "La", ["Threni"]),
        new(26, "Ezekiel", "Ezek", "Eze", ["Ezechiel", "EZK"]),
        new(27, "Daniel", "Dan", "Dn", ["Da"]),
        new(28, "Hosea", "Hos", "Ho"),
        new(29, "Joel", "Joel", "Jl", ["JOL"]),
        new(30, "Amos", "Amos", "Am", ["AMO"]),
        new(31, "Obadiah", "Obad", "Ob", ["Obadia", "OBA"]),
        new(32, "Jonah", "Jonah", "Jon", ["Jnh", "Jona"]),
        new(33, "Micah", "Mic", "Mi", ["Micha"]),
        new(34, "Nahum", "Nah", "Na", ["NAM"]),
        new(35, "Habakkuk", "Hab", "Ha", ["Habakuk"]),
        new(36, "Zephaniah", "Zeph", "Zep", ["Zephania"]),
        new(37, "Haggai", "Hag", "Hg"),
        new(38, "Zechariah", "Zech", "Zec", ["Sacharia"]),
        new(39, "Malachi", "Mal", "Ml", ["Maleachi"]),
        new(40, "Matthew", "Matt", "Mt", ["MAT"]),
        new(41, "Mark", "Mark", "Mk", ["MRK"]),
        new(42, "Luke", "Luke", "Lk", ["LUK"]),
        new(43, "John", "John", "Jn", ["Johaness", "Joh", "JHN"]),
        new(44, "Acts", "Acts", "Ac", ["Apg", "Acts of the Apostles", "ACT"]),
        new(45, "Romans", "Rom", "Ro", ["Röm"]),
        new(46, "1 Corinthians", "1 Cor", "1Co", ["I Cor", "1 Co", "1Kor"]),
        new(47, "2 Corinthians", "2 Cor", "2Co", ["II Cor", "2 Co", "2Kor"]),
        new(48, "Galatians", "Gal", "Ga"),
        new(49, "Ephesians", "Eph", "Ep"),
        new(50, "Philippians", "Phil", "Php"),
        new(51, "Colossians", "Col", "Col", ["Kol"]),
        new(52, "1 Thessalonians", "1 Thess", "1Th", ["I Thess", "1 Th", "1Thes"]),
        new(53, "2 Thessalonians", "2 Thess", "2Th", ["II Thess", "2 Th", "2Thes"]),
        new(54, "1 Timothy", "1 Tim", "1Tm", ["I Tim", "1 Ti"]),
        new(55, "2 Timothy", "2 Tim", "2Tm", ["II Tim", "2 Ti"]),
        new(56, "Titus", "Titus", "Tit"),
        new(57, "Philemon", "Philem", "Phlm", ["Phlm", "Phim", "PHM"]),
        new(58, "Hebrews", "Heb", "He"),
        new(59, "James", "Jas", "Ja", ["Jak"]),
        new(60, "1 Peter", "1 Pet", "1Pt", ["I Pet", "1 Pe", "1Petr"]),
        new(61, "2 Peter", "2 Pet", "2Pt", ["II Pet", "2 Pe", "2Petr"]),
        new(62, "1 John", "1 John", "1Jn", ["I John", "1 Jn", "1Jo"]),
        new(63, "2 John", "2 John", "2Jn", ["II John", "2 Jn", "2Jo"]),
        new(64, "3 John", "3 John", "3Jn", ["III John", "3 Jn", "3Jo"]),
        new(65, "Jude", "Jude", "Jud"),
        new(66, "Revelation", "Rev", "Rv", ["Apocalypse", "The Revelation", "Rv", "Offenbarung", "Offb"]),
        new(67, "Baruch", "Bar", "Bar"),
        new(68, "1 Esdras", "1 Esd", "1Es", ["I Esd", "1 Es"]),
        new(69, "2 Esdras", "2 Esd", "2Es", ["II Esd", "2 Es"]),
        new(70, "Tobit", "Tob", "Tb"),
        new(71, "Judith", "Jth", "Jdt"),
        new(72, "Sirach (Ecclesiasticus)", "Sir", "Si", ["Ecclesiasticus", "Eccl", "Ecclus"]),
        new(73, "1 Maccabees", "1 Macc", "1Mc", ["I Mac", "1 Ma"]),
        new(74, "2 Maccabees", "2 Macc", "2Mc", ["II Mac", "2 Ma"]),
        new(75, "Wisdom of Solomon", "Ws", "Ws", ["Wisdom", "WIS"]),

        // The rest of what the Greek canons carry, and what Brenton prints. Numbered past the
        // Western deuterocanon rather than interleaved with it, because an ordinal here is an
        // identity — it is in every saved URL and on every book row in the database — and a canon
        // decides order separately (DOC-0090).
        //
        // Greek Esther and Greek Daniel get no ordinal of their own: they are Esther and Daniel,
        // longer. A witness holds its own book at the same canonical ordinal and its own
        // versification, which is the whole point of the model. The pieces printed apart in
        // Brenton — Susanna, Bel, the Letter of Jeremiah — do get one, because a canon that prints
        // them as books has to be able to name them.
        new(76, "Letter of Jeremiah", "Ep Jer", "LJe", ["LJE", "Epistle of Jeremiah", "EpJer"]),
        new(77, "Susanna", "Sus", "Sus", ["SUS"]),
        new(78, "Bel and the Dragon", "Bel", "Bel", ["BEL", "Bel and Dragon"]),
        new(79, "Prayer of Manasseh", "Pr Man", "PrM", ["MAN", "Manasseh", "PrMan"]),
        new(80, "3 Maccabees", "3 Macc", "3Mc", ["3MA", "III Mac", "3 Ma"]),
        new(81, "4 Maccabees", "4 Macc", "4Mc", ["4MA", "IV Mac", "4 Ma"]),
        new(82, "Psalm 151", "Ps 151", "Ps151", ["PS2", "Psalm151"]),
        new(83, "Odes", "Ode", "Ode", ["ODA", "ODES"]),
        new(84, "Psalms of Solomon", "Ps Sol", "PsS", ["PSS", "PssSol"]),
    ];

    private static readonly Dictionary<string, BookAbbreviation> AbbreviationMap;
    private static readonly Dictionary<string, BookAbbreviation> ShortAbbreviationMap;
    private static readonly Dictionary<string, BookAbbreviation> NameMap = new();

    static BibleBookAbbreviation()
    {
        AbbreviationMap = new Dictionary<string, BookAbbreviation>(BookAbbreviations.Length);
        ShortAbbreviationMap = new Dictionary<string, BookAbbreviation>(BookAbbreviations.Length);
        foreach (var book in BookAbbreviations)
        {
            AbbreviationMap[book.StandardAbbreviation.Aligned] = book;
            ShortAbbreviationMap[book.ShortAbbreviation.Aligned] = book;
            NameMap[book.FullName.Aligned] = book;
        }
    }

    public static BookAbbreviation? GetByOrdinal(int ordinal)
    {
        return BookAbbreviations.FirstOrDefault(b => b.Ordinal == ordinal);
    }

    public static BookAbbreviation? GetAbbreviation(string bookNameOrAbbreviation)
    {
        var nameOrAbbr = AlignAbbreviation(bookNameOrAbbreviation);
        if (AbbreviationMap.TryGetValue(nameOrAbbr, out var book))
        {
            return book;
        }

        if (ShortAbbreviationMap.TryGetValue(nameOrAbbr, out book))
        {
            return book;
        }

        if (NameMap.TryGetValue(nameOrAbbr, out book))
        {
            return book;
        }

        foreach (var bookAbbreviation in BookAbbreviations)
        {
            if (bookAbbreviation.AlternativeAbbreviations.Any(a => a.Aligned == nameOrAbbr))
            {
                return bookAbbreviation;
            }
        }

        return null;
    }

    public class Abbreviation(string full)
    {
        public string Full { get; } = full;

        public string Aligned { get; } = AlignAbbreviation(full);
    }

    private static string AlignAbbreviation(string abbr)
    {
        return abbr.Replace(" ", "").ToUpperInvariant();
    }

    public class BookAbbreviation
    {
        public int Ordinal { get; }

        public Abbreviation StandardAbbreviation { get; }

        public Abbreviation FullName { get; }

        public Abbreviation TraditionalAbbreviation { get; }

        public Abbreviation ShortAbbreviation { get; }

        public Abbreviation[] AlternativeAbbreviations { get; }

        public BookAbbreviation(int ordinal, string fullName, string traditionalAbbreviation, string shortAbbreviation)
            : this(ordinal, fullName, traditionalAbbreviation, shortAbbreviation, [])
        {
        }

        public BookAbbreviation(int ordinal,
            string fullName,
            string traditionalAbbreviation,
            string shortAbbreviation,
            string[] alternativeAbbreviations)
        {
            Ordinal = ordinal;
            FullName = new Abbreviation(fullName);
            TraditionalAbbreviation = new Abbreviation(traditionalAbbreviation);
            ShortAbbreviation = new Abbreviation(shortAbbreviation);
            AlternativeAbbreviations = alternativeAbbreviations.Select(a => new Abbreviation(a)).ToArray();
            StandardAbbreviation = TraditionalAbbreviation;
        }
    };
}