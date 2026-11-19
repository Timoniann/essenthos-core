using Essenthos.Core.Conversion;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// Wade Maxfield's Strong numbers on the American Standard Version (<see cref="MaxfieldAsvEdition"/>), read from a
/// small copy of his SQLite file, laid onto the ASV's words and matched against Nestle 1904 (<see cref="StrongNumberSource"/>):
/// a supporting layer that leaves out a clause particle's number on a content word, a number naming several Greek words
/// nothing pairs off, and links of its own from function words, from the words the ASV prints as supplied, from the nouns a
/// translator adds (<em>things</em>, <em>one</em>) and from one Hebrew word's number on several phrases.
/// </summary>
public sealed class MaxfieldAsvStrongTests : IDisposable
{
    private const string Asv = EnglishTextSource.AmericanStandard;

    private const string Pair = Asv + "-" + NestleTextSource.Slug;

    private const string Terms =
        "The American Standard Version was published in 1901 and is in the public domain in the United States.\r\r"
        + "Applying Strong's to the text was done as part of the \"Cross Word Project\" (www.crosswordproject.com) and is  "
        + "copyright Wade Maxfield and released by him for any purpose.\r\r  PREFACE TO THE AMERICAN EDITION";

    private static readonly string[] Books =
    [
        "Genesis", "Exodus", "Leviticus", "Numbers", "Deuteronomy", "Joshua", "Judges", "Ruth", "OneSam", "TwoSam",
        "OneKing", "TwoKing", "OneChr", "TwoChr", "Ezra", "Nehemiah", "Esther", "Job", "Psalms", "Proverbs",
        "Ecclesiastes", "SOS", "Isaiah", "Jeremiah", "Lamentations", "Ezekiel", "Daniel", "Hosea", "Joel", "Amos",
        "Obadiah", "Jonah", "Micah", "Nahum", "Habakkuk", "Zephaniah", "Haggai", "Zechariah", "Malachi",
        "Matthew", "Mark", "Luke", "John", "Acts", "Romans", "OneCor", "TwoCor", "Galatians", "Ephesians",
        "Philippians", "Colossians", "OneThes", "TwoThes", "OneTim", "TwoTim", "Titus", "Philemon", "Hebrews", "James",
        "OnePeter", "TwoPeter", "OneJohn", "TwoJohn", "ThreeJohn", "Jude", "Revelation",
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-maxfield", Guid.NewGuid().ToString("N"));

    private readonly ProductStore _store;

    public MaxfieldAsvStrongTests()
    {
        ShippedStrongNumbers.CopyInto(_root);
        _store = new ProductStore(_root);

        // Genesis 1:1 as the file writes it: the head word numbered, the language now and then with a return after it,
        // a footnote marker on a word, and a chapter 0 holding a piece of the book's title. Luke 4:12 has ὅτι's number on
        // "God", the last word of the clause; 4:13 numbers "God" where the Greek writes θεός twice.
        Edition(Path.Combine([_root, .. MaxfieldAsvEdition.EditionFile]), Terms,
            ("Genesis", [(0, 1, "of", " ", 0), (1, 1, "In", "", 0), (1, 1, "the", "", 0), (1, 1, "beginning", "H", 7225),
                (1, 1, "God", "H\r", 430), (1, 1, "created", "F", 1)]),
            ("Luke", [(4, 12, "Jesus", "G", 2424), (4, 12, "said", "G", 2036), (4, 12, "that", "G", 3754), (4, 12, "but", "g", 1161),
                (4, 12, "the", "", 0), (4, 12, "Lord", "G", 2962), (4, 12, "thy", "G", 4675), (4, 12, "God", "G", 3754),
                (4, 13, "God", "G", 2316), (4, 13, "spake", "G", 2980),
                (11, 21, "When", "G", 3752), (11, 21, "the", "", 0), (11, 21, "strong", "", 0), (11, 21, "man", "G", 2478),
                (11, 21, "armed", "G", 2528), (11, 21, "guardeth", "G", 5442), (11, 21, "his", "G", 1438), (11, 21, "court", "G", 833)]),
            ("Isaiah", [(32, 19, "and", "", 0), (32, 19, "the", "", 0), (32, 19, "city", "H", 5892), (32, 19, "shall", "", 0),
                (32, 19, "be", "", 0), (32, 19, "utterly", "H", 8213), (32, 19, "laid", "", 0), (32, 19, "low", "H", 8213)]),
            ("Matthew", [(9, 35, "all", "", 0), (9, 35, "manner", "G", 3956), (9, 35, "of", "", 0), (9, 35, "disease", "G", 3554),
                (14, 22, "till", "G", 2193), (14, 22, "he", "G", 3739), (14, 22, "should", "", 0), (14, 22, "send", "G", 630),
                (14, 22, "the", "", 0), (14, 22, "multitudes", "G", 3793), (14, 22, "away", "G", 630)]),
            ("Mark", [(12, 24, "for", "", 0), (12, 24, "this", "", 0), (12, 24, "cause", "G", 3778), (12, 24, "ye", "", 0), (12, 24, "err", "G", 4105)]),
            ("John", [(9, 25, "one thing", "G", 1520), (9, 25, "I", "", 0), (9, 25, "know", "G", 1492)]),
            ("Acts", [(5, 32, "witnesses", "G", 3144), (5, 32, "of", "", 0), (5, 32, "these", "", 0), (5, 32, "things", "G", 4487),
                (28, 18, "no", "", 0), (28, 18, "cause", "G", 156), (28, 18, "of", "", 0), (28, 18, "death", "G", 2288)]),
            ("Philippians", [(2, 4, "not", "G", 3361), (2, 4, "looking", "G", 4648), (2, 4, "each", "G", 1538), (2, 4, "of", "", 0),
                (2, 4, "you", "", 0), (2, 4, "to", "", 0), (2, 4, "his", "", 0), (2, 4, "own", "", 0), (2, 4, "things", "G", 1438)]));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void Each_word_takes_its_own_number_and_a_footnote_or_a_title_fragment_none()
    {
        var verses = MaxfieldAsvEdition.Read(Path.Combine([_root, .. MaxfieldAsvEdition.EditionFile]), ShippedStrongNumbers.Lists.ClauseParticles, ShippedStrongNumbers.EnglishFunctionWord);

        verses.Keys.Should().BeEquivalentTo([(1, 1, 1), (42, 4, 12), (42, 4, 13), (42, 11, 21), (23, 32, 19), (40, 14, 22), (43, 9, 25),
            (44, 5, 32), (50, 2, 4), (40, 9, 35), (41, 12, 24), (44, 28, 18)], "chapter 0 is the book's title, not a verse");
        verses[(1, 1, 1)].Select(word => (word.Text, string.Join(' ', word.Numbers))).Should().Equal(
            ("In", ""), ("the", ""), ("beginning", "H7225"), ("God", "H430"), ("created", ""));
        verses[(1, 1, 1)].Select(word => word.Unit).Should().OnlyHaveUniqueItems("every word is a rendering of its own");
    }

    /// <summary>
    /// Luke 4:12, <em>the Lord thy God</em>: the file numbers <em>God</em> G3754, the ὅτι the English does not render, so the
    /// number goes and <em>God</em> is untagged. The same number on <em>that</em>, which renders it, stays, and so does δέ's on
    /// <em>but</em>, its language written in lower case.
    /// </summary>
    [Fact]
    public void A_clause_particle_s_number_on_a_content_word_is_dropped_and_on_a_function_word_kept()
    {
        var verse = MaxfieldAsvEdition.Read(Path.Combine([_root, .. MaxfieldAsvEdition.EditionFile]), ShippedStrongNumbers.Lists.ClauseParticles, ShippedStrongNumbers.EnglishFunctionWord, out var dropped)[(42, 4, 12)];

        verse.Single(word => word.Text == "God").Numbers.Should().BeEmpty();
        verse.Single(word => word.Text == "that").Numbers.Should().Equal("G3754");
        verse.Single(word => word.Text == "but").Numbers.Should().Equal("G1161");
        dropped.Should().Be(1);
        ShippedStrongNumbers.Lists.ClauseParticles.Should().Contain(["G3754", "G2443", "G1161", "G1352", "G1063", "G3767"]);
    }

    [Fact]
    public void A_file_whose_copyright_table_no_longer_states_the_terms_is_refused()
    {
        var other = Path.Combine(_root, "other", "ASV.bbl");
        Edition(other, "Copyright Wade Maxfield, all rights reserved.", ("Genesis", [(1, 1, "God", "H", 430)]));

        var read = () => MaxfieldAsvEdition.Read(other, ShippedStrongNumbers.Lists.ClauseParticles, ShippedStrongNumbers.EnglishFunctionWord);

        read.Should().Throw<InvalidDataException>().WithMessage("*released by him for any purpose*");
    }

    [Fact]
    public void The_ASV_s_numbers_are_laid_from_Maxfield_s_edition_and_matched_against_the_Hebrew_and_Nestle()
    {
        SideStrongNumbers.Edition(Asv).Source.Should().Be("AmericanStandard1901Strong/ASV.bbl");
        StrongNumberSource.All.Should().ContainSingle(source => source.States(Asv + "-BHSA"));
        StrongNumberSource.All.Should().ContainSingle(source => source.States(Pair));
        StrongNumberSource.All.Should().NotContain(source => source.States(Asv + "-TR1894"));
    }

    /// <summary>
    /// The ASV's rows are a supporting numbering's: the italics' absences and the names pass do not yield to them, as they do
    /// to a composition or to another laid numbering.
    /// </summary>
    [Fact]
    public void Only_the_ASV_s_numbering_is_one_the_producers_after_it_do_not_yield_to()
    {
        StrongNumberSource.Supporting(MaxfieldAsvEdition.Credit + ", matched within the verse against BHSA").Should().BeTrue();
        StrongNumberSource.Supporting(ReinaValeraStrongLinkLoader.Credit + ", matched within the verse against BHSA").Should().BeFalse();
        StrongNumberSource.Supporting("SIL.Machine, aligned through KJV and through BSB").Should().BeFalse();
    }

    [Fact]
    public void A_content_word_s_number_naming_one_Greek_word_is_a_link_at_nine_tenths_credited_to_Maxfield()
    {
        var jesus = Rows().Single(row => row.From == "42.4:12.1");

        jesus.To.Should().Be("42.4:12.1");
        jesus.Confidence.Should().Be(StrongNumberMatch.Unambiguous);
        jesus.Evidence.Should().BeNull();
        jesus.Source.Should().Be(MaxfieldAsvEdition.Credit + ", matched within the verse against NESTLE1904");
    }

    [Fact]
    public void The_dropped_particle_reaches_nothing_from_the_word_it_sat_on() =>
        Rows().Should().NotContain(row => row.From.Contains("42.4:12.8"), "Luke 4:12's God carries no number once ὅτι's goes");

    /// <summary>Luke 4:13 in this copy writes θεός twice, and nothing says which one <em>God</em> renders (NOT-0228).</summary>
    [Fact]
    public void A_number_naming_two_Greek_words_of_the_verse_is_left_out()
    {
        var rows = Rows();

        rows.Should().NotContain(row => row.From == "42.4:13.1");
        rows.Single(row => row.From == "42.4:13.2").To.Should().Be("42.4:13.2");
    }

    [Fact]
    public void A_function_word_s_number_only_supports_and_a_content_word_s_does_not()
    {
        var rows = Rows();

        rows.Single(row => row.From == "42.4:12.3").Evidence.Should().Be(MappingMethods.SupportOnly, "that");
        rows.Single(row => row.From == "42.4:12.7").Evidence.Should().Be(MappingMethods.SupportOnly, "thy");
        rows.Single(row => row.From == "42.4:12.6").Evidence.Should().BeNull("Lord");
    }

    /// <summary>
    /// Luke 11:21, <em>the strong man</em>: the ASV prints <em>man</em> in italics, and the file gives it ἰσχυρός's number. The
    /// row only supports another method's link (owner, 2026-11-19), while <em>court</em> beside it is a link of its own.
    /// </summary>
    [Fact]
    public void A_number_on_a_word_the_ASV_prints_as_supplied_only_supports()
    {
        var rows = Rows();

        var man = rows.Single(row => row.From == "42.11:21.4");
        man.To.Should().Be("42.11:21.3");
        man.Evidence.Should().Be(MappingMethods.SupportOnlySupplied);
        rows.Single(row => row.From == "42.11:21.8").Evidence.Should().BeNull("court");
        MappingMethods.OnlySupports(man.Evidence).Should().BeTrue("the merge withdraws it where no other method proposes its shape");
    }

    /// <summary>
    /// Philippians 2:4, <em>his own things</em>: the file puts ἑαυτῶν's number on <em>things</em>, a noun the translator added
    /// to the pronoun, so the row only supports. In Acts 5:32 <em>things</em> renders ῥημάτων, a noun, and in John 9:25 <em>one
    /// thing</em> renders ἕν, the numeral; both stay links of their own.
    /// </summary>
    [Fact]
    public void A_number_on_an_added_noun_only_supports_unless_the_Greek_word_is_a_noun_or_the_numeral_one()
    {
        var rows = Rows();

        var things = rows.Single(row => row.From == "50.2:4.9");
        things.To.Should().Be("50.2:4.3");
        things.Evidence.Should().Be(MappingMethods.SupportOnlyAdded);
        var words = rows.Single(row => row.From == "44.5:32.4");
        words.To.Should().Be("44.5:32.3");
        words.Evidence.Should().BeNull();
        var one = rows.Single(row => row.From == "43.9:25.1 43.9:25.2");
        one.To.Should().Be("43.9:25.1");
        one.Evidence.Should().BeNull();
    }

    /// <summary>
    /// Matthew 9:35 <em>all manner of disease</em> puts πᾶσαν's number on <em>manner</em>, Mark 12:24 <em>for this cause</em>
    /// τοῦτο's on <em>cause</em>: both only support. In Acts 28:18 <em>no cause of death</em>, <em>cause</em> renders αἰτίαν, a
    /// noun, and stays a link of its own (coordinator, 2026-11-19).
    /// </summary>
    [Fact]
    public void Manner_and_cause_only_support_over_a_word_that_is_not_a_noun()
    {
        var rows = Rows();

        var manner = rows.Single(row => row.From == "40.9:35.2");
        manner.To.Should().Be("40.9:35.1");
        manner.Evidence.Should().Be(MappingMethods.SupportOnlyAdded);
        var cause = rows.Single(row => row.From == "41.12:24.3");
        cause.To.Should().Be("41.12:24.2");
        cause.Evidence.Should().Be(MappingMethods.SupportOnlyAdded);
        var reason = rows.Single(row => row.From == "44.28:18.2");
        reason.To.Should().Be("44.28:18.2");
        reason.Evidence.Should().BeNull();
    }

    [Theory]
    [InlineData("things", true)]
    [InlineData("One", true)]
    [InlineData("man’s", true)]
    [InlineData("men's", true)]
    [InlineData("ones’", true)]
    [InlineData("manner", true)]
    [InlineData("Cause", true)]
    [InlineData("mankind", false)]
    public void The_added_nouns_are_read_as_printed(string word, bool added) => EnglishAddedNouns.Contains(word).Should().Be(added);

    /// <summary>
    /// Isaiah 32:19, <em>utterly laid low</em>: the King James's numbering carried onto the ASV puts תִּשְׁפַּל's number on
    /// <em>utterly</em> as well as <em>low</em>, though <em>utterly</em> renders בַּשִּׁפְלָה. One Hebrew word's number on several
    /// phrases only supports. In the New Testament the same shape is a phrase the English splits, Matthew 14:22 <em>send …
    /// away</em> for ἀπολύσῃ, and stays a link of its own.
    /// </summary>
    [Fact]
    public void One_Hebrew_word_s_number_on_several_phrases_only_supports_and_a_Greek_word_s_does_not()
    {
        var hebrew = Rows(Asv + "-" + BhsaTextSource.Slug);
        var low = hebrew.Single(row => row.From == "23.32:19.6 23.32:19.8");
        low.To.Should().Be("23.32:19.4");
        low.Evidence.Should().Be(MappingMethods.SupportOnlyPhrases);
        hebrew.Single(row => row.From == "23.32:19.3").Evidence.Should().BeNull("city");

        var send = Rows().Single(row => row.From == "40.14:22.4 40.14:22.7");
        send.To.Should().Be("40.14:22.3");
        send.Evidence.Should().BeNull();
    }

    private List<MappingRow> Rows(string pair = Pair)
    {
        Text(Asv, "eng", "translation",
            [
                ("23.32:19", [("and", null), ("the", null), ("city", "H5892"), ("shall", null), ("be", null), ("utterly", "H8213"), ("laid", null),
                    ("low", "H8213")]),
                ("40.9:35", [("all", null), ("manner", "G3956"), ("of", null), ("disease", "G3554")]),
                ("40.14:22", [("till", "G2193"), ("he", "G3739"), ("should", null), ("send", "G630"), ("the", null), ("multitudes", "G3793"),
                    ("away", "G630")]),
                ("42.4:12", [("Jesus", "G2424"), ("said", "G2036"), ("that", "G3754"), ("but", "G1161"), ("the", null), ("Lord", "G2962"),
                    ("thy", "G4675"), ("God", null)]),
                ("42.4:13", [("God", "G2316"), ("spake", "G2980")]),
                ("42.11:21", [("When", "G3752"), ("the", null), ("strong", null), new Word("man", "G2478", Supplied: 1), ("armed", "G2528"),
                    ("guardeth", "G5442"), ("his", "G1438"), ("court", "G833")]),
                ("41.12:24", [("for", null), ("this", null), ("cause", "G3778"), ("ye", null), ("err", "G4105")]),
                ("43.9:25", [("one", "G1520"), ("thing", "G1520"), ("I", null), ("know", "G1492")]),
                ("44.5:32", [("witnesses", "G3144"), ("of", null), ("these", null), ("things", "G4487")]),
                ("44.28:18", [("no", null), ("cause", "G156"), ("of", null), ("death", "G2288")]),
                ("50.2:4", [("not", "G3361"), ("looking", "G4648"), ("each", "G1538"), ("of", null), ("you", null), ("to", null), ("his", null),
                    ("own", null), ("things", "G1438")]),
            ]);
        Text(NestleTextSource.Slug, "grc", "critical-edition",
            [
                ("40.9:35", [new Word("πᾶσαν", "G3956", "adj"), new Word("νόσον", "G3554", "noun")]),
                ("40.14:22", [("ἕως", "G2193"), ("οὗ", "G3739"), new Word("ἀπολύσῃ", "G630", "verb"), ("τοὺς", "G3588"), new Word("ὄχλους", "G3793", "noun")]),
                ("42.4:12", [("Ἰησοῦς", "G2424"), ("εἶπεν", "G2036"), ("αὐτῷ", "G846"), ("ὅτι", "G3754"), ("δέ", "G1161"), ("κύριον", "G2962"),
                    ("τὸν", "G3588"), ("θεόν", "G2316"), ("σου", "G4675")]),
                ("42.4:13", [("θεός", "G2316"), ("ἐλάλησεν", "G2980"), ("θεοῦ", "G2316")]),
                ("42.11:21", [("ὅταν", "G3752"), ("ὁ", "G3588"), new Word("ἰσχυρὸς", "G2478", "adj"), new Word("καθωπλισμένος", "G2528", "verb"),
                    new Word("φυλάσσῃ", "G5442", "verb"), ("τὴν", "G3588"), new Word("ἑαυτοῦ", "G1438", "pron"), new Word("αὐλήν", "G833", "noun")]),
                ("41.12:24", [new Word("διὰ", "G1223", "prep"), new Word("τοῦτο", "G3778", "pron"), new Word("πλανᾶσθε", "G4105", "verb")]),
                ("43.9:25", [new Word("ἓν", "G1520", "adj"), new Word("οἶδα", "G1492", "verb")]),
                ("44.5:32", [new Word("μάρτυρες", "G3144", "noun"), ("τῶν", "G3588"), new Word("ῥημάτων", "G4487", "noun"), new Word("τούτων", "G3778", "pron")]),
                ("44.28:18", [new Word("μηδεμίαν", "G3367", "adj"), new Word("αἰτίαν", "G156", "noun"), new Word("θανάτου", "G2288", "noun")]),
                ("50.2:4", [("μὴ", "G3361"), ("τὰ", "G3588"), new Word("ἑαυτῶν", "G1438", "pron"), new Word("ἕκαστοι", "G1538", "adj"),
                    new Word("σκοποῦντες", "G4648", "verb")]),
            ]);
        Text(BhsaTextSource.Slug, "hbo", "critical-edition",
            [
                ("23.32:19", [("וּ", "H9000"), ("בַ", "H9003"), new Word("שִּׁפְלָה", "H8218", "subs"), new Word("תִּשְׁפַּל", "H8213", "verb"), ("הָ", "H9009"),
                    new Word("עִיר", "H5892", "subs")]),
            ]);
        Directory.CreateDirectory(Path.Combine(_root, "Strong"));
        File.WriteAllText(Path.Combine(_root, StrongNumberSource.GreekDictionary), "<strongsdictionary></strongsdictionary>");
        return [.. StrongNumberSource.All.Single(source => source.States(pair)).Rows(new ProductTexts(_store), pair, _ => { })];
    }

    /// <summary>A word of a test text: its number, the part of speech its morphology names, and the supplied run it stands in.</summary>
    private sealed record Word(string Surface, string? Strong, string? Pos = null, int? Supplied = null)
    {
        public static implicit operator Word((string Surface, string? Strong) word) => new(word.Surface, word.Strong);
    }

    private void Text(string slug, string language, string kind, (string Verse, Word[] Words)[] verses)
    {
        using (var declared = _store.Create(TextField.ProductOf(slug), ProductKind.Hand, TextField.Schema, "test", []))
        {
            declared.WriteAll([new TextField("slug", slug), new TextField("kind", kind), new TextField("language", language),
                new TextField("versification", "kjv")]);
        }

        using (var writer = _store.Create(TextWord.ProductOf(slug), ProductKind.Machine, TextWord.Schema(slug), "test", []))
        {
            writer.WriteAll(verses.OrderBy(verse => VerseKey.Parse(verse.Verse)).SelectMany(verse => verse.Words.Select((word, n) => new TextWord(
                new WordKey(slug, VerseKey.Parse(verse.Verse), n + 1), word.Surface, " ", null, word.Strong, null,
                word.Pos is null ? null : $"{{\"pos\": \"{word.Pos}\"}}", null, null, false, null, word.Supplied, null))));
        }

        using var frames = _store.Create(FrameFile.Product(slug), ProductKind.Machine, FrameRow.Schema, "test", []);
        frames.WriteAll(verses.Select(verse => new FrameRow(VerseKey.Parse(verse.Verse), null, [VerseKey.Parse(verse.Verse)], "test")));
    }

    /// <summary>A copy of the file's layout: every book's table, the rows given, and the <c>copyright</c> table.</summary>
    private static void Edition(string path, string terms, params (string Book, (int Chapter, int Verse, string Word, string Language, int Number)[] Rows)[] books)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        void Run(string sql, params (string Name, object Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            command.ExecuteNonQuery();
        }

        Run("CREATE TABLE copyright(CopyrightInfo text)");
        Run("INSERT INTO copyright VALUES ($terms)", ("$terms", terms));
        foreach (var book in Books)
        {
            Run($"CREATE TABLE \"{book}\" (Chapter integer KEY,Verse integer KEY,WordSeqNumber integer,Word varchar,pre varchar,post varchar,"
                + "WordFormat integer, OrigWordLanguage char(1), DictEntryNumber integer)");
        }

        foreach (var (book, rows) in books)
        {
            for (var at = 0; at < rows.Length; at++)
            {
                var row = rows[at];
                Run($"INSERT INTO \"{book}\" VALUES ($c, $v, $s, $w, '', '', 0, $l, $n)",
                    ("$c", row.Chapter), ("$v", row.Verse), ("$s", at), ("$w", row.Word), ("$l", row.Language), ("$n", row.Number));
            }
        }

        transaction.Commit();
    }
}
