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

    public DbSet<VerseNote> VerseNotes { get; set; } = null!;

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

    /// <summary>
    /// EVIDENTIA's passes, what each decided about each word, and what a person said about it. None
    /// of it is corpus: a decision reaches <see cref="Links"/> only through an approved review.
    /// </summary>
    public DbSet<EvidentiaRun> EvidentiaRuns { get; set; } = null!;

    public DbSet<EvidentiaDecision> EvidentiaDecisions { get; set; } = null!;

    public DbSet<EvidentiaReview> EvidentiaReviews { get; set; } = null!;

    public DbSet<VerseLink> VerseLinks { get; set; } = null!;

    public DbSet<VerseLinkVerse> VerseLinkVerses { get; set; } = null!;

    /// <summary>The spans a text's own analysis names — clauses, phrases, sentences.</summary>
    public DbSet<WordGroup> WordGroups { get; set; } = null!;

    public DbSet<WordGroupWord> WordGroupWords { get; set; } = null!;

    /// <summary>Strong's concordance, which a word reaches by number rather than by key.</summary>
    public DbSet<StrongEntry> StrongEntries { get; set; } = null!;

    /// <summary>
    /// Strong's four prose fields in a reader's language, beside the English rather than over it.
    /// </summary>
    public DbSet<StrongEntryTranslation> StrongEntryTranslations { get; set; } = null!;

    /// <summary>
    /// A lexicon's short glosses, keyed by the dictionary form and the number it files each under.
    /// </summary>
    public DbSet<LexiconGloss> LexiconGlosses { get; set; } = null!;

    /// <summary>
    /// Strong numbers proposed for a word, with what proposed them. Separate from
    /// <c>word.strong_number</c>, which means a source stated it.
    /// </summary>
    public DbSet<WordStrong> WordStrongs { get; set; } = null!;

    /// <summary>
    /// Second analyses of a word, from a source other than the text it belongs to. Separate from
    /// <c>word.morphology</c>, which is what that word's own edition says, and never merged into
    /// it: two morphologies that disagree are a finding, not a field to overwrite.
    /// </summary>
    public DbSet<WordParsing> WordParsings { get; set; } = null!;

    /// <summary>
    /// Which peoples the dictionary says are named after whom, read out of its own prose. Keyed on
    /// the lexeme rather than on two entities, because the claim is about two words; both ends
    /// reach a page where the encyclopedia holds one.
    /// </summary>
    public DbSet<StrongGentilic> StrongGentilics { get; set; } = null!;

    /// <summary>What each load measured about the corpus it wrote, one row per load.</summary>
    public DbSet<VerificationRun> VerificationRuns { get; set; } = null!;

    /// <summary>The label a released corpus carries inside its dump. Empty in a working copy.</summary>
    public DbSet<CorpusRelease> CorpusReleases { get; set; } = null!;

    /// <summary>The people, places and peoples the text names, and where it names them.</summary>
    public DbSet<Entity> Entities { get; set; } = null!;

    public DbSet<EntityName> EntityNames { get; set; } = null!;

    public DbSet<EntityRelationship> EntityRelationships { get; set; } = null!;

    public DbSet<EntityVerse> EntityVerses { get; set; } = null!;

    /// <summary>
    /// What established a record this corpus wrote itself. Empty for every record a dataset
    /// supplied, whose <c>source</c> is the whole answer.
    /// </summary>
    public DbSet<EntityClaim> EntityClaims { get; set; } = null!;

    /// <summary>Who else a record might be, where the evidence does not decide.</summary>
    public DbSet<EntityAlternative> EntityAlternatives { get; set; } = null!;

    /// <summary>Who the text gives a title to, and the verse where it does.</summary>
    public DbSet<TitleBearer> TitleBearers { get; set; } = null!;

    /// <summary>Where a place is, as one point, and whose coordinates they are.</summary>
    public DbSet<PlaceLocation> PlaceLocations { get; set; } = null!;

    /// <summary>The pictures of people and places, each credited, and whether it is ours.</summary>
    public DbSet<EntityImage> EntityImages { get; set; } = null!;

    /// <summary>
    /// The ordered clauses this corpus says an entity is, out of which its description is rendered
    /// in whatever language a reader asks for.
    /// </summary>
    public DbSet<EntityDescriptor> EntityDescriptors { get; set; } = null!;

    /// <summary>Every method that says a clause is true, the way link claims do for links.</summary>
    public DbSet<EntityDescriptorClaim> EntityDescriptorClaims { get; set; } = null!;

    /// <summary>An entity's name in a reader's language, in the case a phrase puts it in.</summary>
    public DbSet<EntityNameForm> EntityNameForms { get; set; } = null!;

    /// <summary>Each text's spellings of an entity's name, counted from the words that name it.</summary>
    public DbSet<EntityRendering> EntityRenderings { get; set; } = null!;

    /// <summary>
    /// Which word names which person, place or people. The encyclopedia says a verse names
    /// somebody; this says which word of it does, which is what a reader hovering a word is asking.
    /// </summary>
    public DbSet<WordEntity> WordEntities { get; set; } = null!;

    /// <summary>Every method that says a word names an entity, the way link claims do for links.</summary>
    public DbSet<WordEntityClaim> WordEntityClaims { get; set; } = null!;

    public DbSet<Event> Events { get; set; } = null!;

    /// <summary>Whose reckoning a date belongs to, and the dates themselves.</summary>
    public DbSet<Chronology> Chronologies { get; set; } = null!;

    public DbSet<Period> Periods => Set<Period>();

    public DbSet<EventDate> EventDates { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        NameTablesInTheSingular(modelBuilder);

        // A people's origin going away must not take the people with it: the Moabites do not stop
        // existing because the record for the man Moab was merged into another, and the claim on
        // the record still says in Strong's words whom they are named after.
        modelBuilder.Entity<Entity>(entity =>
        {
            entity.Property(e => e.Kind).HasConversion(EnumStorage.EntityKind);

            entity.HasOne(e => e.Origin)
                .WithMany()
                .HasForeignKey(e => e.OriginEntityId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // A name deferring to another record must not take the name with it when that record goes:
        // the row is still Mount Zion's name and still carries its number, and all that is lost is
        // the statement of whose name it is.
        modelBuilder.Entity<EntityName>(entity =>
        {
            entity.HasOne(n => n.AspectOf)
                .WithMany()
                .HasForeignKey(n => n.AspectOfEntityId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EntityRelationship>(entity =>
        {
            entity.Property(r => r.Method).HasConversion(EnumStorage.LinkMethod);

            entity.HasOne(r => r.From).WithMany().HasForeignKey(r => r.FromEntityId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(r => r.To).WithMany().HasForeignKey(r => r.ToEntityId)
                .OnDelete(DeleteBehavior.Cascade);

            // A relationship this corpus concludes for itself names the verse it read; one carried
            // in from a witness names whatever the witness gave, which for 40 of BibleData's rows
            // is nothing. Writing an address for those would be a citation a reader cannot follow
            // dressed as one they can, so the asymmetry is kept and only our own half of it is
            // enforced.
            entity.ToTable(
                "entity_relationship",
                t =>
                {
                    AddProvenanceConstraints(t, "entity_relationship").HasCheckConstraint(
                        "ck_entity_relationship_read_names_a_verse",
                        $"\"method\" = '{EnumSpelling.Of(LinkMethod.StatedBySource)}' "
                        + "OR (\"canonical_book\" IS NOT NULL AND \"canonical_chapter\" IS NOT NULL "
                        + "AND \"canonical_verse\" IS NOT NULL)");

                    t.HasComment(
                        "One entity standing in one relation to another. Two witnesses speak here "
                        + "and every row says which: BibleData's edge list under its own category "
                        + "and its own relation names, and the clauses this corpus read from "
                        + "Scripture under theirs. Nothing settles them into one row -- what a "
                        + "reader is shown is settled the way an annotation is, by claim standing "
                        + "and then confidence.");
                });
        });

        modelBuilder.Entity<EntityClaim>(entity =>
        {
            entity.Property(c => c.Method).HasConversion(EnumStorage.LinkMethod);

            entity.HasOne(c => c.Entity)
                .WithMany(e => e.Claims)
                .HasForeignKey(c => c.EntityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("entity_claim", t => AddProvenanceConstraints(t, "entity_claim"));
        });

        // An alternative going away must not take the record with it: the doubt is the record's,
        // and a reader is better served by "may be somebody this corpus no longer holds" than by
        // the whole person disappearing because a duplicate was merged away.
        modelBuilder.Entity<PlaceLocation>(entity =>
        {
            entity.HasOne(l => l.Entity)
                .WithOne(e => e.Location)
                .HasForeignKey<PlaceLocation>(l => l.EntityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("place_location", t =>
            {
                t.HasCheckConstraint("ck_place_location_longitude", "longitude BETWEEN -180 AND 180");
                t.HasCheckConstraint("ck_place_location_latitude", "latitude BETWEEN -90 AND 90");
                t.HasCheckConstraint(
                    "ck_place_location_kind",
                    "kind IN ('point', 'representative-point', 'center', 'settlement')");
            });
        });

        modelBuilder.Entity<EntityImage>(entity =>
        {
            entity.HasOne(i => i.Entity)
                .WithMany(e => e.Images)
                .HasForeignKey(i => i.EntityId)
                .OnDelete(DeleteBehavior.Cascade);

            // One picture a page leads with, of each kind: the public one and our own.
            entity.HasIndex(i => new { i.EntityId, i.Kind })
                .IsUnique()
                .HasFilter("role = 'primary'")
                .HasDatabaseName("ix_entity_image_one_primary_per_kind");

            entity.ToTable("entity_image", t =>
            {
                t.HasCheckConstraint("ck_entity_image_kind", "kind IN ('public', 'generated')");
                t.HasCheckConstraint("ck_entity_image_role", "role IN ('primary', 'gallery')");
                t.HasCheckConstraint("ck_entity_image_credit", "length(credit) > 0 AND length(licence) > 0");
                t.HasCheckConstraint("ck_entity_image_size", "width > 0 AND height > 0");
                t.HasCheckConstraint(
                    "ck_entity_image_focus",
                    "(focus_x IS NULL OR focus_x BETWEEN 0 AND 1) AND (focus_y IS NULL OR focus_y BETWEEN 0 AND 1)");
            });
        });

        modelBuilder.Entity<EntityAlternative>(entity =>
        {
            entity.HasOne(a => a.Entity)
                .WithMany(e => e.Alternatives)
                .HasForeignKey(a => a.EntityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.Alternative)
                .WithMany()
                .HasForeignKey(a => a.AlternativeEntityId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Either end going away takes the row with it: a bearer is a statement about both records.
        modelBuilder.Entity<TitleBearer>(entity =>
        {
            entity.HasOne(b => b.Title)
                .WithMany()
                .HasForeignKey(b => b.TitleEntityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(b => b.Bearer)
                .WithMany()
                .HasForeignKey(b => b.BearerEntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // The claim is the dictionary's and the entity is only where it lands, so an entity going
        // away takes the link with it and leaves the sentence standing.
        modelBuilder.Entity<StrongGentilic>(entity =>
        {
            entity.HasOne(g => g.Origin)
                .WithMany()
                .HasForeignKey(g => g.OriginEntityId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(g => g.People)
                .WithMany()
                .HasForeignKey(g => g.PeopleEntityId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // The dictionary in a reader's language. Three of the four provenance constraints hold
        // here; the fourth, which requires an inference to carry a confidence, does not, and the
        // table comment says why rather than leaving a reader of the schema to wonder which of the
        // two rules was forgotten.
        modelBuilder.Entity<StrongEntryTranslation>(entity =>
        {
            entity.Property(t => t.Method).HasConversion(EnumStorage.LinkMethod);

            entity.ToTable("strong_entry_translation", table =>
            {
                table.HasCheckConstraint(
                    "ck_strong_entry_translation_confidence_range",
                    "\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)");

                table.HasCheckConstraint(
                    "ck_strong_entry_translation_source_not_empty",
                    "length(btrim(\"source\")) > 0");

                table.HasCheckConstraint(
                    "ck_strong_entry_translation_says_something",
                    "\"definition\" IS NOT NULL OR \"derivation\" IS NOT NULL "
                    + "OR \"kjv_definition\" IS NOT NULL OR \"detailed_definition\" IS NOT NULL");

                table.HasComment(
                    "Strong's four prose fields in one other language, next to the English and "
                    + "never over it. Only these four are language; the lemma, the transliteration, "
                    + "the morphology code and the see-also numbers are identifiers and are not "
                    + "here, because a translated identifier breaks a lookup silently. A row is a "
                    + "machine's reading of Strong rather than Strong in another language, and "
                    + "source says which machine, under which prompt, on which day. It carries no "
                    + "confidence where every other inference in this corpus must: there is no "
                    + "candidate set to be sure between, and what a reader checks it against is the "
                    + "English on strong_entry, not a number nobody measured.");
            });
        });

        modelBuilder.Entity<LexiconGloss>(entity =>
        {
            entity.ToTable("lexicon_gloss", table =>
            {
                table.HasCheckConstraint(
                    "ck_lexicon_gloss_source_not_empty",
                    "length(btrim(\"source\")) > 0");

                table.HasComment(
                    "A lexicon's short gloss for each dictionary form of each entry, filed under the "
                    + "number the lexicon gives it. Which word a gloss belongs to is not stored: a "
                    + "reading reaches it by the word's lemma or number and says which way.");
            });
        });

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

        modelBuilder.Entity<VerseNote>(entity =>
        {
            entity.Property(note => note.Kind).HasConversion(EnumStorage.VerseNoteKind);

            entity.HasOne(note => note.Verse)
                .WithMany(verse => verse.Notes)
                .HasForeignKey(note => note.VerseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(note => note.AnchorWord)
                .WithMany()
                .HasForeignKey(note => note.AnchorWordId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.ToTable("verse_note", table => table.HasCheckConstraint(
                "ck_verse_note_content_not_empty", "length(btrim(\"content\")) > 0"));
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

            entity.Property(w => w.Break).HasConversion(EnumStorage.TextBreak);
        });

        modelBuilder.Entity<VerseReference>(entity =>
        {
            entity.HasOne(r => r.Verse)
                .WithMany(v => v.References)
                .HasForeignKey(r => r.VerseId)
                .OnDelete(DeleteBehavior.Cascade);

            // Exactly one primary placement per verse. The other invariant — that no two verses of
            // one text claim the same primary placement — spans a join and belongs to the
            // verification pass.
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
        ConfigureWordParsing(modelBuilder);
        ConfigureWordEntity(modelBuilder);
        ConfigureEntityDescriptor(modelBuilder);
        ConfigureEvidentia(modelBuilder);
    }

    private static void ConfigureEvidentia(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EvidentiaRun>(entity =>
        {
            entity.HasOne(r => r.FromText)
                .WithMany()
                .HasForeignKey(r => r.FromTextId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.ToText)
                .WithMany()
                .HasForeignKey(r => r.ToTextId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.ParentRun)
                .WithMany()
                .HasForeignKey(r => r.ParentRunId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(r => r.Configuration).HasColumnType("jsonb");
            entity.Property(r => r.Scope).HasColumnType("jsonb");
        });

        modelBuilder.Entity<EvidentiaDecision>(entity =>
        {
            entity.Property(d => d.Abstention).HasConversion(EnumStorage.EvidentiaAbstention);

            entity.HasOne(d => d.Run)
                .WithMany(r => r.Decisions)
                .HasForeignKey(d => d.RunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.SourceWord)
                .WithMany()
                .HasForeignKey(d => d.SourceWordId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.TargetWord)
                .WithMany()
                .HasForeignKey(d => d.TargetWordId)
                .OnDelete(DeleteBehavior.Cascade);

            // A proposal says what it is and how sure; an abstention says why. Neither may be stored
            // as the other, because the problem-verse ranking counts them apart.
            entity.ToTable("evidentia_decision", table =>
            {
                table.HasCheckConstraint(
                    "ck_evidentia_decision_proposal_is_described",
                    "\"target_word_id\" IS NULL OR (\"kind\" IS NOT NULL AND \"confidence\" IS NOT NULL AND \"abstention\" IS NULL)");
                table.HasCheckConstraint(
                    "ck_evidentia_decision_abstention_has_reason",
                    "\"target_word_id\" IS NOT NULL OR (\"abstention\" IS NOT NULL AND \"kind\" IS NULL)");
                table.HasCheckConstraint(
                    "ck_evidentia_decision_confidence_range",
                    "\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)");
            });
        });

        modelBuilder.Entity<EvidentiaReview>(entity =>
        {
            entity.Property(r => r.Verdict).HasConversion(EnumStorage.EvidentiaVerdict);

            entity.HasOne(r => r.Decision)
                .WithOne(d => d.Review)
                .HasForeignKey<EvidentiaReview>(r => r.DecisionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.CorrectedTargetWord)
                .WithMany()
                .HasForeignKey(r => r.CorrectedTargetWordId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.Link)
                .WithMany()
                .HasForeignKey(r => r.LinkId)
                .OnDelete(DeleteBehavior.SetNull);

            var corrected = EnumSpelling.Of(EvidentiaVerdict.Corrected);
            var approved = EnumSpelling.Of(EvidentiaVerdict.Approved);
            var rejected = EnumSpelling.Of(EvidentiaVerdict.Rejected);
            entity.ToTable("evidentia_review", table =>
            {
                table.HasCheckConstraint(
                    "ck_evidentia_review_correction_names_its_word",
                    $"(\"verdict\" = '{corrected}') = (\"corrected_target_word_id\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_evidentia_review_only_an_approval_may_be_unexamined",
                    $"\"examined\" OR \"verdict\" = '{approved}'");
                table.HasCheckConstraint(
                    "ck_evidentia_review_a_rejection_is_never_applied",
                    $"\"verdict\" <> '{rejected}' OR (\"applied_at\" IS NULL AND \"link_id\" IS NULL)");
                table.HasCheckConstraint(
                    "ck_evidentia_review_reviewer_not_empty",
                    "length(btrim(\"reviewer\")) > 0");
            });
        });
    }

    /// <summary>
    /// The description this corpus writes for itself, and the name forms a language needs to render
    /// it. Both cascade from the entity, and a clause cascades from its target too: a clause whose
    /// target is gone names nothing, and prose is exactly what it must not fall back to.
    /// </summary>
    private static void ConfigureEntityDescriptor(ModelBuilder modelBuilder)
    {
        // Said on the table rather than only in the source, because the reader of a schema is
        // usually somebody who has just found an unused-looking column and is deciding about it.
        modelBuilder.Entity<Entity>()
            .Property(e => e.Distinguisher)
            .HasComment(
                "The imported one-line description, in English, in the words of whichever dataset "
                + "supplied it. It is no longer what a reader is shown — entity_descriptor is — and "
                + "it is not dead: it is the record as imported, it is what the generated clauses "
                + "are measured against, and it is the only description an entity nothing has been "
                + "generated for has. Nothing writes it but the dataset loaders.");

        modelBuilder.Entity<EntityDescriptor>(entity =>
        {
            entity.Property(d => d.Method).HasConversion(EnumStorage.LinkMethod);

            entity.HasOne(d => d.Entity)
                .WithMany()
                .HasForeignKey(d => d.EntityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.Target)
                .WithMany()
                .HasForeignKey(d => d.TargetEntityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable(
                "entity_descriptor",
                t => AddProvenanceConstraints(t, "entity_descriptor").HasComment(
                    "One clause of what this corpus says an entity is, in its own voice: a "
                    + "relation, an entity it holds, and the verse it was read from. The line a "
                    + "reader sees is rendered from these per language, so every name in it is a "
                    + "link. It replaces what entity.distinguisher was shown for; that column "
                    + "stays, unchanged and unread by this layer."));
        });

        modelBuilder.Entity<EntityDescriptorClaim>(entity =>
        {
            entity.Property(c => c.Method).HasConversion(EnumStorage.LinkMethod);

            entity.HasOne(c => c.EntityDescriptor)
                .WithMany(d => d.Claims)
                .HasForeignKey(c => c.EntityDescriptorId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable(
                "entity_descriptor_claim",
                t => AddProvenanceConstraints(t, "entity_descriptor_claim"));
        });

        modelBuilder.Entity<EntityNameForm>(entity =>
        {
            entity.Property(f => f.Method).HasConversion(EnumStorage.LinkMethod);

            entity.HasOne(f => f.Entity)
                .WithMany()
                .HasForeignKey(f => f.EntityId)
                .OnDelete(DeleteBehavior.Cascade);

            // The encyclopedia's search reads every form for part of a name, in any language.
            entity.HasIndex(f => f.Form)
                .HasMethod("gin")
                .HasOperators("gin_trgm_ops");

            entity.ToTable(
                "entity_name_form",
                t => AddProvenanceConstraints(t, "entity_name_form").HasComment(
                    "An entity's name in a reader's language, in the grammatical case a phrase "
                    + "puts it in. Produced with the name and never computed from it: a stemmer "
                    + "guessing the genitive of a Hebrew proper name is wrong often and silently."));
        });

        modelBuilder.Entity<EntityRendering>(entity =>
        {
            entity.HasOne(r => r.Entity)
                .WithMany()
                .HasForeignKey(r => r.EntityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.Text)
                .WithMany()
                .HasForeignKey(r => r.TextId)
                .OnDelete(DeleteBehavior.Cascade);

            // A search types part of a name, so the lookup is a substring and only a trigram index
            // can answer that without reading every row.
            entity.HasIndex(r => r.Folded)
                .HasMethod("gin")
                .HasOperators("gin_trgm_ops");

            entity.ToTable("entity_rendering", t =>
            {
                t.HasCheckConstraint("ck_entity_rendering_occurrences", "occurrences > 0");
                t.HasComment(
                    "How one text spells an entity's name, counted from the words word_entity says "
                    + "name it there. Derived and rebuilt with those annotations; it asserts nothing "
                    + "they do not.");
            });
        });
    }

    /// <summary>
    /// A table is named for what one of its rows is, and the design document, every measurement
    /// taken against this corpus and every query in it are written that way: <c>from link</c>,
    /// <c>from verse_reference</c>. Left to the DbSet names, EF would pluralise them all.
    ///
    /// Derived from the entity type rather than listed. The list was written by hand, so a new
    /// entity opted out of the convention by nobody remembering it — two tables were created
    /// plural before anyone noticed. A convention cannot be forgotten.
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

    private static void ConfigureWordParsing(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WordParsing>(entity =>
        {
            entity.Property(p => p.Method).HasConversion(EnumStorage.LinkMethod);

            entity.HasOne(p => p.Word)
                .WithMany()
                .HasForeignKey(p => p.WordId)
                .OnDelete(DeleteBehavior.Cascade);

            // The same rules again, and here they carry the weight: a second morphology is exactly
            // the kind of claim that reads as scholarship, and most of the reasoning that put one
            // on a word is the reasoning about which word it belongs to.
            entity.ToTable("word_parsing", t => AddProvenanceConstraints(t, "word_parsing"));
        });
    }

    /// <summary>
    /// The annotation and its claims live under the same rules the links do, and for the same
    /// reason: an annotation that came from resolving a lexicon entry and carried no confidence
    /// would be stored looking exactly like one a source stated about the word, and no reader could
    /// tell them apart afterwards.
    /// </summary>
    private static void ConfigureWordEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WordEntity>(entity =>
        {
            entity.Property(a => a.Method).HasConversion(EnumStorage.LinkMethod);

            entity.HasOne(a => a.Word)
                .WithMany()
                .HasForeignKey(a => a.WordId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.Entity)
                .WithMany()
                .HasForeignKey(a => a.EntityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("word_entity", t => AddProvenanceConstraints(t, "word_entity"));
        });

        modelBuilder.Entity<WordEntityClaim>(entity =>
        {
            entity.Property(c => c.Method).HasConversion(EnumStorage.LinkMethod);

            entity.HasOne(c => c.WordEntity)
                .WithMany(a => a.Claims)
                .HasForeignKey(c => c.WordEntityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("word_entity_claim", t => AddProvenanceConstraints(t, "word_entity_claim"));
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
