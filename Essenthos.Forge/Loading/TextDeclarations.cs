using Essenthos.Core.Database.Entities;

namespace Essenthos.Core.Loading;

/// <summary>
/// The declarations of texts already loaded that their definitions have since corrected, and what
/// <see cref="TextDeclarationLoader"/> does about each. Every passage named here is in no definition any
/// more and every replacement is in its definition, which a test holds; so a corpus loaded cold already
/// reads as these leave a warm one.
/// </summary>
internal static class TextDeclarations
{
    /// <summary>Fields set again from the definition.</summary>
    public static readonly IReadOnlyList<TextFieldRepair> Fields =
    [
        new("RV1909", TextField.About),
        new("JND2024", TextField.Citation),
        new("WH1881", TextField.Citation),
        new("TR1550", TextField.Citation),
        new("TR1894", TextField.Citation),
        new("NPU2022", TextField.Citation),
        new("OHCB", TextField.Citation),
        new("UST", TextField.Citation),
        new("BSB", TextField.Citation),
        new("ALM1911", TextField.Citation),
        new("GRCBRENT", TextField.Citation),
        new("WEB", TextField.Name, TextField.Edition),
        new("VIGLT", TextField.RightsHolder),
    ];

    /// <summary>Passages of rights notes replaced, or taken out where nothing replaces them.</summary>
    public static readonly IReadOnlyList<TextNoteRepair> Notes =
    [
        // The Strong numberings and taggings the corpus uses without naming them to a reader.
        new("JND2024",
            " The Strong numbers in the module are the work of Concordances et Traductions de la Bible "
            + "(concordance.bible); they are read to draw links and are not stored on the words."),
        new("SCH1951",
            " Nobody is named for the Strong numbers in the module; they are read to draw links and are not "
            + "stored on the words."),
        new("LUTH1912", "Settled for the text and unstated for the tagging, and the two are worth keeping apart. "),
        new("LUTH1912",
            " None of the three says who assigned the Strong numbers or under what terms eBible obtained them, "
            + "and a tagging layer is somebody's work even when the text under it is nobody's."),
        new("RV1909",
            " The Strong tagging that arrives with it is not, and is not loaded. It is Rubén Gómez's, established "
            + "by comparison rather than assumed: eBible tags 3 John 1:12 as Todos|G5259,G3956 and "
            + "misma|G5259,G0846, and bibliaparalela publishes those same numbers on those same phrases as "
            + "\"Reina-Valera 1909 con números de Strong, cortesía de Rubén Gómez, utilizado con permiso\". His "
            + "stated terms are that the layer goes to software publishers and not to individuals and that the "
            + "module must be locked; CrossWire carries it encrypted as \"Copyrighted; Permission to distribute "
            + "granted to CrossWire\". eBible names him nowhere."),
        new("LSG1910",
            " CrossWire's Strong-numbered Segond credits its numbers to Concordances et Traductions de la Bible, "
            + "2026, under \"Copyrighted; Permission to distribute granted to CrossWire\"."),

        // Credits and statements that did not match their sources.
        new("RP2018",
            " That is a request rather than a condition, and it is met.",
            " The notice reads: \"Anyone is permitted to copy and distribute this text or any portion of this "
            + "text. All rights to this text are released to everyone and no one can reduce these rights at any "
            + "time.\""),
        new("OHCB",
            "The text is served unchanged and under Biblica's name and mark.",
            "The text is rebuilt from those token files, which hold its words and punctuation and none of its "
            + "paragraphs, headings or notes."),
        new("BRENTON",
            "in an errata file that is kept beside the data and not loaded, with Brenton's two prefaces and his "
            + "table of Jeremiah's chapters.",
            "in an errata file, which is not loaded; neither are Brenton's two prefaces and his table of "
            + "Jeremiah's chapters."),
        new("ALM1911",
            "nineteenth century. Project Gutenberg states \"Public domain in the USA\" on the eBook's page, and "
            + "its licence at the head and foot of the file concerns its own name and trademark, which are not "
            + "reproduced.",
            "nineteenth century, and the page the transcription is published on states \"Public domain in the "
            + "USA\"."),
        new("UBIO",
            "Not settled. bible4u distributes the file as public domain, and CrossWire and Ukrainian Wikisource "
            + "say the same — but each of the three rests on the others rather than on a grant. Against that: "
            + "Ohienko died in 1972, and the sixteen Door43 files carrying the same text head every book "
            + "\"Copyright British and Foreign Bible Society\". Whether the Society has released the 1962 "
            + "edition has not been asked of them. Everything known about who made it and who published it is "
            + "recorded here in the meantime.",
            Bible4uTextSource.Definitions.Values.Single(d => d.Slug == "UBIO").RightsNote!),
        new("WEB",
            "which is what the row says of every text here.",
            "which is what the row says of every text here, and it is named the World English Bible (edited) for "
            + "that reason."),
        new("BHSA", null),
        new("TR1550", null),
        new("TR1894", null),
    ];

    /// <summary>Part-source credits replaced or added.</summary>
    public static readonly IReadOnlyList<TextPartRepair> Parts =
    [
        new("GRCBRENT", null, BrentonEdits.GlauxPart),
        new("WEB",
            DeuterocanonTextSource.WorldEnglishSource with
            {
                Covers = "The deuterocanonical books — Tobit, Judith, Wisdom, Sirach, Baruch, 1 to 4 Maccabees, "
                         + "1 and 2 Esdras, the Prayer of Manasseh and Psalm 151 — from the Classic edition, whose "
                         + "other books are not read.",
            },
            DeuterocanonTextSource.WorldEnglishSource),
    ];
}
