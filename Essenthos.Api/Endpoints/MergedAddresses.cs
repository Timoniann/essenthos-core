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
/// </summary>
internal static class MergedAddresses
{
    public static async Task<string> Current(AppDbContext db, string slug, CancellationToken cancellationToken) =>
        await db.MergedRecords
            .Where(m => m.Slug == slug)
            .Select(m => m.Entity!.Slug)
            .FirstOrDefaultAsync(cancellationToken)
        ?? slug;
}
