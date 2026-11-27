using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nunam.Core.Data.Migrations
{
    /// <summary>
    /// The fts trigger fired on every update of a file, rebuilding its full-text vector when only the
    /// embedding or the description's credit changed. Also credits descriptions written before
    /// providers were recorded to the first provider, which the server created from its AI settings.
    /// </summary>
    public partial class LimitFtsTriggerToTextColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_update_fts ON files;
                CREATE TRIGGER trg_update_fts
                BEFORE INSERT OR UPDATE OF file_name, content, description ON files
                FOR EACH ROW EXECUTE FUNCTION update_fts();

                UPDATE files f
                SET description_model = p.model, description_provider = p.name
                FROM (SELECT model, name FROM describe_providers ORDER BY created LIMIT 1) p
                WHERE f.description IS NOT NULL AND f.description_model IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_update_fts ON files;
                CREATE TRIGGER trg_update_fts
                BEFORE INSERT OR UPDATE ON files
                FOR EACH ROW EXECUTE FUNCTION update_fts();
                """);
        }
    }
}
