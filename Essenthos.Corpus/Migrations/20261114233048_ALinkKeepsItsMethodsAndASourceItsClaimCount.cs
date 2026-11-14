using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <summary>
    /// What the readers need of <c>link_claim</c> kept where a served release still has it: each link's set of
    /// methods on the link, and each source's number of claims on the source. Both follow the claims through a
    /// trigger, as a link's fingerprint follows its words, so whoever writes a claim keeps them true; the mapping
    /// load, which writes with the triggers off, computes them itself.
    /// </summary>
    public partial class ALinkKeepsItsMethodsAndASourceItsClaimCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "claims",
                table: "provenance",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<short>(
                name: "methods",
                table: "link",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.Sql(LinkMethods.Function);

            // A claim added only adds its method; a claim taken away or changed has the link's set read again
            // from the claims left. The sources' counts move by the rows that came and went.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION link_claims_follow() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'TRUNCATE' THEN
                        UPDATE link SET methods = 0 WHERE methods <> 0;
                        UPDATE provenance SET claims = 0 WHERE claims <> 0;
                    ELSIF TG_OP = 'INSERT' THEN
                        UPDATE link l SET methods = l.methods | a.methods
                        FROM (SELECT link_id, bit_or(link_method_bit(method)) AS methods FROM added GROUP BY link_id) a
                        WHERE l.id = a.link_id AND l.methods | a.methods <> l.methods;
                        UPDATE provenance p SET claims = p.claims + a.claims
                        FROM (SELECT provenance_id, count(*)::integer AS claims FROM added GROUP BY provenance_id) a
                        WHERE p.id = a.provenance_id;
                    ELSIF TG_OP = 'DELETE' THEN
                        UPDATE link l SET methods = s.methods
                        FROM (SELECT t.id, coalesce(bit_or(link_method_bit(c.method)), 0)::smallint AS methods
                              FROM link t LEFT JOIN link_claim c ON c.link_id = t.id
                              WHERE t.id IN (SELECT link_id FROM removed)
                              GROUP BY t.id) s
                        WHERE l.id = s.id AND l.methods <> s.methods;
                        UPDATE provenance p SET claims = p.claims - r.claims
                        FROM (SELECT provenance_id, count(*)::integer AS claims FROM removed GROUP BY provenance_id) r
                        WHERE p.id = r.provenance_id;
                    ELSE
                        UPDATE link l SET methods = s.methods
                        FROM (SELECT t.id, coalesce(bit_or(link_method_bit(c.method)), 0)::smallint AS methods
                              FROM link t LEFT JOIN link_claim c ON c.link_id = t.id
                              WHERE t.id IN (SELECT link_id FROM removed UNION SELECT link_id FROM added)
                              GROUP BY t.id) s
                        WHERE l.id = s.id AND l.methods <> s.methods;
                        UPDATE provenance p SET claims = p.claims + d.claims
                        FROM (SELECT provenance_id, sum(n)::integer AS claims
                              FROM (SELECT provenance_id, 1 AS n FROM added UNION ALL SELECT provenance_id, -1 FROM removed) x
                              GROUP BY provenance_id HAVING sum(n) <> 0) d
                        WHERE p.id = d.provenance_id;
                    END IF;
                    RETURN NULL;
                END $$
                """);
            migrationBuilder.Sql(
                "CREATE OR REPLACE TRIGGER link_claim_added AFTER INSERT ON link_claim " +
                "REFERENCING NEW TABLE AS added FOR EACH STATEMENT EXECUTE FUNCTION link_claims_follow()");
            migrationBuilder.Sql(
                "CREATE OR REPLACE TRIGGER link_claim_removed AFTER DELETE ON link_claim " +
                "REFERENCING OLD TABLE AS removed FOR EACH STATEMENT EXECUTE FUNCTION link_claims_follow()");
            migrationBuilder.Sql(
                "CREATE OR REPLACE TRIGGER link_claim_changed AFTER UPDATE ON link_claim " +
                "REFERENCING OLD TABLE AS removed NEW TABLE AS added FOR EACH STATEMENT EXECUTE FUNCTION link_claims_follow()");
            migrationBuilder.Sql(
                "CREATE OR REPLACE TRIGGER link_claim_emptied AFTER TRUNCATE ON link_claim " +
                "FOR EACH STATEMENT EXECUTE FUNCTION link_claims_follow()");

            migrationBuilder.Sql(
                """
                UPDATE link l SET methods = c.methods
                FROM (SELECT link_id, bit_or(link_method_bit(method))::smallint AS methods FROM link_claim GROUP BY link_id) c
                WHERE l.id = c.link_id;
                UPDATE provenance p SET claims = c.claims
                FROM (SELECT provenance_id, count(*)::integer AS claims FROM link_claim GROUP BY provenance_id) c
                WHERE p.id = c.provenance_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS link_claim_added ON link_claim");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS link_claim_removed ON link_claim");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS link_claim_changed ON link_claim");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS link_claim_emptied ON link_claim");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS link_claims_follow()");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS link_method_bit(text)");

            migrationBuilder.DropColumn(
                name: "claims",
                table: "provenance");

            migrationBuilder.DropColumn(
                name: "methods",
                table: "link");
        }
    }
}
