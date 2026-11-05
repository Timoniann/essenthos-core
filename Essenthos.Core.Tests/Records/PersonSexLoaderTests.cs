using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// A person's sex is written on the record: the dataset's where it gave one, otherwise what the person's
/// relationships say, otherwise the grammar of the name the Greek prints. Nothing else is given one.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PersonSexLoaderTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _greek;

    public PersonSexLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _greek = Corpus.Add(_db, "NESTLE1904", TextKind.CriticalEdition, "grc",
            (25, 1, ["Βερνίκη"]), (25, 2, ["Λεγιών"]), (25, 3, ["Ἀθῆναι"]), (25, 4, ["Ἰουνίαν"]), (25, 5, ["Ἰουνιᾶν"]),
            (25, 6, ["Μαρία"]), (25, 7, ["Μαριὰμ"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void JuniaIsAWomanByTheOwnersWord() =>
        OwnNameLoader.Read().Sexes.Should().ContainSingle(r => r.Entity == "junias" && r.Sex == "female");
}
