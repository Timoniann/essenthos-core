using Essenthos.Core.Corpus;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// The verses a record's relationships were read from, listed on its page where nothing else lists a
/// verse for it.
///
/// <para>
/// Every relationship is this project's own reading of a verse, so the verse it was read from is one
/// the text has the record in. A record the corpus has no word of — a son of Mizraim the genealogy
/// names under the plural of his people, a king the list of Joshua 12 counts — would otherwise have a
/// page with no verse at all while its family tree cites them. The rows say what they are: the verse
/// concerns the record, and no word of it is claimed to name him, so the naming step leaves them
/// <see cref="ReferenceKinds.Concerning"/>.
/// </para>
///
/// <para>
/// Only for a record with no other verse a reader is shown. records-verses writes these rows into
/// <c>records/derived/verses</c> (<see cref="Records.RecordVerses"/>); this keeps the credit they carry.
/// </para>
/// </summary>
internal static class RelationshipVerseLoader
{
    /// <summary>What every row of this kind says about itself.</summary>
    public const string Source =
        "Essenthos, the verse a relationship of this project's own was read from, which concerns the record";
}
