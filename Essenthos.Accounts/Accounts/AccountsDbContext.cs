using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Essenthos.Core.Accounts;

/// <summary>
/// The database the API owns and writes: accounts, how they sign in, and their sessions. It is
/// <c>essenthos_app</c> on a server, beside the corpus and never inside it — a corpus release replaces
/// its database whole, and this one must survive every release untouched.
///
/// Nothing here may reference a corpus row id: the corpus renumbers every key when it is
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

    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();

    public DbSet<ChapterBookmark> ChapterBookmarks => Set<ChapterBookmark>();

    public DbSet<FavoriteText> FavoriteTexts => Set<FavoriteText>();

    public DbSet<Suggestion> Suggestions => Set<Suggestion>();

    public DbSet<SuggestionMessage> SuggestionMessages => Set<SuggestionMessage>();

    public DbSet<AdminAction> AdminActions => Set<AdminAction>();

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

        // A forgotten device is a row nobody reads, and its readings are deleted with it, so the
        // history that points at a device never points at one the filter hides.
        optionsBuilder.ConfigureWarnings(warnings => warnings.Ignore(
            CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
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
            device.HasQueryFilter(d => d.DeletedAt == null);
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

        model.Entity<Bookmark>(bookmark =>
        {
            bookmark.ToTable("bookmark");
            bookmark.Property(b => b.Id).ValueGeneratedNever();
            bookmark.Property(b => b.Text).HasMaxLength(32);
            bookmark.Property(b => b.Color).HasMaxLength(16);
            bookmark.Property(b => b.Comment).HasMaxLength(Limits.BookmarkComment);
            // What the reader opens a chapter by: every bookmark of theirs that starts in this book.
            bookmark.HasIndex(b => new { b.AccountId, b.Book, b.Chapter });
            bookmark.HasIndex(b => b.Revision);
            bookmark.HasQueryFilter(b => b.DeletedAt == null);
            bookmark.HasOne<Account>().WithMany().HasForeignKey(b => b.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<ChapterBookmark>(bookmark =>
        {
            bookmark.ToTable("chapter_bookmark");
            bookmark.Property(b => b.Id).ValueGeneratedNever();
            bookmark.Property(b => b.Color).HasMaxLength(16);
            // One live bookmark per chapter: a removed one stays as a tombstone and the chapter can be marked again.
            bookmark.HasIndex(b => new { b.AccountId, b.Book, b.Chapter }).IsUnique().HasFilter("deleted_at IS NULL");
            bookmark.HasIndex(b => b.Revision);
            bookmark.HasQueryFilter(b => b.DeletedAt == null);
            bookmark.HasOne<Account>().WithMany().HasForeignKey(b => b.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<FavoriteText>(favorite =>
        {
            favorite.ToTable("favorite_text");
            favorite.Property(f => f.Id).ValueGeneratedNever();
            favorite.Property(f => f.Text).HasMaxLength(Limits.TextSlug);
            favorite.HasIndex(f => new { f.AccountId, f.Text }).IsUnique().HasFilter("deleted_at IS NULL");
            favorite.HasIndex(f => f.Revision);
            favorite.HasQueryFilter(f => f.DeletedAt == null);
            favorite.HasOne<Account>().WithMany().HasForeignKey(f => f.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Suggestion>(suggestion =>
        {
            suggestion.ToTable("suggestion");
            suggestion.Property(s => s.Id).ValueGeneratedNever();
            suggestion.Property(s => s.Category).HasMaxLength(16);
            suggestion.Property(s => s.Status).HasMaxLength(16);
            suggestion.Property(s => s.Body).HasMaxLength(Limits.SuggestionBody);
            suggestion.Property(s => s.Fields).HasColumnType("jsonb");
            suggestion.Property(s => s.Language).HasMaxLength(Limits.SuggestionField);
            suggestion.Property(s => s.Locale).HasMaxLength(16);
            // The admin list: what is new first, then by the latest message.
            suggestion.HasIndex(s => new { s.Status, s.LastActivityAt });
            suggestion.HasIndex(s => new { s.AccountId, s.CreatedAt });
            suggestion.HasIndex(s => s.AssignedTo);
            suggestion.HasOne<Account>().WithMany().HasForeignKey(s => s.AccountId).OnDelete(DeleteBehavior.Cascade);
            suggestion.HasOne<Account>().WithMany().HasForeignKey(s => s.AssignedTo).OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<SuggestionMessage>(message =>
        {
            message.ToTable("suggestion_message");
            message.Property(m => m.Kind).HasMaxLength(16);
            message.Property(m => m.Body).HasMaxLength(Limits.SuggestionMessage);
            message.HasIndex(m => new { m.SuggestionId, m.At });
            message.HasIndex(m => new { m.AuthorId, m.At });
            message.HasOne<Suggestion>().WithMany().HasForeignKey(m => m.SuggestionId).OnDelete(DeleteBehavior.Cascade);
            message.HasOne<Account>().WithMany().HasForeignKey(m => m.AuthorId).OnDelete(DeleteBehavior.SetNull);
        });

        // No foreign keys: the record of what was done outlives the account and the suggestion it was
        // done to, which is what the copied names are for.
        model.Entity<AdminAction>(action =>
        {
            action.ToTable("admin_action");
            action.Property(a => a.ActorName).HasMaxLength(Limits.DisplayName);
            action.Property(a => a.AccountName).HasMaxLength(Limits.DisplayName);
            action.Property(a => a.Action).HasMaxLength(32);
            action.Property(a => a.Before).HasMaxLength(200);
            action.Property(a => a.After).HasMaxLength(200);
            action.HasIndex(a => a.At);
            action.HasIndex(a => new { a.SuggestionId, a.At });
            action.HasIndex(a => new { a.AccountId, a.At });
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
    /// revision, from one sequence, so revisions are ordered across tables and never reused. A row that was
    /// removed is kept as a tombstone and stamped as any other change.
    ///
    /// A revision handed out is not yet a revision committed: two saves of one account could take 10 and
    /// 11 and commit 11 first, and a device that synchronised in between would carry 11 as its cursor and
    /// never be told of 10. So the rows of one account are saved under that account's lock, held until the
    /// commit, and its revisions commit in the order they were handed out.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var revised = Revised();
        if (revised.Count == 0)
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        await using var own = Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(cancellationToken) : null;
        foreach (var account in Owners(revised))
        {
            await Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({LockKey(account)}, 0))", cancellationToken);
        }

        foreach (var entry in revised)
        {
            Stamp(entry, await NextRevisionQuery().SingleAsync(cancellationToken));
        }

        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (own is not null)
        {
            await own.CommitAsync(cancellationToken);
        }

        return saved;
    }

    /// <summary>The same, for the one caller that cannot be asynchronous: the data-protection key store.</summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var revised = Revised();
        if (revised.Count == 0)
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        using var own = Database.CurrentTransaction is null ? Database.BeginTransaction() : null;
        foreach (var account in Owners(revised))
        {
            Database.ExecuteSql($"SELECT pg_advisory_xact_lock(hashtextextended({LockKey(account)}, 0))");
        }

        foreach (var entry in revised)
        {
            Stamp(entry, NextRevisionQuery().Single());
        }

        var saved = base.SaveChanges(acceptAllChangesOnSuccess);
        own?.Commit();
        return saved;
    }

    /// <summary>The accounts the rows belong to, in one order, so two saves taking several locks cannot wait on each other.</summary>
    private static IEnumerable<Guid> Owners(IEnumerable<EntityEntry<IRevised>> revised) =>
        revised.Select(e => e.Entity.Owner).Distinct().Order();

    private static string LockKey(Guid account) => $"account-revision:{account}";

    /// <summary>
    /// The rows this save adds or changes, after every removed row that is kept as a tombstone has been
    /// turned from a deletion into the change that marks it.
    /// </summary>
    private List<EntityEntry<IRevised>> Revised()
    {
        var leaving = ChangeTracker.Entries<Account>().Where(e => e.State == EntityState.Deleted).Select(e => e.Entity.Id).ToHashSet();
        var now = DateTimeOffset.UtcNow;
        foreach (var removed in ChangeTracker.Entries<ISoftDeleted>().Where(e => e.State == EntityState.Deleted).ToList())
        {
            // A row going with its account goes in the database's cascade; there is nobody left to tell.
            if (!leaving.Contains(removed.Entity.Owner))
            {
                removed.State = EntityState.Modified;
                removed.Entity.Forget(now);
            }
        }

        return ChangeTracker.Entries<IRevised>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToList();
    }

    private static void Stamp(EntityEntry<IRevised> entry, long revision)
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

    /// <summary>A bookmark's comment: several pages of writing, which is a margin and not a book.</summary>
    public const int BookmarkComment = 20_000;

    /// <summary>How many bookmarks one account keeps: a third of the Bible's verses, short of a bulk store.</summary>
    public const int BookmarksPerAccount = 10_000;

    /// <summary>
    /// The characters one account's comments hold between them — a thousand pages of margin — so the
    /// accounts database, which shares its disk with the corpus, grows by megabytes an account at most.
    /// </summary>
    public const int BookmarkCommentsPerAccount = 2_000_000;

    /// <summary>How many bookmarks one account may make in a day: more than anyone marks by hand.</summary>
    public const int BookmarksPerDay = 500;

    /// <summary>How many chapters one account keeps as places it is reading: more than a reader moves between.</summary>
    public const int ChapterBookmarksPerAccount = 2_000;

    /// <summary>The texts one chapter bookmark remembers as open: more panes than a screen holds.</summary>
    public const int ChapterBookmarkTexts = 12;

    /// <summary>A text's slug, as an account names one: <c>KJV</c>, <c>NESTLE1904</c>.</summary>
    public const int TextSlug = 32;

    /// <summary>How many texts one account keeps as favourites: more than anyone moves between.</summary>
    public const int FavoriteTextsPerAccount = 200;

    /// <summary>A suggestion's main text: a long letter, not a manuscript.</summary>
    public const int SuggestionBody = 10_000;

    /// <summary>Any one of a suggestion's short fields — a language, a title, an edition.</summary>
    public const int SuggestionField = 200;

    /// <summary>The longer fields — notes, sources, a suggested fix.</summary>
    public const int SuggestionLongField = 4_000;

    public const int SuggestionMessage = 10_000;

    public const int SuggestionLinks = 10;

    public const int SuggestionLink = 2_000;

    /// <summary>How many suggestions one account may send in a day: more than anyone writes by hand.</summary>
    public const int SuggestionsPerDay = 10;

    /// <summary>How many follow-up messages one account may write in a day, across its suggestions.</summary>
    public const int SuggestionMessagesPerDay = 60;
}
