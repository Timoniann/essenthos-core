using System.Reflection;
using Essenthos.Core.Build;
using Essenthos.Core.Database;
using Essenthos.Core.Products;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>
/// What <c>build --apply</c> does with a database behind the Forge's migrations: each migration's operations read for the tables
/// they touch, each touched table mapped to the load that fills it whole, and what only a fresh build fills refused.
/// </summary>
public sealed class ApplyReloadsTests
{
    private static readonly ProductGraph Graph = ProductGraph.Read(new ProductStore(TestResources.Checkout("Resources")));

    private static (ApplyReloads Reloads, string? Refused, SeedMigration.Reading Reading) Plan(params string[] migrations)
    {
        var reading = new SeedMigration.Reading();
        foreach (var id in migrations)
        {
            SeedMigration.Read(id, Migration(id).UpOperations, reading);
        }

        var reloads = new ApplyReloads();
        var refused = reading.Refused ?? reloads.Reload(reading.Touched.Keys.Where(t => !reading.Dropped.Contains(t)), Graph, "test");
        return (reloads, refused, reading);
    }

    private static Migration Migration(string id)
    {
        var type = typeof(AppDbContext).Assembly.GetTypes()
            .Single(t => t.IsSubclassOf(typeof(Migration)) && t.GetCustomAttribute<MigrationAttribute>()?.Id == id);
        return (Migration)Activator.CreateInstance(type)!;
    }

    [Fact]
    public void An_index_only_migration_reloads_nothing()
    {
        var (reloads, refused, _) = Plan("20261109174415_WhichTextsALinkJoinsIsReadFromAnIndex", "20261109182106_TheChapterContextReadsATranslationsExceptionsFromAnIndex");

        refused.Should().BeNull();
        reloads.IsEmpty.Should().BeTrue("an index holds no row");
    }

    [Fact]
    public void A_column_a_load_fills_reloads_that_load_whole()
    {
        var (reloads, refused, _) = Plan("20261104142548_AWordsNameSaysItsRoleAndWhereItCameFrom");

        refused.Should().BeNull();
        reloads.Names.Should().BeTrue();
        reloads.Records.Should().BeFalse();
    }

    [Fact]
    public void New_tables_of_the_records_the_indexes_the_tables_and_the_serving_stage_go_to_their_loads()
    {
        Plan("20261117145914_EventOrderIsStored").Reloads.Records.Should().BeTrue("the events' columns are loaded by the record files");
        Plan("20261113133801_EachSpellingsRenderingsAreCountedAtBuild").Reloads.Renderings.Should().BeTrue();
        Plan("20261116111145_ACommentIsWrittenOnAPassage").Reloads.Carried.Should().Equal("commentary_entry");
        Plan("20261115100446_EachRecordAndStrongPageIsOneStoredDocument").Reloads.Serve.Should().Equal("page_document");
        Plan("20261117193408_TranslationWordsCarryTheStrongNumbersTheyRender").Reloads.Numbers.Should().BeTrue();

        var (pages, refused, _) = Plan("20261117181724_StrongPagesReadPairCountsBookSumsAndPlacesTheBuildStored");
        refused.Should().BeNull();
        pages.Strong.Should().BeTrue("text_book_linked and strong_book_words are counted by the Strong index load");
        pages.StoredStrong.Should().BeTrue();
    }

