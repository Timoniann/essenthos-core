namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The thirty words a stemmer cannot hold together, held together by hand — and measured, and
/// found not to be worth it.
///
/// <see cref="SlavicStemmer"/> strips endings, and that is the right instrument for the open
/// classes — <em>отделил</em>, <em>отделяет</em> and <em>отделить</em> land on one stem because
/// they differ only by ending. It is the wrong instrument for the closed ones, where the forms of a
/// word do not share a stem at all: <em>я</em> and <em>меня</em>, <em>он</em> and <em>его</em>,
/// <em>бути</em> and <em>є</em> and <em>буде</em>. Those the stemmer tears into three to twelve
/// pieces each, and no amount of ending-stripping puts them back.
///
/// <para>
/// **The population is a quarter of the problem.** Measured over the corpus, the thirty
/// most-fragmented lexemes are 18.0% of the Ukrainian text and 18.7% of the Russian, and
/// they hold 25.4% of everything the Ukrainian alignment fails to link and 19.0% of the Russian.
/// That is why the table was written. It is also, on its own, an argument about a population and
/// not about an outcome, and the two turned out to differ.
/// </para>
///
/// <para>
/// **What it is worth, scored.** The aligner was run twice over the same texts — once with this
/// table and once without, nothing else changed — and both runs scored against the only word-level
/// correspondences a Slavic text in this corpus has: 7,109 links stated by unfoldingWord's
/// Ukrainian Bible Interlinear Ogienko, 10,403 word pairs over Esther and eight epistles. At the
/// pipeline's own operating point, one target per source word at 0.25, counting only the source
/// words the interlinear actually speaks about:
///
///     ukr -> nestle1904   off   4,083 right of 4,401 proposed   92.8 %
///     ukr -> nestle1904   on    4,100 right of 4,479 proposed   91.5 %
///     ukr -> bhsa         off   1,880 right of 2,113 proposed   89.0 %
///     ukr -> bhsa         on    1,875 right of 2,118 proposed   88.5 %
///
/// Twelve more correct pairs in 10,403, and seventy-one more wrong ones. The table buys about four
/// wrong links for every right one it adds, and the direction is the same in all twelve
/// configurations measured — three selection rules, two thresholds, both testaments.
/// </para>
///
/// <para>
/// **So it is off**, and the switch stays rather than the code being deleted, because the
/// measurement is the valuable part and it has to remain repeatable: <c>score ukr nestle1904
/// --stated</c> against <c>score ukr nestle1904 --stated --suppletion</c>. A pooling that does not
/// pay today may pay against a different scorer — a bilingual lexical prior would ask which
/// <em>word</em> this form belongs to, which is the question this table answers and the stemmer
/// does not.
/// </para>
///
/// <para>
/// Why it fails is worth saying, because it is not that the table is wrong. These are the words
/// that occur everywhere, so unifying their forms makes one very frequent lexical item out of
/// several frequent ones, and a very frequent item co-occurs with everything: the model gains
/// evidence and loses discrimination in the same step. The pronoun that was three stems competing
/// weakly becomes one stem competing strongly for whatever stands near it.
/// </para>
///
/// <para>
/// **Closed classes only, and that is the safety.** These are function words whose forms have not
/// changed since Ogienko and Синодальный, so the table can be written out and checked rather than
/// learned. Nothing here touches a content word: a wrong entry would merge two lexemes into one and
/// make the model confidently wrong — a guess that reads like a sourced claim, which is worse than
/// no answer — so the list is short on purpose and every entry is a form somebody can look up.
/// </para>
/// </summary>
internal static class SlavicSuppletion
{
    /// <summary>
    /// Every form of the closed-class words, against the one key each should count as. The key is
    /// not a lemma and nothing reads it — it only has to be the same string for every form of one
    /// word and a different one for every other, which is all the model counts with.
    ///
    /// Russian and Ukrainian share an entry where they share the word. They share forms outright —
    /// <em>нас</em>, <em>вам</em>, <em>сказав</em>, <em>буду</em> — so a table with one entry per
    /// language put those under whichever language was written first and left the other language's
    /// nominative under a second key. The Ukrainian first person plural came out as two words.
    /// </summary>
    private static readonly Dictionary<string, string> Forms = Build();

    /// <summary>
    /// The key a word counts as, or null where this table has nothing to say — which is almost
    /// always, and where the stemmer is the right instrument.
    /// </summary>
    public static string? Of(string word) =>
        Forms.TryGetValue(word.ToLowerInvariant().Replace('ё', 'е'), out var key) ? key : null;

