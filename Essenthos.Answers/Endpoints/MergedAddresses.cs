using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Where the address of a record folded into another now arrives.
///
/// <para>
/// A dataset that wrote one man twice gave him two addresses, and a page is linked from outside by
/// whichever one the linker saw. Once the two are one record, the second address is answered as the
/// first: the response carries the address that stays, which is how a client knows to show it.
/// </para>
///
/// <para>
/// A record withdrawn from the encyclopedia whose word another record holds is answered the same
/// way, as that record (<see cref="WithdrawnAddresses"/>), unless a record holds the address again.
/// </para>
/// </summary>
internal static class MergedAddresses
{
    public static Task<string> Current(AppDbContext db, string slug, CancellationToken cancellationToken) =>
        SharedReads.Once($"current:{slug}", () => Read(db, slug, cancellationToken));

    private static async Task<string> Read(AppDbContext db, string slug, CancellationToken cancellationToken)
    {
        var folded = await db.MergedRecords
            .Where(m => m.Slug == slug)
            .Select(m => m.Entity!.Slug)
            .FirstOrDefaultAsync(cancellationToken);
        if (folded is not null)
        {
            return folded;
        }

        return WithdrawnAddresses.LeadsTo(slug) is { } leads && !await db.Entities.AnyAsync(e => e.Slug == slug, cancellationToken)
            ? leads
            : slug;
    }
}
