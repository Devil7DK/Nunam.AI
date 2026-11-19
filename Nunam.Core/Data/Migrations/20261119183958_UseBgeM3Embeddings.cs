using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Nunam.Core.Data.Migrations
{
    /// <summary>
    /// Moves embeddings from nomic-embed-text (768 dimensions) to bge-m3 (1024), which retrieved
    /// noticeably better on the sample set and matches across Tamil and English. Old vectors can't be
    /// converted, so they are cleared and every file is queued to be embedded again; files stay
    /// keyword-searchable meanwhile.
    /// </summary>
    public partial class UseBgeM3Embeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => Resize(migrationBuilder, from: 768, to: 1024);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => Resize(migrationBuilder, from: 1024, to: 768);

        private static void Resize(MigrationBuilder migrationBuilder, int from, int to)
        {
            migrationBuilder.Sql("UPDATE files SET embedding = NULL WHERE embedding IS NOT NULL;");
            migrationBuilder.Sql("DELETE FROM index_jobs WHERE kind = 'Embed' AND status IN ('Pending', 'Failed');");

            migrationBuilder.AlterColumn<Vector>(
                name: "embedding",
                table: "files",
                type: $"vector({to})",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: $"vector({from})",
                oldNullable: true);

            migrationBuilder.Sql("""
                INSERT INTO index_jobs (id, machine_id, path, kind, status, stage, created, last_modified)
                SELECT gen_random_uuid(), machine_id, path, 'Embed', 'Pending', 'None', now(), now()
                FROM files;
                """);
        }
    }
}
