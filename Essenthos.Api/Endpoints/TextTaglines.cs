using Essenthos.Core.Corpus;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// What each text is, in one short sentence, in every language the interface speaks: the line a
/// list of texts shows beside the facts it lines up for comparison. The language, the date, the
/// books and the licence are columns of their own, so a line says what they cannot.
///
/// Keyed and looked up as <see cref="TextSummaries"/> is, which holds the longer account a text's
/// own page leads with.
/// </summary>
internal static class TextTaglines
{
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Written =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["BHSA"] = Say(
                "The Masoretic Hebrew Bible of the Leningrad Codex, with the ETCBC's linguistic annotation of every word.",
                "Масоретська єврейська Біблія за Ленінградським кодексом, з лінгвістичною розміткою ETCBC для кожного слова.",
                "Die masoretische Hebräische Bibel nach dem Codex Leningradensis, mit der sprachlichen Annotation des ETCBC zu jedem Wort.",
                "La Biblia hebrea masorética del Códice de Leningrado, con la anotación lingüística del ETCBC en cada palabra."),
            ["SP"] = Say(
                "The Torah as the Samaritans have handed it down, in Stefan Schorch's critical edition.",
                "Тора в самарянській передачі, за критичним виданням Штефана Шорха.",
                "Die Tora in der Überlieferung der Samaritaner, in Stefan Schorchs kritischer Ausgabe.",
                "La Torá tal como la han transmitido los samaritanos, en la edición crítica de Stefan Schorch."),
            ["GEEZ81"] = Say(
                "The eighty-one-book Bible of the Ethiopian Orthodox Church in Ge'ez, with 1 Enoch and Jubilees.",
                "Біблія Ефіопської православної церкви мовою ґеез: вісімдесят одна книга, серед них 1 Єноха та Ювілеї.",
                "Die Bibel der Äthiopisch-Orthodoxen Kirche in Ge'ez: einundachtzig Bücher, darunter 1 Henoch und die Jubiläen.",
                "La Biblia de la Iglesia ortodoxa etíope en ge'ez: ochenta y un libros, entre ellos 1 Enoc y Jubileos."),
            ["NESTLE1904"] = Say(
                "Nestle's Greek New Testament of 1904, built from Tischendorf, Westcott–Hort and Weymouth, fully parsed.",
                "Грецький Новий Заповіт Нестле 1904 року, складений із Тішендорфа, Весткотта–Горта й Веймута, з повним морфологічним розбором.",
                "Nestles griechisches Neues Testament von 1904, aus Tischendorf, Westcott–Hort und Weymouth gebildet, vollständig morphologisch bestimmt.",
                "El Nuevo Testamento griego de Nestle de 1904, compuesto a partir de Tischendorf, Westcott–Hort y Weymouth, con análisis morfológico completo."),
            ["TISCH"] = Say(
                "Tischendorf's eighth critical edition, which weighed the Codex Sinaiticus he had found.",
                "Восьме критичне видання Тішендорфа, що спирається на знайдений ним Синайський кодекс.",
                "Tischendorfs achte kritische Ausgabe, gestützt auf den von ihm gefundenen Codex Sinaiticus.",
                "La octava edición crítica de Tischendorf, que se apoya en el Códice Sinaítico que él mismo halló."),
            ["WH1881"] = Say(
                "The Westcott–Hort Greek text, which put Vaticanus and Sinaiticus above the Received Text.",
                "Грецький текст Весткотта й Горта, що поставив Ватиканський і Синайський кодекси вище за Textus Receptus.",
                "Der griechische Text von Westcott und Hort, der Vaticanus und Sinaiticus über den Textus receptus stellte.",
                "El texto griego de Westcott y Hort, que antepuso el Vaticano y el Sinaítico al Texto Recibido."),
            ["TR1894"] = Say(
                "Scrivener's reconstruction of the Greek the King James translators followed.",
                "Реконструкція Скрівенера: грецький текст, за яким перекладали Біблію короля Якова.",
                "Scriveners Rekonstruktion des griechischen Textes, dem die Übersetzer der King James folgten.",
                "La reconstrucción de Scrivener del griego que siguieron los traductores de la King James."),
            ["TR1550"] = Say(
                "Stephanus's editio regia of 1550, the Received Text of the English Reformation.",
                "«Королівське видання» Стефана 1550 року — Textus Receptus англійської Реформації.",
                "Die Editio regia des Stephanus von 1550, der Textus receptus der englischen Reformation.",
                "La editio regia de Estéfano de 1550, el Texto Recibido de la Reforma inglesa."),
            ["RP2018"] = Say(
                "The Byzantine majority of the Greek manuscripts, as Robinson and Pierpont reconstructed it.",
                "Візантійська більшість грецьких рукописів у реконструкції Робінсона й Пірпонта.",
                "Die byzantinische Mehrheit der griechischen Handschriften, wie Robinson und Pierpont sie rekonstruierten.",
                "La mayoría bizantina de los manuscritos griegos, tal como la reconstruyeron Robinson y Pierpont."),
            ["GRCBRENT"] = Say(
                "The Septuagint in the Roman edition of 1587, as Brenton printed it facing his English.",
                "Септуагінта за римським виданням 1587 року, як Брентон надрукував її поруч зі своїм англійським перекладом.",
                "Die Septuaginta in der römischen Ausgabe von 1587, wie Brenton sie neben seiner englischen Übersetzung druckte.",
                "La Septuaginta en la edición romana de 1587, tal como Brenton la imprimió frente a su traducción inglesa."),
            ["SWETE"] = Say(
                "The Septuagint of Codex Vaticanus, as Swete printed it at Cambridge.",
                "Септуагінта за Ватиканським кодексом у кембриджському виданні Світа.",
                "Die Septuaginta des Codex Vaticanus, wie Swete sie in Cambridge druckte.",
                "La Septuaginta del Códice Vaticano, tal como Swete la imprimió en Cambridge."),
            ["SWETEOG"] = Say(
                "Daniel in the Septuagint's older Greek translation, printed by Swete beside Theodotion's.",
                "Даниїл у давнішому грецькому перекладі Септуагінти, надрукований Світом поруч із Теодотіоновим.",
                "Daniel in der älteren griechischen Übersetzung der Septuaginta, von Swete neben der des Theodotion gedruckt.",
                "Daniel en la traducción griega más antigua de la Septuaginta, impresa por Swete junto a la de Teodoción."),
            ["OTTLEY"] = Say(
                "Isaiah exactly as Codex Alexandrinus has it, printed by Ottley.",
                "Книга Ісаї точно так, як її містить Олександрійський кодекс, у виданні Оттлі.",
                "Jesaja genau so, wie der Codex Alexandrinus ihn bietet, gedruckt von Ottley.",
                "Isaías tal como lo trae el Códice Alejandrino, impreso por Ottley."),
            ["ALEX"] = Say(
                "The fifth-century Codex Alexandrinus, transcribed as its scribe wrote it.",
                "Олександрійський кодекс V століття, переписаний так, як його написав писар.",
                "Der Codex Alexandrinus aus dem fünften Jahrhundert, so transkribiert, wie sein Schreiber ihn schrieb.",
                "El Códice Alejandrino del siglo V, transcrito tal como lo escribió su copista."),
            ["KJV"] = Say(
                "The English Bible of 1611, in the modern standard text, with the Apocrypha.",
                "Англійська Біблія 1611 року в сучасному стандартному тексті, з апокрифами.",
                "Die englische Bibel von 1611 im heutigen Standardtext, mit den Apokryphen.",
                "La Biblia inglesa de 1611 en su texto estándar moderno, con los apócrifos."),
            ["TYN1534"] = Say(
                "Tyndale's New Testament, the first printed in English, in his own spelling.",
                "Новий Заповіт Тиндейла — перший, надрукований англійською, з його власним правописом.",
                "Tyndales Neues Testament, das erste gedruckte auf Englisch, in seiner eigenen Schreibung.",
                "El Nuevo Testamento de Tyndale, el primero impreso en inglés, con su propia ortografía."),
            ["GNV1599"] = Say(
                "The Bible of the Protestant exiles at Geneva, which England read before the King James.",
                "Біблія протестантських вигнанців у Женеві, яку Англія читала до Біблії короля Якова.",
                "Die Bibel der protestantischen Exilanten in Genf, die England vor der King James las.",
                "La Biblia de los exiliados protestantes en Ginebra, que Inglaterra leyó antes de la King James."),
            ["ASV"] = Say(
                "The American Revised Version: the King James line brought onto the critical Greek text.",
                "Американська редакція Revised Version: традиція Біблії короля Якова на основі критичного грецького тексту.",
                "Die amerikanische Revised Version: die Linie der King James auf den kritischen griechischen Text gebracht.",
                "La Revised Version americana: la línea de la King James llevada al texto griego crítico."),
            ["YLT"] = Say(
                "A strictly literal English that keeps the grammar of the Hebrew and Greek.",
                "Суворо буквальний англійський переклад, що зберігає граматику єврейського й грецького тексту.",
                "Ein streng wörtliches Englisch, das die Grammatik des Hebräischen und Griechischen bewahrt.",
                "Un inglés estrictamente literal que conserva la gramática del hebreo y del griego."),
            ["JPS1917"] = Say(
                "The first English Bible made by Jews for Jews, from the Masoretic text.",
                "Перша англійська Біблія, зроблена євреями для євреїв, з масоретського тексту.",
                "Die erste englische Bibel von Juden für Juden, aus dem masoretischen Text.",
                "La primera Biblia inglesa hecha por judíos para judíos, a partir del texto masorético."),
            ["BSB"] = Say(
                "A modern English translation in which every word can be traced to the Hebrew or Greek.",
                "Сучасний англійський переклад, у якому кожне слово можна простежити до єврейського чи грецького.",
                "Eine moderne englische Übersetzung, in der sich jedes Wort auf das Hebräische oder Griechische zurückführen lässt.",
                "Una traducción inglesa moderna en la que cada palabra remite al hebreo o al griego."),
            ["WEB"] = Say(
                "A modern English update of the American Standard Version, in the public domain.",
                "Сучасне оновлення Американської стандартної версії, у суспільному надбанні.",
                "Eine moderne Überarbeitung der American Standard Version, gemeinfrei.",
                "Una actualización moderna de la American Standard Version, de dominio público."),
            ["BBE"] = Say(
                "The Bible in a vocabulary of about a thousand English words.",
                "Біблія, викладена словником приблизно з тисячі англійських слів.",
                "Die Bibel in einem Wortschatz von etwa tausend englischen Wörtern.",
                "La Biblia en un vocabulario de unas mil palabras inglesas."),
            ["LUTH1912"] = Say(
                "Luther's German Bible in the churches' revision of 1912.",
                "Німецька Біблія Лютера в церковній редакції 1912 року.",
                "Luthers deutsche Bibel in der kirchlichen Revision von 1912.",
                "La Biblia alemana de Lutero en la revisión eclesiástica de 1912."),
            ["ELB1905"] = Say(
                "The literal German Bible of the Brethren, published at Elberfeld.",
                "Буквальна німецька Біблія «братів», видана в Ельберфельді.",
                "Die wörtliche deutsche Bibel der Brüderbewegung, in Elberfeld herausgegeben.",
                "La Biblia alemana literal de los Hermanos, publicada en Elberfeld."),
            ["RV1909"] = Say(
                "Reina's Spanish Bible as Valera revised it, in the edition of 1909.",
                "Іспанська Біблія Рейни в редакції Валери, видання 1909 року.",
                "Reinas spanische Bibel in Valeras Revision, Ausgabe von 1909.",
                "La Biblia española de Reina revisada por Valera, en la edición de 1909."),
            ["CUV"] = Say(
                "The Chinese Union Version, the Bible of most Chinese Protestants, in traditional characters.",
                "Китайська Об'єднана версія, Біблія більшості китайських протестантів, традиційними ієрогліфами.",
                "Die chinesische Union Version, die Bibel der meisten chinesischen Protestanten, in Langzeichen.",
                "La Versión de la Unión china, la Biblia de la mayoría de los protestantes chinos, en caracteres tradicionales."),
            ["CUVS"] = Say(
                "The Chinese Union Version in simplified characters.",
                "Китайська Об'єднана версія спрощеними ієрогліфами.",
                "Die chinesische Union Version in Kurzzeichen.",
                "La Versión de la Unión china en caracteres simplificados."),
            ["KRV"] = Say(
                "The Korean Revised Version of 1961, the Bible of Korean Protestants until 1998.",
                "Корейська переглянута версія 1961 року, Біблія корейських протестантів до 1998 року.",
                "Die koreanische Revised Version von 1961, bis 1998 die Bibel der koreanischen Protestanten.",
                "La Versión Revisada coreana de 1961, la Biblia de los protestantes coreanos hasta 1998."),
            ["RUSV"] = Say(
                "The Russian Synodal translation of 1876, with the books it marks as non-canonical.",
                "Російський Синодальний переклад 1876 року, разом із книгами, які він позначає як неканонічні.",
                "Die russische Synodalübersetzung von 1876, mit den Büchern, die sie als nichtkanonisch kennzeichnet.",
                "La traducción sinodal rusa de 1876, con los libros que marca como no canónicos."),
            ["UBIO"] = Say(
                "Ivan Ohienko's Ukrainian Bible, translated from the Hebrew and Greek.",
                "Українська Біблія Івана Огієнка, перекладена з єврейської та грецької.",
                "Iwan Ohijenkos ukrainische Bibel, aus dem Hebräischen und Griechischen übersetzt.",
                "La Biblia ucraniana de Iván Ohienko, traducida del hebreo y del griego."),
            ["UKR1871"] = Say(
                "The first complete Bible in modern literary Ukrainian, by Kulish, Puliui and Nechui-Levytsky.",
                "Перша повна Біблія сучасною літературною українською — Куліша, Пулюя й Нечуя-Левицького.",
                "Die erste vollständige Bibel im modernen literarischen Ukrainisch, von Kulisch, Puljuj und Netschuj-Lewyzkyj.",
                "La primera Biblia completa en ucraniano literario moderno, de Kulish, Puliui y Nechui-Levytski."),
            ["NPU2022"] = Say(
                "A new translation into present-day Ukrainian: the New Testament and the Psalms.",
                "Новий переклад сучасною українською: Новий Заповіт і Псалми.",
                "Eine neue Übersetzung ins heutige Ukrainisch: das Neue Testament und die Psalmen.",
                "Una nueva traducción al ucraniano actual: el Nuevo Testamento y los Salmos."),
            ["LSG1910"] = Say(
                "Louis Segond's French Bible in the 1910 revision, long the Bible of French Protestants.",
                "Французька Біблія Луї Сегона в редакції 1910 року, що довго була Біблією французьких протестантів.",
                "Louis Segonds französische Bibel in der Revision von 1910, lange die Bibel der französischen Protestanten.",
                "La Biblia francesa de Louis Segond en la revisión de 1910, durante décadas la de los protestantes franceses."),
            ["JND2024"] = Say(
                "Darby's French Bible of 1885, in the 2024 revision released free of rights.",
                "Французька Біблія Дарбі 1885 року в редакції 2024 року, вільній від прав.",
                "Darbys französische Bibel von 1885 in der rechtefreien Revision von 2024.",
                "La Biblia francesa de Darby de 1885, en la revisión de 2024 libre de derechos."),
            ["SCH1951"] = Say(
                "Schlachter's German Bible in the Geneva Bible Society's revision of 1951.",
                "Німецька Біблія Шлахтера в редакції Женевського біблійного товариства 1951 року.",
                "Schlachters deutsche Bibel in der Revision der Genfer Bibelgesellschaft von 1951.",
                "La Biblia alemana de Schlachter en la revisión de 1951 de la Sociedad Bíblica de Ginebra."),
            ["RLT2018"] = Say(
                "A light modern revision of the King James Version, with the divine name as Yhwh.",
                "Легка сучасна редакція Біблії короля Якова, з Божим ім'ям як Yhwh.",
                "Eine behutsame moderne Revision der King James Version, mit dem Gottesnamen als Yhwh.",
                "Una revisión moderna y ligera de la King James Version, con el nombre divino como Yhwh."),
            ["ALM1911"] = Say(
                "Almeida's Bible, the first in Portuguese, in the Revista e Corrigida.",
                "Біблія Алмейди, перша португальською, у редакції Revista e Corrigida.",
                "Almeidas Bibel, die erste auf Portugiesisch, in der Revista e Corrigida.",
                "La Biblia de Almeida, la primera en portugués, en la Revista e Corrigida."),
            ["AVD1865"] = Say(
                "The Van Dyck Arabic Bible of the Protestant churches, fully vowelled.",
                "Арабська Біблія Ван Дейка, Біблія протестантських церков, з повною огласовкою.",
                "Die arabische Van-Dyck-Bibel der protestantischen Kirchen, voll vokalisiert.",
                "La Biblia árabe de Van Dyck de las iglesias protestantes, totalmente vocalizada."),
            ["IRV2019"] = Say(
                "The Indian Revised Version, the one complete Hindi Bible under an open licence.",
                "Індійська переглянута версія — єдина повна Біблія мовою гінді під відкритою ліцензією.",
                "Die Indian Revised Version, die einzige vollständige Hindi-Bibel unter offener Lizenz.",
                "La Indian Revised Version, la única Biblia completa en hindi con licencia abierta."),
            ["BRENTON"] = Say(
                "Brenton's English translation of the Septuagint, verse for verse with its Greek.",
                "Англійський переклад Септуагінти Брентона, вірш у вірш із її грецьким текстом.",
                "Brentons englische Übersetzung der Septuaginta, Vers für Vers mit ihrem griechischen Text.",
                "La traducción inglesa de la Septuaginta de Brenton, versículo a versículo con su griego."),
            ["DRA"] = Say(
                "The English Catholic Bible from the Latin Vulgate, in Challoner's revision.",
                "Англійська католицька Біблія з латинської Вульгати в редакції Чаллонера.",
                "Die englische katholische Bibel aus der lateinischen Vulgata, in Challoners Revision.",
                "La Biblia católica inglesa traducida de la Vulgata latina, en la revisión de Challoner."),
            ["VULGCLEM"] = Say(
                "The Latin Vulgate in the Clementine edition, the Catholic Church's text until 1979.",
                "Латинська Вульгата в Климентинському виданні, офіційний текст Католицької церкви до 1979 року.",
                "Die lateinische Vulgata in der Clementina, bis 1979 der Text der katholischen Kirche.",
                "La Vulgata latina en la edición clementina, texto de la Iglesia católica hasta 1979."),
            ["ULT"] = Say(
                "unfoldingWord's literal revision of the American Standard Version, every word tied by hand to the original.",
                "Буквальна редакція Американської стандартної версії від unfoldingWord, де кожне слово вручну пов'язане з оригіналом.",
                "Die wörtliche Revision der American Standard Version von unfoldingWord, jedes Wort von Hand mit dem Original verbunden.",
                "La revisión literal de la American Standard Version de unfoldingWord, con cada palabra unida a mano al original."),
        };

    /// <summary>
    /// The line for the text with this identifier, looked up under the identifier and under every
    /// spelling it answers to. Null for a text nobody has described yet.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? For(string slug)
    {
        if (Written.TryGetValue(slug, out var tagline))
        {
            return tagline;
        }

        return TextAliases.Of(slug)
            .Select(alias => Written.GetValueOrDefault(alias))
            .FirstOrDefault(found => found is not null);
    }

    private static IReadOnlyDictionary<string, string> Say(string en, string uk, string de, string es) =>
        new Dictionary<string, string> { ["en"] = en, ["uk"] = uk, ["de"] = de, ["es"] = es };
}
