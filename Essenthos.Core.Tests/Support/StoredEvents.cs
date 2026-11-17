using Essenthos.Core.Database;
using Essenthos.Core.Endpoints;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Tests;

/// <summary>The events' stored order written into a test's database, as the build writes it once the events are loaded.</summary>
internal static class StoredEvents
{
    public static async Task Order(AppDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await StoredEventOrder.Write((NpgsqlConnection)db.Database.GetDbConnection(), null, default);
    }
}