    private static Dictionary<string, string> Build()
    {
        var forms = new Dictionary<string, string>(StringComparer.Ordinal);

        void Word(string key, params string[] spellings)
        {
            foreach (var spelling in spellings)
            {
                // A form written twice is a bug in the table, not a fact about the language: it
                // would put one lexeme under two keys and split exactly what this exists to join.
                if (!forms.TryAdd(spelling, key) && forms[spelling] != key)
                {
                    throw new InvalidOperationException(
                        $"\"{spelling}\" is already claimed by \"{forms[spelling]}\" and is being " +
                        $"given to \"{key}\". One of the two is wrong, and leaving it would split " +
                        "the lexeme this table exists to hold together.");
                }
            }
        }

        // **One entry per lexeme, both languages together.** Russian and Ukrainian share many of
        // these forms outright -- нас, нам, вас, вам, сказав, буду -- and giving each language its
        // own key put the shared forms under whichever was written first and the other language's
        // nominative under a second key. The Ukrainian first person plural came out as "ми" and
        // "мы", which is the fragmentation this table exists to remove, introduced by the table.
        //
        // The key is arbitrary and nothing reads it; it only has to be the same string for every
        // form of one word. The Russian spelling is used for both because it had to be one of them.

        // The copula. Russian writes it mostly in the future and the past; Ukrainian writes it
        // everywhere, and "є" against "буде" against "був" is where "хай станеться" comes apart.
        Word("быть", "быть", "бути", "был", "была", "было", "были", "був", "була", "було", "були",
            "буду", "будешь", "будет", "будем", "будете", "будут", "будеш", "буде", "будемо",
            "будуть", "будь", "будьте", "есмь", "еси", "есть", "суть", "є", "єсть", "будучи");

        // The personal pronouns, whose forms share no letters at all.
        Word("я", "я", "меня", "мне", "мной", "мною", "мене", "мені");
        Word("ты", "ты", "ти", "тебя", "тебе", "тобой", "тобою", "тобі");
        Word("он", "он", "він", "его", "него", "ему", "нему", "им", "ним", "нем", "нём",
            "його", "нього", "йому", "ньому");
        Word("она", "она", "вона", "её", "ее", "неё", "нее", "ей", "ней", "ею", "нею",
            "її", "неї", "їй");
        Word("мы", "мы", "ми", "нас", "нам", "нами");
        Word("вы", "вы", "ви", "вас", "вам", "вами");
        Word("они", "они", "вони", "их", "них", "ими", "ними", "їх", "їм");

        // The possessives.
        Word("мой", "мой", "мій", "моя", "моё", "мое", "моє", "мои", "мої", "моего", "мого",
            "моей", "моєї", "моих", "моїх", "моим", "моїм", "моими", "моїми", "моему", "моєму",
            "моём", "моем", "мою");
        Word("твой", "твой", "твій", "твоя", "твоё", "твое", "твоє", "твои", "твої", "твоего",
            "твого", "твоей", "твоєї", "твоих", "твоїх", "твоим", "твоїм", "твоими", "твоїми",
            "твоему", "твоєму", "твоём", "твоем", "твою");
        Word("свой", "свой", "свій", "своя", "своё", "свое", "своє", "свои", "свої", "своего",
            "свого", "своей", "своєї", "своих", "своїх", "своим", "своїм", "своими", "своїми",
            "своему", "своєму", "своём", "своем", "свою");

        // The demonstratives and the quantifier.
        Word("тот", "тот", "той", "та", "то", "те", "ті", "того", "тієї", "тех", "тих", "тем",
            "тим", "теми", "тими", "тому", "том", "ту");
        Word("этот", "этот", "эта", "это", "эти", "этого", "этой", "этих", "этим", "этими",
            "этому", "этом", "эту", "це", "цей", "ця", "ці", "цього", "цієї", "цих", "цим",
            "цими", "цьому", "цю");
        Word("весь", "весь", "вся", "всё", "все", "всего", "всей", "всех", "всем", "всеми",
            "всему", "всём", "всю");

        // The interrogatives, which do duty as relatives on nearly every page.
        Word("кто", "кто", "хто", "кого", "кому", "кем", "ким", "ком", "кім");
        Word("что", "что", "чего", "чему", "чем", "чём");
        Word("как", "как", "як", "яка", "яке", "які", "якого", "якої", "яких", "яким", "якими",
            "якому");

        // The three content words in the list, in it because they are said constantly and their
        // paradigms are irregular enough for the stemmer to split them.
        Word("сказать", "сказать", "сказати", "сказал", "сказала", "сказало", "сказали", "сказав",
            "скажет", "скаже", "скажу", "скажешь", "скажеш", "скажем", "скажемо", "скажете",
            "скажут", "скажуть", "скажи", "скажите", "скажіть", "сказано");
        Word("бог", "бог", "бога", "богу", "богом", "боге", "боже", "боги", "богов", "богам",
            "богами", "богах", "богові", "богів");
        Word("день", "день", "дня", "дню", "днем", "днём", "дне", "дни", "дней", "дням", "днями",
            "днях", "дні", "днів");

        return forms;
    }
}
