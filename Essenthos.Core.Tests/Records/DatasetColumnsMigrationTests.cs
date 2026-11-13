using Essenthos.Core.Database;
using Essenthos.Core.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The record's dataset columns go wherever they still are, and a database the record files were loaded into, which lost
/// them at the load, takes the migration without a word.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class DatasetColumnsMigrationTests : IDisposable
{
    private const string Columns = """
        SELECT count(*)::int AS "Value" FROM information_schema.columns
        WHERE table_schema = current_schema()
          AND (table_name, column_name) IN (('entity', 'source'), ('entity', 'source_id'), ('entity', 'open_bible_id'),
                                            ('merged_record', 'record_source'), ('merged_record', 'record_source_id'))
        """;

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public DatasetColumnsMigrationTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task ADatabaseThatNeverHadThemTakesTheMigration()
    {
        (await Count()).Should().Be(0);

        await Run(builder => new Migration().Up(builder));

        (await Count()).Should().Be(0);
    }

    [Fact]
    public async Task ADatabaseThatHasThemLosesThem()
    {
        await Run(builder => new Migration().Down(builder));
        (await Count()).Should().Be(5);

        await Run(builder => new Migration().Up(builder));

        (await Count()).Should().Be(0);
    }

    private Task<int> Count() => _db.Database.SqlQueryRaw<int>(Columns).SingleAsync();

    private async Task Run(Action<MigrationBuilder> migration)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        migration(builder);
        foreach (var operation in builder.Operations.OfType<SqlOperation>())
        {
            await _db.Database.ExecuteSqlRawAsync(operation.Sql);
        }
    }

    private sealed class Migration : TheDatasetColumnsOfARecordAreGone
    {
        public new void Up(MigrationBuilder builder) => base.Up(builder);

        public new void Down(MigrationBuilder builder) => base.Down(builder);
    }
}
