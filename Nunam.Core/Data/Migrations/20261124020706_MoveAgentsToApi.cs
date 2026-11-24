using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nunam.Core.Data.Migrations
{
    /// <summary>
    /// Indexers and the AI worker now go through the API, so only the API's login works on the tables.
    /// Row-level security existed to keep one machine's database login out of another machine's rows;
    /// the API checks the machine token instead. The old logins keep no rights; dropping them is left to
    /// the server's admin, since migrations don't own roles.
    /// </summary>
    public partial class MoveAgentsToApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS indexer_own_machine ON machines;
                DROP POLICY IF EXISTS indexer_own_files ON files;
                DROP POLICY IF EXISTS indexer_own_jobs ON index_jobs;
                DROP POLICY IF EXISTS indexer_own_status ON indexer_status;
                DROP POLICY IF EXISTS indexer_own_settings ON indexer_settings;
                DROP POLICY IF EXISTS indexer_own_commands ON indexer_commands;
                DROP POLICY IF EXISTS indexer_own_logs ON indexer_logs;
                DROP POLICY IF EXISTS aiworker_files ON files;
                DROP POLICY IF EXISTS aiworker_jobs ON index_jobs;
                DROP POLICY IF EXISTS api_machines ON machines;
                DROP POLICY IF EXISTS api_files ON files;
                DROP POLICY IF EXISTS api_jobs ON index_jobs;
                DROP POLICY IF EXISTS api_status ON indexer_status;
                DROP POLICY IF EXISTS api_settings ON indexer_settings;
                DROP POLICY IF EXISTS api_commands ON indexer_commands;
                DROP POLICY IF EXISTS api_logs ON indexer_logs;
                ALTER TABLE machines DISABLE ROW LEVEL SECURITY;
                ALTER TABLE files DISABLE ROW LEVEL SECURITY;
                ALTER TABLE index_jobs DISABLE ROW LEVEL SECURITY;
                ALTER TABLE indexer_status DISABLE ROW LEVEL SECURITY;
                ALTER TABLE indexer_settings DISABLE ROW LEVEL SECURITY;
                ALTER TABLE indexer_commands DISABLE ROW LEVEL SECURITY;
                ALTER TABLE indexer_logs DISABLE ROW LEVEL SECURITY;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nunam_indexer') THEN
                        REVOKE ALL ON machines, files, index_jobs, indexer_status, indexer_settings, indexer_commands, indexer_logs FROM nunam_indexer;
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nunam_aiworker') THEN
                        REVOKE ALL ON machines, files, index_jobs, indexer_status, indexer_settings, indexer_commands, indexer_logs FROM nunam_aiworker;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "ix_machines_hostname",
                table: "machines");

            migrationBuilder.DropColumn(
                name: "db_role",
                table: "machines");

            migrationBuilder.AddColumn<byte[]>(
                name: "token_hash",
                table: "machines",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "token_issued_at",
                table: "machines",
                type: "timestamp with time zone",
                nullable: true);

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
                name: "ix_machine_enrollments_code_hash",
                table: "machine_enrollments",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_machine_enrollments_machine_id",
                table: "machine_enrollments",
                column: "machine_id");

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nunam_api') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON machines, files, index_jobs, indexer_status, indexer_settings, indexer_commands, indexer_logs, machine_enrollments TO nunam_api;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nunam_api') THEN
                        REVOKE ALL ON machines, files, index_jobs, indexer_status, indexer_settings, indexer_commands, indexer_logs FROM nunam_api;
                        GRANT SELECT ON machines, files, index_jobs, indexer_status, indexer_logs TO nunam_api;
                        GRANT SELECT, INSERT, UPDATE ON indexer_settings TO nunam_api;
                        GRANT SELECT, INSERT ON indexer_commands TO nunam_api;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "machine_enrollments");

            migrationBuilder.DropIndex(
                name: "ix_machines_hostname",
                table: "machines");

            migrationBuilder.DropIndex(
                name: "ix_machines_token_hash",
                table: "machines");

            migrationBuilder.DropColumn(
                name: "token_hash",
                table: "machines");

            migrationBuilder.DropColumn(
                name: "token_issued_at",
                table: "machines");

            migrationBuilder.AddColumn<string>(
                name: "db_role",
                table: "machines",
                type: "text",
                nullable: true,
                defaultValueSql: "current_user");

            migrationBuilder.CreateIndex(
                name: "ix_machines_hostname",
                table: "machines",
                column: "hostname",
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE machines ENABLE ROW LEVEL SECURITY;
                ALTER TABLE files ENABLE ROW LEVEL SECURITY;
                ALTER TABLE index_jobs ENABLE ROW LEVEL SECURITY;
                ALTER TABLE indexer_status ENABLE ROW LEVEL SECURITY;
                ALTER TABLE indexer_settings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE indexer_commands ENABLE ROW LEVEL SECURITY;
                ALTER TABLE indexer_logs ENABLE ROW LEVEL SECURITY;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nunam_indexer') THEN
                        GRANT SELECT, INSERT, UPDATE ON machines, indexer_status TO nunam_indexer;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON files, index_jobs TO nunam_indexer;
                        GRANT SELECT ON indexer_settings TO nunam_indexer;
                        GRANT SELECT, UPDATE ON indexer_commands TO nunam_indexer;
                        GRANT SELECT, INSERT, DELETE ON indexer_logs TO nunam_indexer;
                        CREATE POLICY indexer_own_machine ON machines TO nunam_indexer
                            USING (db_role = current_user) WITH CHECK (db_role = current_user);
                        CREATE POLICY indexer_own_files ON files TO nunam_indexer
                            USING (machine_id IN (SELECT id FROM machines WHERE db_role = current_user))
                            WITH CHECK (machine_id IN (SELECT id FROM machines WHERE db_role = current_user));
                        CREATE POLICY indexer_own_jobs ON index_jobs TO nunam_indexer
                            USING (machine_id IN (SELECT id FROM machines WHERE db_role = current_user))
                            WITH CHECK (machine_id IN (SELECT id FROM machines WHERE db_role = current_user));
                        CREATE POLICY indexer_own_status ON indexer_status TO nunam_indexer
                            USING (machine_id IN (SELECT id FROM machines WHERE db_role = current_user))
                            WITH CHECK (machine_id IN (SELECT id FROM machines WHERE db_role = current_user));
                        CREATE POLICY indexer_own_settings ON indexer_settings FOR SELECT TO nunam_indexer
                            USING (machine_id IN (SELECT id FROM machines WHERE db_role = current_user));
                        CREATE POLICY indexer_own_commands ON indexer_commands TO nunam_indexer
                            USING (machine_id IN (SELECT id FROM machines WHERE db_role = current_user))
                            WITH CHECK (machine_id IN (SELECT id FROM machines WHERE db_role = current_user));
                        CREATE POLICY indexer_own_logs ON indexer_logs TO nunam_indexer
                            USING (machine_id IN (SELECT id FROM machines WHERE db_role = current_user))
                            WITH CHECK (machine_id IN (SELECT id FROM machines WHERE db_role = current_user));
                    END IF;

                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nunam_aiworker') THEN
                        GRANT SELECT, UPDATE ON files TO nunam_aiworker;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON index_jobs TO nunam_aiworker;
                        CREATE POLICY aiworker_files ON files TO nunam_aiworker USING (true) WITH CHECK (true);
                        CREATE POLICY aiworker_jobs ON index_jobs TO nunam_aiworker USING (true) WITH CHECK (true);
                    END IF;

                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nunam_api') THEN
                        CREATE POLICY api_machines ON machines FOR SELECT TO nunam_api USING (true);
                        CREATE POLICY api_files ON files FOR SELECT TO nunam_api USING (true);
                        CREATE POLICY api_jobs ON index_jobs FOR SELECT TO nunam_api USING (true);
                        CREATE POLICY api_status ON indexer_status FOR SELECT TO nunam_api USING (true);
                        CREATE POLICY api_logs ON indexer_logs FOR SELECT TO nunam_api USING (true);
                        CREATE POLICY api_settings ON indexer_settings TO nunam_api USING (true) WITH CHECK (true);
                        CREATE POLICY api_commands ON indexer_commands TO nunam_api USING (true) WITH CHECK (true);
                    END IF;
                END $$;
                """);
        }
    }
}