    [Fact]
    public void A_dropped_table_needs_no_load()
    {
        var (reloads, refused, reading) = Plan("20261105184921_TheStepLoadsMarksAreGone");

        refused.Should().BeNull();
        reading.Dropped.Should().Contain("load_step_mark");
        reloads.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void What_only_a_fresh_build_fills_is_refused()
    {
        Plan("20261114233048_ALinkKeepsItsMethodsAndASourceItsClaimCount").Refused.Should().Contain("links' load");
        Plan("20261117191524_LinkWordsCarryTheirWordsVerse").Refused.Should().Contain("link_word");
        Plan("20261116015047_RowsAreKeyedByWhatTheyAre").Refused.Should().NotBeNull("word_entity_claim is filled by no load");
    }

    [Fact]
    public void Sql_is_read_for_the_tables_it_writes_and_refused_where_it_cannot_be()
    {
        var reading = new SeedMigration.Reading();
        SeedMigration.ReadSql("m", """
            UPDATE event_date d SET axis_year = 1 FROM chronology c WHERE c.id = d.chronology_id;
            INSERT INTO public.text_book_linked (text_id, book) SELECT 1, 2;
            TRUNCATE word_entity_claim, "verse_strength" RESTART IDENTITY;
            ALTER TABLE link_word ADD COLUMN x int REFERENCES verse (id) ON DELETE CASCADE ON UPDATE CASCADE;
            CREATE TRIGGER t BEFORE INSERT OR UPDATE ON pane FOR EACH ROW EXECUTE FUNCTION f();
            CREATE INDEX ix_word_x ON word (x);
            DROP TABLE IF EXISTS carry_mark;
            -- DELETE FROM text
            """, reading);

        reading.Refused.Should().BeNull();
        reading.Touched.Keys.Should().BeEquivalentTo("event_date", "text_book_linked", "word_entity_claim", "verse_strength", "link_word", "pane");
        reading.Dropped.Should().Equal("carry_mark");

        var dynamic = new SeedMigration.Reading();
        SeedMigration.ReadSql("m", "DO $$ BEGIN EXECUTE format('DELETE FROM %I', 'word'); END $$;", dynamic);
        dynamic.Refused.Should().Contain("EXECUTE");
    }

    [Fact]
    public void A_word_group_table_is_refused_because_the_words_fill_it_too()
    {
        new ApplyReloads().Reload(["word_group"], Graph, "test").Should().NotBeNull("the words fill it too");
        new ApplyReloads().Reload(["strong_entry"], Graph, "test").Should().BeNull();
        new ApplyReloads().Reload(["word"], Graph, "test").Should().Contain("texts' load");
    }

    [Fact]
    public void A_load_whose_code_moved_reloads_its_own_tables_and_a_texts_or_links_load_refuses()
    {
        ApplyReloads Moved(string name, out string? refused)
        {
            var reloads = new ApplyReloads();
            refused = reloads.Reload(LoadCode.Tables(LoadCode.All.Single(load => load.Load == name)), Graph, "test");
            return reloads;
        }

        Moved("records", out var refused).Records.Should().BeTrue();
        refused.Should().BeNull();
        var names = Moved("names", out refused);
        (names.Names, names.Records, refused).Should().Be((true, false, (string?)null));
        var indexes = Moved("indexes", out refused);
        (indexes.Strong, indexes.Forms, indexes.Renderings, indexes.Spellings, indexes.Names, refused)
            .Should().Be((true, true, true, true, false, (string?)null));
        Moved("numbers", out refused).Numbers.Should().BeTrue();
        Moved("stored-strong", out refused).StoredStrong.Should().BeTrue();
        Moved("frames", out refused).Frames.Should().BeTrue();
        Moved("verse-links", out refused).VerseLinks.Should().BeTrue();
        refused.Should().BeNull();
        foreach (var fresh in new[] { "texts", "links", "carried" })
        {
            Moved(fresh, out refused);
            refused.Should().NotBeNull($"only a fresh build fills what the {fresh} load fills");
        }
    }

    [Fact]
    public void A_served_state_names_the_loads_whose_code_moved_and_none_where_it_records_no_code()
    {
        var served = new ProductHeader("Essenthos/build/served/test", [], "test");
        LoadCode.Recorded(served).Should().BeFalse();
        served.Notes.AddRange(LoadCode.Notes);
        LoadCode.Recorded(served).Should().BeTrue();
        LoadCode.Moved(served).Should().BeEmpty();

        var names = served.Notes.FindIndex(note => note.Key == "code-names");
        served.Notes[names] = new(served.Notes[names].Key, "0000000000000000");
        LoadCode.Moved(served).Select(load => load.Load).Should().Equal("names");
    }
}
