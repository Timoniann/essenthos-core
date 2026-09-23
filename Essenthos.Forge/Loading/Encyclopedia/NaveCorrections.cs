namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// Places where BibleData's transcription of Nave has a semicolon where Nave has a comma, so that a
/// verse reads as a whole chapter: AMRAM's <c>EXO 6:18; 20</c> is Exodus 6:18 and 20, not Exodus 6:18
/// and the Ten Commandments.
///
/// <para>
/// The grammar cannot tell these apart — <c>1CH 6:3-15,50-53; 24</c> is rightly 1 Chronicles 24 —
/// so each is listed. A citation is listed where the verse names or tells what the heading is about
/// and the chapter does not; the rest of the file's bare numbers after a verse are left as chapters.
/// Some of those are wrong in another way: ASHDOD's <c>AMO 1:8; 9</c> has lost a chapter, and is
/// neither Amos 1:9 nor Amos 9.
/// </para>
/// </summary>
internal static class NaveCorrections
{
    /// <param name="Subject">The subject whose entry has it.</param>
    /// <param name="Written">The citation as the transcription writes it, once in that subject's entries.</param>
    /// <param name="Read">The same citation with the verse as a verse.</param>
    internal sealed record Correction(string Subject, string Written, string Read);

    public static readonly IReadOnlyList<Correction> All =
    [
        new("AFFLICTIONS AND ADVERSITIES", "ACT 20:23; 24", "ACT 20:23,24"),
        new("AFFLICTIONS AND ADVERSITIES", "REV 2:21; 22", "REV 2:21,22"),
        new("AMRAM", "EXO 6:18; 20", "EXO 6:18,20"),
        new("ASSURANCE", "PSA 3:6; 8", "PSA 3:6,8"),
        new("CAPERNAUM", "MAT 9:1-26; 17:24; 27", "MAT 9:1-26; 17:24,27"),
        new("DUST", "GEN 2:7; 3:19; 23", "GEN 2:7; 3:19,23"),
        new("HAMMOLEKETH", "1CH 7:17; 18", "1CH 7:17,18"),
        new("HELL", "1KI 2:6; 9", "1KI 2:6,9"),
        new("JESUS, THE CHRIST", "LUK 4:14; 15", "LUK 4:14,15"),
        new("JESUS, THE CHRIST", "LUK 6:17-19; 7:21; 22", "LUK 6:17-19; 7:21,22"),
        new("JESUS, THE CHRIST", "MRK 1:14; 15", "MRK 1:14,15"),
        new("MOON", "JOS 10:12; 13", "JOS 10:12,13"),
        new("PARENTS", "EZK 16:44; 45", "EZK 16:44,45"),
        new("SHEBA", "1KI 10:1; 13", "1KI 10:1,13"),
        new("SHIELD", "1KI 10:16; 17", "1KI 10:16,17"),
        new("SHIMEI", "2SA 16:5-13; 19:16; 23", "2SA 16:5-13; 19:16,23"),
        new("SORCERY", "ACT 8:9; 11", "ACT 8:9,11"),
        new("ULAM", "1CH 7:16; 17", "1CH 7:16,17"),
        new("VISION", "REV 6:12; 14", "REV 6:12,14"),
        new("WOMEN", "EXO 15:20; 21", "EXO 15:20,21"),
    ];

    private static readonly ILookup<string, Correction> BySubject = All.ToLookup(c => c.Subject, StringComparer.Ordinal);

    /// <summary>The entry with this subject's listed citations read as Nave printed them.</summary>
    public static string Apply(string subject, string entry)
    {
        foreach (var correction in BySubject[subject])
        {
            entry = entry.Replace(correction.Written, correction.Read, StringComparison.Ordinal);
        }

        return entry;
    }
}
