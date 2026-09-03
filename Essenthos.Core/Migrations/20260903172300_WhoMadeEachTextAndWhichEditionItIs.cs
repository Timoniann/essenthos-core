using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class WhoMadeEachTextAndWhichEditionItIs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "about",
                table: "text",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "edition",
                table: "text",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "edition_year",
                table: "text",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "editors",
                table: "text",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rights_note",
                table: "text",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "translators",
                table: "text",
                type: "text",
                nullable: true);

            // The nine texts already loaded, brought level with the definitions under Loading/.
            //
            // A migration rather than a reload. Those definitions are what a fresh corpus is built
            // from and they remain the source of truth; but reloading a text to pick up six columns
            // cascades away every link into it, which for the King James is the whole Old Testament
            // word mapping.
            //
            // Each statement only touches a row that still holds nothing in the field it is about,
            // so a corpus where somebody has already written one of these keeps what they wrote.
            migrationBuilder.Sql(
                """
                UPDATE text SET
                    translators = 'The six companies of about forty-seven translators appointed by James VI and I',
                    edition = 'The modern standard text, not the 1611 printing',
                    edition_year = 1769,
                    about = 'Translated 1604-1611 by six companies working at Westminster, Oxford and Cambridge, from the Hebrew and the Greek printed editions of the day, and revised against the Bishops'' Bible. The file served here is not the 1611 printing: its spelling is modernised throughout, and Ruth 3:15 reads "and she went into the city", the reading the 1762 Cambridge and 1769 Oxford revisions introduced where 1611 has "he". Which of the later standard editions it follows exactly has not been established.'
                WHERE slug = 'kjv' AND translators IS NULL;

                UPDATE text SET
                    translators = 'The four Orthodox theological academies of Saint Petersburg, Moscow, Kazan and Kiev, under the Most Holy Synod of the Russian Orthodox Church',
                    editors = 'Filaret (Drozdov), Metropolitan of Moscow, who had the final editorship',
                    about = 'Begun in 1813 under the Russian Bible Society, halted in 1826 when the Society was dissolved, and resumed under Alexander II; the Synod approved translating the Old Testament from the Masoretic Text in 1862, and the complete Bible appeared in 1876. Where the Septuagint has words the Masoretic Text does not, the edition prints them in square brackets — 4,247 spans of them, which are loaded as the words this edition supplies rather than as text.'
                WHERE slug = 'rusv' AND translators IS NULL;

                -- The name changes with it. "Ukrainian Bible" names no translator and there is more
                -- than one Ukrainian Bible; this is Ohienko's, and nothing in the row said so.
                UPDATE text SET
                    name = 'Ohienko Bible',
                    name_native = 'Біблія в перекладі Івана Огієнка',
                    translators = 'Ivan Ohienko, Metropolitan Ilarion (1882-1972)',
                    rights_holder = 'British and Foreign Bible Society, which published the 1962 edition',
                    edition = 'The first complete edition, printed in London in 1962',
                    about = 'Ohienko began translating in 1917 and worked from the Hebrew and the Greek, deliberately clear of Russianisms. He signed a contract with the British and Foreign Bible Society in 1936; the Gospels appeared in 1937 and the rest of the New Testament with the Psalms in 1939; the complete text was finished in 1940 and, delayed by the war, first printed in London in 1962. That this file is his translation was established two ways: Genesis 1:1 reads "На початку Бог створив Небо та землю", and the file is 99.4% token-identical, verse by verse, with the uk_ubio text on Door43 whose every book header reads "Біблія в пер. Івана Огієнка, 1962".',
                    rights_note = 'Not settled. bible4u distributes the file as public domain, and CrossWire and Ukrainian Wikisource say the same — but each of the three rests on the others rather than on a grant. Against that: Ohienko died in 1972, and the sixteen Door43 files carrying the same text head every book "Copyright British and Foreign Bible Society". Whether the Society has released the 1962 edition has not been asked of them. Everything known about who made it and who published it is recorded here in the meantime.',
                    citation = 'Біблія в перекладі Івана Огієнка (Metropolitan Ilarion), first complete edition, British and Foreign Bible Society, London, 1962.'
                WHERE slug = 'ukr' AND translators IS NULL;

                UPDATE text SET
                    editors = 'The Eep Talstra Centre for Bible and Computer, VU Amsterdam; encoded for Text-Fabric by Dirk Roorda',
                    edition = 'ETCBC version 2021',
                    about = 'The consonantal and vocalised text of the Biblia Hebraica Stuttgartensia, which is the Masoretic Text as the Leningrad Codex preserves it, carrying the linguistic annotation the ETCBC and its predecessor the Werkgroep Informatica have been building since the 1970s: part of speech, stem, state, and the clause and phrase structure every syntactic query in this corpus is asked of. Nobody translated it; what is edited here is the encoding and the annotation, not the words.'
                WHERE slug = 'bhsa' AND editors IS NULL;

                UPDATE text SET
                    editors = 'Eberhard Nestle',
                    edition = 'The 1904 British and Foreign Bible Society printing',
                    about = 'Nestle collated no manuscripts for this text: he built it by combining the printed editions of Tischendorf, Westcott and Hort, and Weymouth, which is why it stands close to the modern critical text without being one. He published the first edition in 1898; the British and Foreign Bible Society printed the 1904 edition read here, and the Nestle name has been on their Greek New Testament ever since. The digital edition was transcribed by Diego Renato dos Santos, given its morphology by Ulrik Sandborg-Petersen and marked up by Jonathan Robie.'
                WHERE slug = 'nestle1904' AND editors IS NULL;

                UPDATE text SET
                    editors = 'Sir Lancelot Charles Lee Brenton',
                    edition = 'The Greek Brenton printed facing his English translation, following Codex Vaticanus',
                    about = 'The Greek here is not Brenton''s work in the way the English beside it is: he printed a text following Codex Vaticanus, and what he translated was that. Who first put these books into Greek is not known — the translation was made in Alexandria between roughly the third and the first century BC, by different hands book by book, which is why its books differ so much from one another in manner. Samuel Bagster and Sons published Brenton''s edition in London in 1844 and added the Apocrypha in 1851. It arrived here with no annotation at all; its lemmas come from GLAUx.'
                WHERE slug = 'lxx-brenton' AND editors IS NULL;

                UPDATE text SET
                    editors = 'Frederick Henry Ambrose Scrivener (1813-1891)',
                    edition = 'The 1894 Cambridge printing, published after his death',
                    about = 'Not an edition of the Greek in the ordinary sense but a reconstruction of one. The King James translators never published the Greek they worked from, and they did not follow a single edition: they took readings from Erasmus, Stephanus and Beza as they went. Scrivener worked backwards from the English, choosing at each point the reading those editions offered that the Authorised Version had followed. He first published it in 1881 as The New Testament in the Original Greek according to the Text followed in the Authorised Version; the 1894 printing read here appeared three years after his death.'
                WHERE slug = 'scrivener1894' AND editors IS NULL;

                UPDATE text SET
                    editors = 'Robert Estienne, who printed as Stephanus (1503-1559)',
                    edition = 'The 1550 editio regia, printed in Paris',
                    about = 'The third of the four Greek New Testaments Estienne edited, in 1546, 1549, 1550 and 1551, and the one that carried. It rests on Erasmus and on the Complutensian Polyglot, and prints variant readings in the margin from the dozen and more manuscripts he had reached, among them Codex Bezae. In England this is the edition that became the Received Text, which is why it and the Scrivener stand beside each other here: they are two states of one tradition, and the places where they differ are the places where the King James followed somebody else.'
                WHERE slug = 'stephanus1550' AND editors IS NULL;

                UPDATE text SET
                    translators = 'The Bible Hub and Discovery Bible teams',
                    editors = 'An advisory committee of Gary Hill (original languages), Grant Osborne (New Testament lead), Eugene H. Merrill (Old Testament lead), Maury Robertson, Ulrik Sandborg-Petersen and Baruch Korman',
                    about = 'A new translation rather than a revision of an existing one, made so that every English word could be traced back to the Greek or Hebrew behind it — which is why the translators publish their own word-level tables, and why those tables are the only stated English mapping this corpus has for the New Testament. The advisory committee directed the use of the sources and settled the translation decisions; the Bible Hub and Discovery Bible teams did the translating, styling and proofing under them. It was funded from Bible Hub''s advertising revenue, with no donors or publisher, and placed in the public domain on 30 April 2023.',
                    rights_note = 'The licensing page places the text in the public domain and adds that licensing is not required for any use. The footer of every page on the same site still reads "Copyright © 2021 Berean Standard Bible. All rights reserved." The licensing page is the specific and deliberate statement and the footer is template chrome, so the licensing page is the one believed — and both are recorded rather than one of them chosen quietly.'
                WHERE slug = 'bsb' AND translators IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the six columns loses what only they held. The Ukrainian row is the
            // exception: its name, its native name, its rights holder and its citation are older
            // columns this migration overwrote, so going back has to put them back.
            migrationBuilder.Sql(
                """
                UPDATE text SET
                    name = 'Ukrainian Bible',
                    name_native = 'Біблія',
                    rights_holder = NULL,
                    citation = NULL
                WHERE slug = 'ukr' AND name = 'Ohienko Bible';
                """);

            migrationBuilder.DropColumn(
                name: "about",
                table: "text");

            migrationBuilder.DropColumn(
                name: "edition",
                table: "text");

            migrationBuilder.DropColumn(
                name: "edition_year",
                table: "text");

            migrationBuilder.DropColumn(
                name: "editors",
                table: "text");

            migrationBuilder.DropColumn(
                name: "rights_note",
                table: "text");

            migrationBuilder.DropColumn(
                name: "translators",
                table: "text");
        }
    }
}
