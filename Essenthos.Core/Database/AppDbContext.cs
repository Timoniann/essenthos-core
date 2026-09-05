using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database;

/// <summary>
/// The witness model: texts, the relations between them, each text's own books, chapters, verses
/// and words, the canonical frame that places every verse in one address space, and the links that
/// state which words of one text correspond to which words of another.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Text> Texts { get; set; } = null!;

    public DbSet<TextRelation> TextRelations { get; set; } = null!;

    public DbSet<Book> Books { get; set; } = null!;

    public DbSet<Chapter> Chapters { get; set; } = null!;

    public DbSet<Verse> Verses { get; set; } = null!;

    public DbSet<Word> Words { get; set; } = null!;

    public DbSet<VerseReference> VerseReferences { get; set; } = null!;

    public DbSet<StatedVerseNumber> StatedVerseNumbers { get; set; } = null!;

    public DbSet<Link> Links { get; set; } = null!;

    public DbSet<LinkWord> LinkWords { get; set; } = null!;

    /// <summary>
    /// Every method that says a link is true. The link keeps the strongest as its own answer; this
    /// is where the others live, and where agreement between them becomes countable.
    /// </summary>
    public DbSet<LinkClaim> LinkClaims { get; set; } = null!;

    public DbSet<VerseLink> VerseLinks { get; set; } = null!;

    public DbSet<VerseLinkVerse> VerseLinkVerses { get; set; } = null!;

    /// <summary>The spans a text's own analysis names — clauses, phrases, sentences.</summary>
    public DbSet<WordGroup> WordGroups { get; set; } = null!;

    public DbSet<WordGroupWord> WordGroupWords { get; set; } = null!;

    /// <summary>Strong's concordance, which a word reaches by number rather than by key.</summary>
    public DbSet<StrongEntry> StrongEntries { get; set; } = null!;

    /// <summary>
    /// Strong numbers proposed for a word, with what proposed them. Separate from
    /// <c>word.strong_number</c>, which means a source stated it.
    /// </summary>
    public DbSet<WordStrong> WordStrongs { get; set; } = null!;

    /// <summary>
    /// Which peoples the dictionary says are named after whom, read out of its own prose. Keyed on
    /// the lexeme rather than on two entities, because a people is not an entity here.
    /// </summary>
    public DbSet<StrongGentilic> StrongGentilics { get; set; } = null!;

    /// <summary>What each load measured about the corpus it wrote, one row per load.</summary>
    public DbSet<VerificationRun> VerificationRuns { get; set; } = null!;

    /// <summary>The people and places the text names, and where it names them.</summary>
    public DbSet<Entity> Entities { get; set; } = null!;

    public DbSet<EntityName> EntityNames { get; set; } = null!;

    public DbSet<EntityRelationship> EntityRelationships { get; set; } = null!;

    public DbSet<EntityVerse> EntityVerses { get; set; } = null!;

    public DbSet<Event> Events { get; set; } = null!;

    /// <summary>Whose reckoning a date belongs to, and the dates themselves.</summary>
    public DbSet<Chronology> Chronologies { get; set; } = null!;

    public DbSet<Period> Periods => Set<Period>();

    public DbSet<EventDate> EventDates { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        NameTablesInTheSingular(modelBuilder);

        modelBuilder.Entity<Entity>().Property(e => e.Kind).HasConversion(EnumStorage.EntityKind);

        modelBuilder.Entity<EntityRelationship>(entity =>
        {
            entity.HasOne(r => r.From).WithMany().HasForeignKey(r => r.FromEntityId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(r => r.To).WithMany().HasForeignKey(r => r.ToEntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // The claim is the dictionary's and the entity is only where it lands, so an entity going
        // away takes the link with it and leaves the sentence standing.
        modelBuilder.Entity<StrongGentilic>(entity => entity
            .HasOne(g => g.Origin)
            .WithMany()
            .HasForeignKey(g => g.OriginEntityId)
            .OnDelete(DeleteBehavior.SetNull));

        modelBuilder.Entity<Text>(entity =>
        {
            entity.Property(t => t.Kind).HasConversion(EnumStorage.TextKind);
            entity.Property(t => t.Direction).HasConversion(EnumStorage.TextDirection);
            entity.Property(t => t.Versification).HasConversion(EnumStorage.Versification);
            entity.Property(t => t.Redistribution).HasConversion(EnumStorage.Redistribution);
        });

        modelBuilder.Entity<TextRelation>(entity =>
        {
            entity.Property(r => r.Relation).HasConversion(EnumStorage.TextRelationKind);

            entity.HasOne(r => r.FromText)
                .WithMany()
                .HasForeignKey(r => r.FromTextId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.ToText)
                .WithMany()
                .HasForeignKey(r => r.ToTextId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("text_relation", t => t.HasCheckConstraint(
                "ck_text_relation_distinct_texts", "\"from_text_id\" <> \"to_text_id\""));
        });

        modelBuilder.Entity<Chapter>(entity =>
        {
            entity.HasOne(c => c.Book)
                .WithMany(b => b.Chapters)
                .HasForeignKey(c => c.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            // The text is denormalised onto every level below it so that scoping a query to one
            // text needs no join, and every one of those keys cascades: Postgres does not promise
            // that the chain through book and chapter has run by the time it checks this key, so a
            // no-action key here refuses to let a text be removed at all.
            entity.HasOne(c => c.Text)
                .WithMany()
                .HasForeignKey(c => c.TextId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Verse>(entity =>
        {
            entity.HasOne(v => v.Chapter)
                .WithMany(c => c.Verses)
                .HasForeignKey(v => v.ChapterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(v => v.Book)
                .WithMany()
                .HasForeignKey(v => v.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(v => v.Text)
                .WithMany()
                .HasForeignKey(v => v.TextId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Word>(entity =>
        {
            entity.HasOne(w => w.Verse)
                .WithMany(v => v.Words)
                .HasForeignKey(w => w.VerseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(w => w.Text)
                .WithMany()
                .HasForeignKey(w => w.TextId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VerseReference>(entity =>
        {
            entity.HasOne(r => r.Verse)
                .WithMany(v => v.References)
                .HasForeignKey(r => r.VerseId)
                .OnDelete(DeleteBehavior.Cascade);

            // Exactly one primary placement per verse. The other invariant DOC-0007 names — that no
            // two verses of one text claim the same primary placement — spans a join and belongs to
            // the verification pass.
            entity.HasIndex(r => r.VerseId)
                .IsUnique()
                .HasFilter("\"is_primary\"")
                .HasDatabaseName("ix_verse_reference_one_primary_per_verse");
        });

        modelBuilder.Entity<StatedVerseNumber>(entity =>
        {
            entity.HasOne(n => n.Verse)
                .WithMany(v => v.StatedNumbers)
                .HasForeignKey(n => n.VerseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WordGroup>(entity =>
        {
            entity.Property(g => g.Kind).HasConversion(EnumStorage.WordGroupKind);

            entity.HasOne(g => g.Text).WithMany().HasForeignKey(g => g.TextId).OnDelete(DeleteBehavior.Cascade);

            // A group whose parent goes takes its children with it, which is what nesting means.
            entity.HasOne(g => g.Parent)
                .WithMany()
                .HasForeignKey(g => g.ParentId)
                .OnDelete(DeleteBehavior.Cascade);

            // A mother is a reference and not a container, so losing it loses the edge and not the
            // group that pointed along it.
            entity.HasOne(g => g.MotherGroup)
                .WithMany()
                .HasForeignKey(g => g.MotherGroupId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(g => g.MotherWord)
                .WithMany()
                .HasForeignKey(g => g.MotherWordId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        ConfigureLink(modelBuilder);
        ConfigureVerseLink(modelBuilder);
        ConfigureWordStrong(modelBuilder);
    }

    /// <summary>
    /// A table is named for what one of its rows is, and the design document, every measurement
    /// taken against this corpus and every query in it are written that way: <c>from link</c>,
    /// <c>from verse_reference</c>. Left to the DbSet names, EF would pluralise them all.
    ///
    /// Derived from the entity type rather than listed. The list was written by hand, so a new
    /// entity opted out of the convention by nobody remembering it — two tables were created
    /// plural before anyone noticed (PRB-0083). A convention cannot be forgotten.
    /// </summary>
    private static void NameTablesInTheSingular(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.ClrType is { } type && !entity.IsOwned())
            {
                entity.SetTableName(SnakeCase(type.Name));
            }
        }
    }

    /// <summary>
    /// <c>VerseReference</c> becomes <c>verse_reference</c>, which is what
    /// <c>EFCore.NamingConventions</c> does to every column already; this applies the same rule to
    /// the table, singular because the type is singular.
    /// </summary>
    private static string SnakeCase(string name)
    {
        var snake = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                snake.Append('_');
            }

            snake.Append(char.ToLowerInvariant(name[i]));
        }

        return snake.ToString();
    }

    private static void ConfigureLink(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Link>(entity =>
        {
            entity.Property(l => l.Relation).HasConversion(EnumStorage.LinkRelation);
            entity.Property(l => l.Method).HasConversion(EnumStorage.LinkMethod);

            entity.HasOne(l => l.FromText)
                .WithMany()
                .HasForeignKey(l => l.FromTextId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(l => l.ToText)
                .WithMany()
                .HasForeignKey(l => l.ToTextId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("link", t => AddProvenanceConstraints(t, "link"));
        });

        modelBuilder.Entity<LinkWord>(entity =>
        {
            entity.Property(w => w.Side).HasConversion(EnumStorage.LinkSide);

            entity.HasOne(w => w.Link)
                .WithMany(l => l.Words)
                .HasForeignKey(w => w.LinkId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(w => w.Word)
                .WithMany()
                .HasForeignKey(w => w.WordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LinkClaim>(entity =>
        {
            entity.Property(c => c.Method).HasConversion(EnumStorage.LinkMethod);

            entity.HasOne(c => c.Link)
                .WithMany(l => l.Claims)
                .HasForeignKey(c => c.LinkId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("link_claim", t => AddProvenanceConstraints(t, "link_claim"));
        });
    }

    private static void ConfigureWordStrong(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WordStrong>(entity =>
        {
            entity.Property(w => w.Method).HasConversion(EnumStorage.LinkMethod);

            entity.HasOne(w => w.Word)
                .WithMany()
                .HasForeignKey(w => w.WordId)
                .OnDelete(DeleteBehavior.Cascade);

            // The same rules the links live under, and for the same reason: a proposal that carried
            // no confidence while claiming to be inferred would read as testimony.
            entity.ToTable("word_strong", t => AddProvenanceConstraints(t, "word_strong"));
        });
    }

    private static void ConfigureVerseLink(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VerseLink>(entity =>
        {
            entity.Property(l => l.Relation).HasConversion(EnumStorage.LinkRelation);
            entity.Property(l => l.Method).HasConversion(EnumStorage.LinkMethod);

            entity.HasOne(l => l.FromText)
                .WithMany()
                .HasForeignKey(l => l.FromTextId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(l => l.ToText)
                .WithMany()
                .HasForeignKey(l => l.ToTextId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("verse_link", t => AddProvenanceConstraints(t, "verse_link"));
        });

        modelBuilder.Entity<VerseLinkVerse>(entity =>
        {
            entity.Property(v => v.Side).HasConversion(EnumStorage.LinkSide);

            entity.HasOne(v => v.VerseLink)
                .WithMany(l => l.Verses)
                .HasForeignKey(v => v.VerseLinkId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(v => v.Verse)
                .WithMany()
                .HasForeignKey(v => v.VerseId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>
    /// The rule that a guess is never stored looking like a sourced claim, written as constraints
    /// rather than as a convention a loader is trusted to keep: a correspondence a source states
    /// carries no confidence, one a process inferred carries one, and every correspondence names
    /// what produced it.
    /// </summary>
    private static Microsoft.EntityFrameworkCore.Metadata.Builders.TableBuilder AddProvenanceConstraints(
        Microsoft.EntityFrameworkCore.Metadata.Builders.TableBuilder table, string tableName)
    {
        var stated = EnumSpelling.Of(LinkMethod.StatedBySource);
        var manual = EnumSpelling.Of(LinkMethod.Manual);

        table.HasCheckConstraint(
            $"ck_{tableName}_confidence_range",
            "\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)");

        table.HasCheckConstraint(
            $"ck_{tableName}_stated_carries_no_confidence",
            $"\"method\" <> '{stated}' OR \"confidence\" IS NULL");

        table.HasCheckConstraint(
            $"ck_{tableName}_inferred_carries_confidence",
            $"\"method\" IN ('{stated}', '{manual}') OR \"confidence\" IS NOT NULL");

        table.HasCheckConstraint(
            $"ck_{tableName}_source_not_empty",
            "length(btrim(\"source\")) > 0");

        return table;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSnakeCaseNamingConvention();
    }
}
