using Essenthos.Core.Build;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the build writes for the Strong pages from the links, the words and the frame (<see cref="StoredStrong"/>), written
/// for a test's database on its connection and transaction, replacing what the tables held.
/// </summary>
internal static class StoredStrongAnswers
{
    public static async Task WriteStoredStrong(this AppDbContext db)
    {
        await db.SaveChangesAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await StoredStrong.WritePairLinks(connection, null, null, CancellationToken.None);
        await StoredStrong.WriteOccurrences(connection, null, null, CancellationToken.None);
        await StoredStrong.WriteRendered(connection, null, null, CancellationToken.None);
    }
}
