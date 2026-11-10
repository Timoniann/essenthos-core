using System.Text.Json;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// The records a dataset filed under a word that is nobody's name — <em>waters</em>, <em>a despicable
/// person</em> — withdrawn on the owner's decision that the encyclopedia is this project's.
///
/// <para>
/// **Only a dataset's record, and only one nothing of ours stands on.** The list names a record by
/// its slug and by the dataset's own id for it, and a slug that has since come to mean another
/// record is not touched. A record this corpus annotated a word to, cited a verse for, described,
/// tied to another, pictured or dated an event by is left however the list names it, and the outcome
/// names it: withdrawing it would take our own rows with it, and that is a decision about those
/// rows and not about the heading; the list says so where the owner has decided it.
/// </para>
///
/// <para>
/// **Unless the owner has ruled the record out whatever stands on it.** A record the list marks
/// <see cref="WithdrawnRecord.OwnerRuled"/> goes with everything this corpus holds on it — the lines,
/// ties, names, verses and annotated words, which the database removes with the record, so nothing is
/// left pointing at it. The one thing that stops it is an event or a period dated on it: those are
/// the dataset's timeline, which the record does not own, and the outcome names it.
/// </para>
///
/// <para>
/// What the dataset says of the record stays in its files on disk. The record files hold the list as
/// <c>records/withdrawn</c>, which the records load checks against what it loads.
/// </para>
/// </summary>
internal static class WithdrawnRecordLoader
{
    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static IReadOnlyList<WithdrawnRecord> Read() => File().Records;

    /// <summary>The embedded list, shared with the API through <see cref="WithdrawnAddresses"/>.</summary>
    internal static WithdrawnRecords File()
    {
        using var stream = WithdrawnAddresses.Open();
        return JsonSerializer.Deserialize<WithdrawnRecords>(stream, Shape)
               ?? throw new InvalidOperationException($"{WithdrawnAddresses.Resource} holds no list.");
    }
}

/// <summary>The embedded list, as a file.</summary>
internal sealed record WithdrawnRecords(string DecidedBy, string Policy, IReadOnlyList<WithdrawnRecord> Records);

/// <summary>One record, by its slug and by the dataset's own id for it, with why its heading is no name.</summary>
/// <param name="OwnerRuled">
/// Set where the owner ruled the record out although this corpus holds something on it: what he ruled
/// and when. The record then goes with what stands on it.
/// </param>
/// <param name="OwnRecord">
/// Set where the record is one this corpus wrote itself and a ruling says it should not exist: who
/// ruled it and when. Such a record is withdrawn only while nothing at all stands on it, since there
/// is no dataset's part of it to tell from ours.
/// </param>
/// <param name="LeadsTo">
/// The record now holding the word or the verses the withdrawn one stood for, which its old address
/// leads to; null where nothing took its place and the address is gone.
/// </param>
internal sealed record WithdrawnRecord(
    string Slug, string SourceId, string Why, string? OwnerRuled = null, string? OwnRecord = null, string? LeadsTo = null);
