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
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    token_issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                    disabled = table.Column<bool>(type: "boolean", nullable: false),
                    failed_sign_ins = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_sign_in = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
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
                    embedding = table.Column<Vector>(type: "vector(1024)", nullable: true),
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
                    attempts = table.Column<int>(type: "integer", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "indexer_commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    machine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    requested_by = table.Column<string>(type: "text", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    result = table.Column<string>(type: "text", nullable: true),
                    created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_indexer_commands", x => x.id);
                    table.ForeignKey(
                        name: "fk_indexer_commands_machines_machine_id",
                        column: x => x.machine_id,
                        principalTable: "machines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indexer_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    machine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    level = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    exception = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_indexer_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_indexer_logs_machines_machine_id",
                        column: x => x.machine_id,
                        principalTable: "machines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indexer_settings",
                columns: table => new
                {
                    machine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_indexer_settings", x => x.machine_id);
                    table.ForeignKey(
                        name: "fk_indexer_settings_machines_machine_id",
                        column: x => x.machine_id,
                        principalTable: "machines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indexer_status",
                columns: table => new
                {
                    machine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    process_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<string>(type: "text", nullable: false),
                    operating_system = table.Column<string>(type: "text", nullable: false),
                    service_account = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    remote_management = table.Column<bool>(type: "boolean", nullable: false),
                    applied_revision = table.Column<int>(type: "integer", nullable: true),
                    scan_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scan_finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    config = table.Column<string>(type: "jsonb", nullable: false),
                    watch_paths = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_indexer_status", x => x.machine_id);
                    table.ForeignKey(
                        name: "fk_indexer_status_machines_machine_id",
                        column: x => x.machine_id,
                        principalTable: "machines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "machine_enrollments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    machine_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    enrolled_machine_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machine_enrollments", x => x.id);
                    table.ForeignKey(
                        name: "fk_machine_enrollments_machines_machine_id",
                        column: x => x.machine_id,
                        principalTable: "machines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "machine_access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    machine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machine_access", x => x.id);
                    table.ForeignKey(
                        name: "fk_machine_access_machines_machine_id",
                        column: x => x.machine_id,
                        principalTable: "machines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_machine_access_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    last_seen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    ip_address = table.Column<string>(type: "text", nullable: true),
                    created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
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
                name: "ix_indexer_commands_machine_id_status",
                table: "indexer_commands",
                columns: new[] { "machine_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_indexer_logs_machine_id_at",
                table: "indexer_logs",
                columns: new[] { "machine_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_machine_access_machine_id",
                table: "machine_access",
                column: "machine_id");

            migrationBuilder.CreateIndex(
                name: "ix_machine_access_user_id_machine_id",
                table: "machine_access",
                columns: new[] { "user_id", "machine_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_machine_enrollments_code_hash",
                table: "machine_enrollments",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_machine_enrollments_machine_id",
                table: "machine_enrollments",
                column: "machine_id");

            migrationBuilder.CreateIndex(
                name: "ix_machines_hostname",
                table: "machines",
                column: "hostname");

            migrationBuilder.CreateIndex(
                name: "ix_machines_token_hash",
                table: "machines",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_token_hash",
                table: "user_sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_user_id",
                table: "user_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_user_name",
                table: "users",
                column: "user_name",
                unique: true);
            // files.fts can't be expressed in the model, so a trigger keeps it current.
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

                CREATE TRIGGER trg_update_fts
                BEFORE INSERT OR UPDATE ON files
                FOR EACH ROW EXECUTE FUNCTION update_fts();
                """);

            // Grants are explicit rather than default privileges, so a table added later starts closed.
            // The API's login exists only in the Compose stack, and never gets the migration history.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nunam_api') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON machines, files, index_jobs, indexer_status,
                            indexer_settings, indexer_commands, indexer_logs, machine_enrollments, users,
                            user_sessions, machine_access TO nunam_api;
                    END IF;
                END $$;
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
                name: "indexer_commands");

            migrationBuilder.DropTable(
                name: "indexer_logs");

            migrationBuilder.DropTable(
                name: "indexer_settings");

            migrationBuilder.DropTable(
                name: "indexer_status");

            migrationBuilder.DropTable(
                name: "machine_access");

            migrationBuilder.DropTable(
                name: "machine_enrollments");

            migrationBuilder.DropTable(
                name: "user_sessions");

            migrationBuilder.DropTable(
                name: "machines");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
