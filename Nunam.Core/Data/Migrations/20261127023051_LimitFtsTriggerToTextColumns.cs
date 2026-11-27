using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nunam.Core.Data.Migrations
{
    /// <summary>
    /// The fts trigger fired on every update of a file, rebuilding its full-text vector when only the
    /// embedding or the description's credit changed.
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
