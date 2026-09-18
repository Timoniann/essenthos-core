using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Accounts;

/// <summary>
/// The database the API owns and writes: accounts, how they sign in, and their sessions. It is
/// <c>essenthos_app</c> on a server, beside the corpus and never inside it — a corpus release replaces
/// its database whole, and this one must survive every release untouched.
///
/// Nothing here may reference a corpus row id (RUL-0185): the corpus renumbers every key when it is
/// rebuilt, so anything that points into it does so by canonical address.
/// </summary>
public class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : DbContext(options)
{
    public const string RevisionSequence = "revision";

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Credential> Credentials => Set<Credential>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<AccountPhoto> AccountPhotos => Set<AccountPhoto>();

    public DbSet<AccountEmail> AccountEmails => Set<AccountEmail>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<Reading> Readings => Set<Reading>();

    public DbSet<Note> Notes => Set<Note>();

    /// <summary>
    /// The keys that protect the sign-in flow's short-lived cookies. Here rather than on a disk so that
    /// every API process of one environment shares them, they survive a container being replaced, and
    /// they are in the nightly backup with everything else. Two environments never share them: each
    /// has its own database.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSnakeCaseNamingConvention();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasSequence<long>(RevisionSequence);

        // Singular, as the corpus's tables are: a table is named for what one row is.
        model.Entity<Account>(account =>
        {
            account.ToTable("account");
            account.Property(a => a.Id).ValueGeneratedNever();
            account.Property(a => a.DisplayName).HasMaxLength(Limits.DisplayName);
            account.Property(a => a.About).HasMaxLength(Limits.About);
            account.Property(a => a.Locale).HasMaxLength(16);
            account.Property(a => a.ProviderPhotoUrl).HasMaxLength(1024);
            account.HasIndex(a => a.Revision);
            account.HasMany(a => a.Credentials).WithOne(c => c.Account).HasForeignKey(c => c.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Credential>(credential =>
        {
            credential.ToTable("credential");
            credential.Property(c => c.Provider).HasMaxLength(32);
            credential.Property(c => c.Subject).HasMaxLength(256);
            credential.Property(c => c.Email).HasMaxLength(320);
            credential.HasIndex(c => new { c.Provider, c.Subject }).IsUnique();
        });

        model.Entity<Session>(session =>
        {
            session.ToTable("session");
            session.Property(s => s.Id).ValueGeneratedNever();
            session.Property(s => s.Device).HasMaxLength(Limits.Device);
            session.HasIndex(s => s.TokenHash).IsUnique();
            session.HasIndex(s => s.AccountId);
            session.HasOne(s => s.Account).WithMany().HasForeignKey(s => s.AccountId).OnDelete(DeleteBehavior.Cascade);
            session.HasOne<Device>().WithMany().HasForeignKey(s => s.DeviceId).OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<DataProtectionKey>(key => key.ToTable("data_protection_key"));

        model.Entity<AccountEmail>(email =>
        {
            email.ToTable("account_email");
            email.HasKey(e => e.Email);
            email.Property(e => e.Email).HasMaxLength(320);
            email.HasIndex(e => e.AccountId);
            email.HasOne<Account>().WithMany().HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Device>(device =>
        {
            device.ToTable("device");
            device.Property(d => d.Id).ValueGeneratedNever();
            device.Property(d => d.Kind).HasMaxLength(16);
            device.Property(d => d.Os).HasMaxLength(32);
            device.Property(d => d.Browser).HasMaxLength(32);
            device.Property(d => d.Model).HasMaxLength(64);
            device.Property(d => d.Settings).HasColumnType("jsonb");
            device.HasIndex(d => d.AccountId);
            device.HasIndex(d => d.Revision);
            device.HasOne<Account>().WithMany().HasForeignKey(d => d.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Reading>(reading =>
        {
            reading.ToTable("reading");
            reading.Property(r => r.Book).HasMaxLength(32);
            reading.Property(r => r.Corpora).HasMaxLength(200);
            reading.HasIndex(r => new { r.AccountId, r.At });
            reading.HasIndex(r => new { r.DeviceId, r.At });
            reading.HasOne<Device>().WithMany().HasForeignKey(r => r.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Note>(note =>
        {
            note.ToTable("note");
            note.Property(n => n.Id).ValueGeneratedNever();
            note.Property(n => n.Text).HasMaxLength(32);
            note.Property(n => n.Body).HasMaxLength(Limits.NoteBody);
            // What the reader opens a chapter by: every note of theirs that starts in this book.
            note.HasIndex(n => new { n.AccountId, n.Book, n.Chapter });
            note.HasIndex(n => n.Revision);
            note.HasOne<Account>().WithMany().HasForeignKey(n => n.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<AccountPhoto>(photo =>
        {
            photo.ToTable("account_photo");
            photo.HasKey(p => p.AccountId);
            photo.Property(p => p.ContentType).HasMaxLength(32);
            photo.HasOne<Account>().WithOne().HasForeignKey<AccountPhoto>(p => p.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>
    /// Stamps every added or changed row that a second device will have to catch up on with the next
    /// revision, from one sequence, so revisions are ordered across tables and never reused.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        foreach (var entry in Revised())
        {
            Stamp(entry, await NextRevisionQuery().SingleAsync(cancellationToken));
        }

        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>The same, for the one caller that cannot be asynchronous: the data-protection key store.</summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        foreach (var entry in Revised())
        {
            Stamp(entry, NextRevisionQuery().Single());
        }

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private List<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<IRevised>> Revised() =>
        ChangeTracker.Entries<IRevised>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToList();

    private static void Stamp(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<IRevised> entry, long revision)
    {
        entry.Entity.Revision = revision;
        entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private IQueryable<long> NextRevisionQuery() =>
        Database.SqlQueryRaw<long>($"SELECT nextval('{RevisionSequence}') AS \"Value\"");
}

/// <summary>The lengths the columns and the endpoints agree on.</summary>
public static class Limits
{
    public const int DisplayName = 60;

    public const int About = 1000;

    public const int Device = 200;

    /// <summary>An uploaded photo. Enough for a sharp square picture; not enough to be a file store.</summary>
    public const int PhotoBytes = 1024 * 1024;

    /// <summary>A note: several pages of writing, which is a margin and not a book.</summary>
    public const int NoteBody = 20_000;

    /// <summary>How many notes one account keeps; far past any reader, short of a bulk store.</summary>
    public const int NotesPerAccount = 50_000;
}
