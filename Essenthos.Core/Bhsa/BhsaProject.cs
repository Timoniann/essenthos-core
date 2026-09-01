using Essenthos.Core.Bhsa.Attributes;
using Essenthos.Core.Bhsa.Core;
using Essenthos.Core.TextFabric;

namespace Essenthos.Core.Bhsa;

public class BhsaProject
{
    public IReadOnlyList<Word> Words { get; init; }

    public IReadOnlyList<Book> Books { get; init; }

    public IReadOnlyList<Chapter> Chapters { get; init; }

    public IReadOnlyList<Verse> Verses { get; init; }

    public IReadOnlyList<Clause> Clauses { get; init; }

    public IReadOnlyList<ClauseAtom> ClauseAtoms { get; init; }

    public IReadOnlyList<HalfVerse> HalfVerses { get; init; }

    public IReadOnlyList<Phrase> Phrases { get; init; }

    public IReadOnlyList<PhraseAtom> PhraseAtoms { get; init; }

    public IReadOnlyList<Sentence> Sentences { get; init; }

    public IReadOnlyList<SentenceAtom> SentenceAtoms { get; init; }

    public IReadOnlyList<Subphrase> Subphrases { get; init; }

    public IReadOnlyList<Lexeme> Lexemes { get; init; }

    public static BhsaProject Load(string path)
    {
        var tfProject = Project.Load(path);
        var wordRange = tfProject.NodeTypeRanges["word"];
        var bookRange = tfProject.NodeTypeRanges["book"];
        var chapterRange = tfProject.NodeTypeRanges["chapter"];
        var verseRange = tfProject.NodeTypeRanges["verse"];
        var clauseRange = tfProject.NodeTypeRanges["clause"];
        var clauseAtomRange = tfProject.NodeTypeRanges["clause_atom"];
        var halfVerseRange = tfProject.NodeTypeRanges["half_verse"];
        var phraseRange = tfProject.NodeTypeRanges["phrase"];
        var phraseAtomRange = tfProject.NodeTypeRanges["phrase_atom"];
        var sentenceRange = tfProject.NodeTypeRanges["sentence"];
        var sentenceAtomRange = tfProject.NodeTypeRanges["sentence_atom"];
        var subphraseRange = tfProject.NodeTypeRanges["subphrase"];
        var lexRange = tfProject.NodeTypeRanges["lex"];

        var accessedKeys = new HashSet<string>();

        IDocument<T> GetDocument<T>(string name)
        {
            accessedKeys.Add(name);
            if (!tfProject.Documents.TryGetValue(name, out var document))
            {
                throw new KeyNotFoundException($"Document '{name}' not found in the project.");
            }

            if (document is IDocument<T> typedDocument)
            {
                return typedDocument;
            }

            throw new InvalidOperationException($"Document '{name}' is not of type '{typeof(T).Name}'.");
        }

        var wordUtf8 = GetDocument<string>("g_word_utf8");
        var gloss = GetDocument<string>("gloss");
        var lexemeUtf8 = GetDocument<string>("g_lex_utf8");
        var consonantalUtf8 = GetDocument<string>("g_cons_utf8");
        var qereUtf8 = GetDocument<string>("qere_utf8");
        var qereTrailerUtf8 = GetDocument<string>("qere_trailer_utf8");
        var bookNames = GetDocument<string>("book");
        var chapterNumbers = GetDocument<int>("chapter");
        var verseNumbers = GetDocument<int>("verse");

        var vocLexUtf8 = GetDocument<string>("voc_lex_utf8");
        var nametype = GetDocument<string>("nametype");

        var type = GetDocument<string>("typ");
        var kind = GetDocument<string>("kind");
        var rela = GetDocument<string>("rela");
        var code = GetDocument<int>("code");
        var isRoot = GetDocument<string>("is_root");
        var tab = GetDocument<int>("tab");
        var pargr = GetDocument<string>("pargr");
        var instruction = GetDocument<string>("instruction");
        var domain = GetDocument<string>("domain");
        var txt = GetDocument<string>("txt");
        var function = GetDocument<string>("function");
        var det = GetDocument<string>("det");
        var trailer = GetDocument<string>("trailer_utf8");
        var languageIso = GetDocument<string>("languageISO");
        var sp = GetDocument<string>("sp");
        var pdp = GetDocument<string>("pdp");
        var ls = GetDocument<string>("ls");
        var gn = GetDocument<string>("gn");
        var prsGn = GetDocument<string>("prs_gn");
        var nu = GetDocument<string>("nu");
        var prsNu = GetDocument<string>("prs_nu");
        var ps = GetDocument<string>("ps");
        var prsPs = GetDocument<string>("prs_ps");
        var st = GetDocument<string>("st");
        var vs = GetDocument<string>("vs");
        var vt = GetDocument<string>("vt");
        var phono = GetDocument<string>("phono");
        var phonoTrailer = GetDocument<string>("phono_trailer");
        var label = GetDocument<string>("label");

        var wordCount = wordRange.End - wordRange.Start + 1;
        var books = new List<Book>(bookRange.End - bookRange.Start + 1);
        var chapters = new List<Chapter>(chapterRange.End - chapterRange.Start + 1);
        var verses = new List<Verse>(verseRange.End - verseRange.Start + 1);
        var words = new List<Word>(wordCount);
        var clauses = new List<Clause>(clauseRange.End - clauseRange.Start + 1);
        var clauseAtoms = new List<ClauseAtom>(clauseAtomRange.End - clauseAtomRange.Start + 1);
        var halfVerses = new List<HalfVerse>(halfVerseRange.End - halfVerseRange.Start + 1);
        var phrases = new List<Phrase>(phraseRange.End - phraseRange.Start + 1);
        var phraseAtoms = new List<PhraseAtom>(phraseAtomRange.End - phraseAtomRange.Start + 1);
        var sentences = new List<Sentence>(sentenceRange.End - sentenceRange.Start + 1);
        var sentenceAtoms = new List<SentenceAtom>(sentenceAtomRange.End - sentenceAtomRange.Start + 1);
        var subphrases = new List<Subphrase>(subphraseRange.End - subphraseRange.Start + 1);
        var lexemes = new List<Lexeme>(lexRange.End - lexRange.Start + 1);

        var wordData = new WordData[wordCount + wordRange.Start];
        for (int i = wordRange.Start, end = wordRange.End; i <= end; i++)
        {
            wordData[i] = new WordData();
        }


        List<Action> actions =
        [
            () =>
            {
                for (var i = bookRange.Start; i <= bookRange.End; i++)
                {
                    var wordSlotRanges = tfProject.ObjectSlotsMap[i];
                    var name = bookNames[i];
                    var book = new Book(SlotId: i, Ordinal: i - bookRange.Start + 1, Name: name, Chapters: []);
                    books.Add(book);
                    for (int index = 0, count = wordSlotRanges.Count; index < count; index++)
                    {
                        var slot = wordSlotRanges[index];
                        for (int j = slot.Start, end = slot.End; j <= end; j++)
                        {
                            wordData[j].Book = book;
                        }
                    }
                }

                for (var i = chapterRange.Start; i <= chapterRange.End; i++)
                {
                    var wordSlotRanges = tfProject.ObjectSlotsMap[i];
                    var wordSlotId = wordSlotRanges[0].Start;
                    var book = wordData[wordSlotId].Book!;
                    var number = chapterNumbers[i];
                    var chapter = new Chapter(i, number, book, []);
                    chapters.Add(chapter);
                    book.Chapters.Add(chapter);
                    for (int index = 0, count = wordSlotRanges.Count; index < count; index++)
                    {
                        var slot = wordSlotRanges[index];
                        for (int j = slot.Start, end = slot.End; j <= end; j++)
                        {
                            wordData[j].Chapter = chapter;
                        }
                    }
                }

                for (var i = verseRange.Start; i <= verseRange.End; i++)
                {
                    var wordSlotRanges = tfProject.ObjectSlotsMap[i];
                    var wordSlotId = wordSlotRanges[0].Start;
                    var chapter = wordData[wordSlotId].Chapter!;
                    var number = verseNumbers[i];
                    var verse = new Verse(i, number, chapter, []);
                    verses.Add(verse);
                    chapter.Verses.Add(verse);
                    for (int index = 0, count = wordSlotRanges.Count; index < count; index++)
                    {
                        var slot = wordSlotRanges[index];
                        for (int j = slot.Start, end = slot.End; j <= end; j++)
                        {
                            wordData[j].Verse = verse;
                        }
                    }
                }
            },
            () =>
            {
                var clauseIndex = 0;
                for (var i = clauseRange.Start; i <= clauseRange.End; i++)
                {
                    var wordSlotRanges = tfProject.ObjectSlotsMap[i];
                    var clauseKind = ClauseKind.Parse(kind[i]);
                    var linguisticRelation = ClauseLinguisticRelation.Parse(rela[i]);
                    var domainValue = ClauseDomain.Parse(domain[i]);
                    var textTypesStr = txt[i];
                    var textTypes = new ClauseTextType[textTypesStr.Length];
                    for (int j = 0, len = textTypesStr.Length; j < len; j++)
                    {
                        textTypes[j] = ClauseTextType.Parse(textTypesStr[j].ToString());
                    }

                    var clause = new Clause(SlotId: i, Ordinal: clauseIndex++, Type: type[i], Kind: clauseKind,
                        LinguisticRelation: linguisticRelation,
                        Domain: domainValue, TextTypes: textTypes, Words: []);
                    clauses.Add(clause);
                    for (int index = 0, count = wordSlotRanges.Count; index < count; index++)
                    {
                        var slot = wordSlotRanges[index];
                        for (int j = slot.Start, end = slot.End; j <= end; j++)
                        {
                            wordData[j].Clause = clause;
                        }
                    }
                }
            },

            () =>
            {
                var clauseAtomIndex = 0;
                for (var i = clauseAtomRange.Start; i <= clauseAtomRange.End; i++)
                {
                    var wordSlotRanges = tfProject.ObjectSlotsMap[i];
                    var relation = ClauseAtomRelation.Parse(code[i]);
                    var root = isRoot[i] == "true";
                    var tabValue = tab[i];
                    var paragraph = pargr[i];
                    var instr = instruction[i];
                    var clause = new ClauseAtom(i, clauseAtomIndex++, Type: type[i], Relation: relation, IsRoot: root,
                        Tab: tabValue, Paragraph: paragraph, Instruction: instr, Words: []);
                    clauseAtoms.Add(clause);
                    for (int index = 0, count = wordSlotRanges.Count; index < count; index++)
                    {
                        var slot = wordSlotRanges[index];
                        for (int j = slot.Start, end = slot.End; j <= end; j++)
                        {
                            wordData[j].ClauseAtom = clause;
                        }
                    }
                }
            },

            () =>
            {
                var halfVerseIndex = 0;
                for (var i = halfVerseRange.Start; i <= halfVerseRange.End; i++)
                {
                    var wordSlotRanges = tfProject.ObjectSlotsMap[i];
                    var halfVerse = new HalfVerse(i, halfVerseIndex++, Part: label[i], Words: []);
                    halfVerses.Add(halfVerse);
                    for (int index = 0, count = wordSlotRanges.Count; index < count; index++)
                    {
                        var slot = wordSlotRanges[index];
                        for (int j = slot.Start, end = slot.End; j <= end; j++)
                        {
                            wordData[j].HalfVerse = halfVerse;
                        }
                    }
                }
            },

            () =>
            {
                var phraseIndex = 0;
                for (var i = phraseRange.Start; i <= phraseRange.End; i++)
                {
                    var wordSlotRanges = tfProject.ObjectSlotsMap[i];
                    var phrase = new Phrase(SlotId: i, Ordinal: phraseIndex++, Type: type[i], Function: function[i],
                        Determination: det[i], LinguisticRelation: rela[i], Words: []);
                    phrases.Add(phrase);
                    for (int index = 0, count = wordSlotRanges.Count; index < count; index++)
                    {
                        var slot = wordSlotRanges[index];
                        for (int j = slot.Start, end = slot.End; j <= end; j++)
                        {
                            wordData[j].Phrase = phrase;
                        }
                    }
                }
            },

            () =>
            {
                var phraseAtomIndex = 0;
                for (var i = phraseAtomRange.Start; i <= phraseAtomRange.End; i++)
                {
                    var wordSlotRanges = tfProject.ObjectSlotsMap[i];
                    var phraseAtom = new PhraseAtom(SlotId: i, Ordinal: phraseAtomIndex++, LinguisticRelation: rela[i],
                        Determination: det[i], Words: []);
                    phraseAtoms.Add(phraseAtom);
                    for (int index = 0, count = wordSlotRanges.Count; index < count; index++)
                    {
                        var slot = wordSlotRanges[index];
                        for (int j = slot.Start, end = slot.End; j <= end; j++)
                        {
                            wordData[j].PhraseAtom = phraseAtom;
                        }
                    }
                }
            },

            () =>
            {
                var sentenceIndex = 0;
                for (var i = sentenceRange.Start; i <= sentenceRange.End; i++)
                {
                    var wordSlotRanges = tfProject.ObjectSlotsMap[i];
                    var sentence = new Sentence(i, sentenceIndex++, Words: []);
                    sentences.Add(sentence);
                    for (int index = 0, count = wordSlotRanges.Count; index < count; index++)
                    {
                        var slot = wordSlotRanges[index];
                        for (int j = slot.Start, end = slot.End; j <= end; j++)
                        {
                            wordData[j].Sentence = sentence;
                        }
                    }
                }
            },

            () =>
            {
                var sentenceAtomIndex = 0;
                for (var i = sentenceAtomRange.Start; i <= sentenceAtomRange.End; i++)
                {
                    var wordSlotRanges = tfProject.ObjectSlotsMap[i];
                    var sentenceAtom = new SentenceAtom(i, sentenceAtomIndex++, Words: []);
                    sentenceAtoms.Add(sentenceAtom);
                    for (int index = 0, count = wordSlotRanges.Count; index < count; index++)
                    {
                        var slot = wordSlotRanges[index];
                        for (int j = slot.Start, end = slot.End; j <= end; j++)
                        {
                            wordData[j].SentenceAtom = sentenceAtom;
                        }
                    }
                }
            },

            () =>
            {
                var subphraseIndex = 0;
                for (var i = subphraseRange.Start; i <= subphraseRange.End; i++)
                {
                    var wordSlotRanges = tfProject.ObjectSlotsMap[i];
                    var subphrase = new Subphrase(i, subphraseIndex++, rela[i], Words: []);
                    subphrases.Add(subphrase);
                    for (int index = 0, count = wordSlotRanges.Count; index < count; index++)
                    {
                        var slot = wordSlotRanges[index];
                        for (int j = slot.Start, end = slot.End; j <= end; j++)
                        {
                            wordData[j].Subphrase = subphrase;
                        }
                    }
                }
            },

            () =>
            {
                for (var i = lexRange.Start; i <= lexRange.End; i++)
                {
                    var wordSlotRanges = tfProject.ObjectSlotsMap[i];

                    var lexeme = new Lexeme(i, "", Gloss: gloss[i], VocalizedLexemeUtf8: vocLexUtf8[i],
                        Language: languageIso[i], PartOfSpeech: sp[i],
                        LexicalSet: ls.GetNullable(i), Nametypes: Nametype.ParseMultiple(nametype.GetNullable(i)),
                        Words: []);
                    lexemes.Add(lexeme);
                    for (int index = 0, count = wordSlotRanges.Count; index < count; index++)
                    {
                        var slot = wordSlotRanges[index];
                        for (int j = slot.Start, end = slot.End; j <= end; j++)
                        {
                            wordData[j].Lexeme = lexeme;
                        }
                    }
                }
            }
        ];

        Parallel.Invoke(actions.ToArray());

        Parallel.For(wordRange.Start, wordRange.End + 1, i =>
        {
            var verse = wordData[i].Verse!;
            var clause = wordData[i].Clause!;
            var clauseAtom = wordData[i].ClauseAtom!;
            var halfVerse = wordData[i].HalfVerse!;
            var phrase = wordData[i].Phrase!;
            var phraseAtom = wordData[i].PhraseAtom!;
            var sentence = wordData[i].Sentence!;
            var sentenceAtom = wordData[i].SentenceAtom!;
            var subphrase = wordData[i].Subphrase;
            var lexeme = wordData[i].Lexeme!;
            var word = new Word(i,
                TextUtf8: wordUtf8[i],
                ConsonantalUtf8: consonantalUtf8[i],
                LexemeUtf8: lexemeUtf8[i],
                Trailer: trailer[i],
                Qere: qereUtf8.GetNullable(i),
                QereTrailer: qereTrailerUtf8.GetNullable(i),
                Gloss: gloss.GetNullable(i),
                VocalizedLexemeUtf8: vocLexUtf8[i],
                PhonologicalTranscription: phono[i],
                PhonologicalTrailer: phonoTrailer[i],
                Language: languageIso[i],
                Verse: verse,
                Lexeme: lexeme,
                HalfVerse: halfVerse,
                PartOfSpeech: sp[i],
                Nametypes: Nametype.ParseMultiple(nametype.GetNullable(i)),
                PhraseDependentPartOfSpeech: pdp[i],
                LexicalSet: ls[i]!,
                Clause: clause,
                ClauseAtom: clauseAtom,
                Phrase: phrase,
                PhraseAtom: phraseAtom,
                Sentence: sentence,
                SentenceAtom: sentenceAtom,
                Subphrase: subphrase,
                Gender: gn[i],
                PronominalSuffixGender: prsGn[i],
                WordNumberClass: nu[i],
                PronominalWordNumberClass: prsNu[i],
                WordPersonClass: ps[i],
                PronominalWordPersonClass: prsPs[i],
                NounState: st[i],
                VerbalStem: vs[i],
                VerbalTense: vt[i]
            );
            wordData[i].Word = word;
        });

        for (int i = wordRange.Start, end = wordRange.End; i <= end; i++)
        {
            var data = wordData[i];
            var word = data.Word!;
            data.Verse!.Words.Add(word);
            data.HalfVerse!.Words.Add(word);
            data.Clause!.Words.Add(word);
            data.ClauseAtom?.Words.Add(word);
            data.Phrase!.Words.Add(word);
            data.PhraseAtom!.Words.Add(word);
            data.Sentence!.Words.Add(word);
            data.SentenceAtom!.Words.Add(word);
            data.Subphrase?.Words.Add(word);
            data.Lexeme!.Words.Add(word);
            words.Add(word);
        }

        //     verse.Words.Add(word);
        //     halfVerse.Words.Add(word);
        //     clause?.Words.Add(word);
        //     clauseAtom?.Words.Add(word);
        //     phrase?.Words.Add(word);
        //     phraseAtom?.Words.Add(word);
        //     sentence?.Words.Add(word);
        //     sentenceAtom?.Words.Add(word);
        //     subphrase?.Words.Add(word);
        //     lexeme.Words.Add(word);
        //     words.Add(word);


        // for (int i = wordRange.Start, end = wordRange.End; i <= end; i++)
        // {
        //     var verse = wordData[i].Verse!;
        //     var clause = wordData[i].Clause;
        //     var clauseAtom = wordData[i].ClauseAtom;
        //     var halfVerse = wordData[i].HalfVerse!;
        //     var phrase = wordData[i].Phrase;
        //     var phraseAtom = wordData[i].PhraseAtom;
        //     var sentence = wordData[i].Sentence;
        //     var sentenceAtom = wordData[i].SentenceAtom;
        //     var subphrase = wordData[i].Subphrase;
        //     var lexeme = wordData[i].Lexeme!;
        //     var word = new Word(i,
        //         TextUtf8: wordUtf8[i],
        //         LexemeUtf8: lexemeUtf8[i],
        //         Trailer: trailer[i],
        //         Gloss: gloss.GetNullable(i),
        //         Language: languageIso[i],
        //         Verse: verse,
        //         Lexeme: lexeme,
        //         HalfVerse: halfVerse,
        //         PartOfSpeech: sp[i],
        //         Nametypes: Nametype.ParseMultiple(nametype.GetNullable(i)),
        //         PhraseDependentPartOfSpeech: pdp[i],
        //         LexicalSet: ls[i],
        //         Clause: clause,
        //         ClauseAtom: clauseAtom,
        //         Phrase: phrase,
        //         PhraseAtom: phraseAtom,
        //         Sentence: sentence,
        //         SentenceAtom: sentenceAtom,
        //         Subphrase: subphrase,
        //         Gender: gn[i],
        //         PronominalSuffixGender: prsGn[i],
        //         WordNumberClass: nu[i],
        //         PronominalWordNumberClass: prsNu[i],
        //         WordPersonClass: ps[i],
        //         PronominalWordPersonClass: prsPs[i],
        //         NounState: st[i],
        //         VerbalStem: vs[i],
        //         VerbalTense: vt[i]
        //     );
        //     verse.Words.Add(word);
        //     halfVerse.Words.Add(word);
        //     clause?.Words.Add(word);
        //     clauseAtom?.Words.Add(word);
        //     phrase?.Words.Add(word);
        //     phraseAtom?.Words.Add(word);
        //     sentence?.Words.Add(word);
        //     sentenceAtom?.Words.Add(word);
        //     subphrase?.Words.Add(word);
        //     lexeme.Words.Add(word);
        //     words.Add(word);
        // }
        accessedKeys.Add("otext");
        accessedKeys.Add("otype");
        accessedKeys.Add("oslots");
        accessedKeys.Add("book@ur");
        accessedKeys.Add("book@tr");
        accessedKeys.Add("book@de");
        accessedKeys.Add("book@es");
        accessedKeys.Add("book@id");
        accessedKeys.Add("book@pa");
        accessedKeys.Add("book@am");
        accessedKeys.Add("book@syc");
        accessedKeys.Add("book@fa");
        accessedKeys.Add("book@la");
        accessedKeys.Add("book@ru");
        accessedKeys.Add("book@da");
        accessedKeys.Add("book@zh");
        accessedKeys.Add("book@ar");
        accessedKeys.Add("book@bn");
        accessedKeys.Add("book@nl");
        accessedKeys.Add("book@yo");
        accessedKeys.Add("book@ja");
        accessedKeys.Add("book@en");
        accessedKeys.Add("book@he");
        accessedKeys.Add("book@hi");
        accessedKeys.Add("book@sw");
        accessedKeys.Add("book@pt");
        accessedKeys.Add("book@fr");
        accessedKeys.Add("book@ko");
        accessedKeys.Add("book@el");
        accessedKeys.Add("language");
        accessedKeys.Add("g_cons");
        accessedKeys.Add("g_lex");
        accessedKeys.Add("g_word");
        accessedKeys.Add("freq_occ");
        accessedKeys.Add("freq_lex");
        accessedKeys.Add("qere");
        accessedKeys.Add("qere_trailer");
        // accessedKeys.Add("");
        var unusedKeys = tfProject.Documents.Keys.Except(accessedKeys);
        Console.WriteLine($"Unused documents in the project: {string.Join(", ", unusedKeys)}");

        return new BhsaProject
        {
            Words = words,
            Books = books,
            Lexemes = lexemes,
            Chapters = chapters,
            Verses = verses,
            Clauses = clauses,
            ClauseAtoms = clauseAtoms,
            HalfVerses = halfVerses,
            Phrases = phrases,
            PhraseAtoms = phraseAtoms,
            Sentences = sentences,
            SentenceAtoms = sentenceAtoms,
            Subphrases = subphrases,
        };
    }

    private class WordData
    {
        public ClauseAtom? ClauseAtom { get; set; }

        public Clause? Clause { get; set; }

        public Book? Book { get; set; }

        public Chapter? Chapter { get; set; }

        public Verse? Verse { get; set; }

        public HalfVerse? HalfVerse { get; set; }

        public Phrase? Phrase { get; set; }

        public PhraseAtom? PhraseAtom { get; set; }

        public Sentence? Sentence { get; set; }

        public SentenceAtom? SentenceAtom { get; set; }

        public Subphrase? Subphrase { get; set; }

        public Lexeme? Lexeme { get; set; }

        public Word? Word { get; set; }
    }
}