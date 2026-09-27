using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A dataset loaded once into a class's own <see cref="WitnessDatabase"/>, for the classes that
/// used to load the same one again for every test. Each test still opens a transaction and rolls
/// back what it writes, so what one test sees is the seed and nothing another test did.
///
/// The statistics are gathered after the seed: autovacuum is off in a scratch database, and a
/// planner that believes the tables are empty picks plans that take a minute on a few thousand rows.
/// </summary>
public abstract class SeededDatabase(WitnessDatabase database) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var db = database.NewContext();
        await Seed(db);
        await db.Database.ExecuteSqlRawAsync("ANALYZE");
    }

    protected abstract Task Seed(AppDbContext db);

    public Task DisposeAsync() => Task.CompletedTask;
}
