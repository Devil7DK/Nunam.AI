using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;
using Pgvector;

#nullable disable

namespace Nunam.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "machines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    hostname = table.Column<string>(type: "text", nullable: false),
                    last_seen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    machine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    extension = table.Column<string>(type: "text", nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    file_type = table.Column<string>(type: "text", nullable: false),
                    file_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    file_modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    indexed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    preview_image = table.Column<byte[]>(type: "bytea", nullable: true),
                    embedding = table.Column<Vector>(type: "vector(768)", nullable: true),
                    fts = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true),
                    created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => x.id);
                    table.UniqueConstraint("ak_files_machine_id_path", x => new { x.machine_id, x.path });
                    table.ForeignKey(
                        name: "fk_files_machines_machine_id",
                        column: x => x.machine_id,
                        principalTable: "machines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "index_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    machine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    stage = table.Column<string>(type: "text", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_index_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_index_jobs_machines_machine_id",
                        column: x => x.machine_id,
                        principalTable: "machines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_files_embedding",
                table: "files",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_files_extension",
                table: "files",
                column: "extension");

            migrationBuilder.CreateIndex(
                name: "ix_files_file_modified_at",
                table: "files",
                column: "file_modified_at");

            migrationBuilder.CreateIndex(
                name: "ix_files_file_type",
                table: "files",
                column: "file_type");

            migrationBuilder.CreateIndex(
                name: "ix_files_fts",
                table: "files",
                column: "fts")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_files_machine_id",
                table: "files",
                column: "machine_id");

            migrationBuilder.CreateIndex(
                name: "ix_index_jobs_kind_status",
                table: "index_jobs",
                columns: new[] { "kind", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_index_jobs_machine_id",
                table: "index_jobs",
                column: "machine_id");

            migrationBuilder.CreateIndex(
                name: "ix_machines_hostname",
                table: "machines",
                column: "hostname",
                unique: true);

            // The `fts` column on `files` is maintained by a database trigger because
            // it can't be expressed in EF Core fluent API. Concatenates filename + content
            // + description and runs to_tsvector('english', ...) before every insert/update.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION update_fts() RETURNS trigger AS $$
                BEGIN
                    NEW.fts := to_tsvector('english',
                        coalesce(NEW.file_name,'')   || ' ' ||
                        coalesce(NEW.content,'')     || ' ' ||
                        coalesce(NEW.description,''));
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_update_fts
                BEFORE INSERT OR UPDATE ON files
                FOR EACH ROW EXECUTE FUNCTION update_fts();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_update_fts ON files;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS update_fts();");

            migrationBuilder.DropTable(
                name: "files");

            migrationBuilder.DropTable(
                name: "index_jobs");

            migrationBuilder.DropTable(
                name: "machines");
        }
    }
}
