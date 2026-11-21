using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nunam.Core.Data.Migrations
{
    /// <summary>
    /// Tables for managing indexers from the UI. Each indexer reports its status and logs and reads its
    /// own commands and settings; the API reads everything and writes only commands and settings. Row
    /// security keeps one machine's login out of every other machine's rows, as for files and jobs.
    /// </summary>
    public partial class AddIndexerManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "ix_indexer_commands_machine_id_status",
                table: "indexer_commands",
                columns: new[] { "machine_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_indexer_logs_machine_id_at",
                table: "indexer_logs",
                columns: new[] { "machine_id", "at" });

            migrationBuilder.Sql("""
                ALTER TABLE indexer_status ENABLE ROW LEVEL SECURITY;
                ALTER TABLE indexer_settings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE indexer_commands ENABLE ROW LEVEL SECURITY;
                ALTER TABLE indexer_logs ENABLE ROW LEVEL SECURITY;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nunam_indexer') THEN
                        GRANT SELECT, INSERT, UPDATE ON indexer_status TO nunam_indexer;
                        GRANT SELECT ON indexer_settings TO nunam_indexer;
                        GRANT SELECT, UPDATE ON indexer_commands TO nunam_indexer;
                        GRANT SELECT, INSERT, DELETE ON indexer_logs TO nunam_indexer;

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

                    -- Who may manage which machine is checked in the API (MachineScope).
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nunam_api') THEN
                        GRANT SELECT ON indexer_status, indexer_logs TO nunam_api;
                        GRANT SELECT, INSERT, UPDATE ON indexer_settings TO nunam_api;
                        GRANT SELECT, INSERT ON indexer_commands TO nunam_api;
                        CREATE POLICY api_status ON indexer_status FOR SELECT TO nunam_api USING (true);
                        CREATE POLICY api_logs ON indexer_logs FOR SELECT TO nunam_api USING (true);
                        CREATE POLICY api_settings ON indexer_settings TO nunam_api USING (true) WITH CHECK (true);
                        CREATE POLICY api_commands ON indexer_commands TO nunam_api USING (true) WITH CHECK (true);
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "indexer_commands");

            migrationBuilder.DropTable(
                name: "indexer_logs");

            migrationBuilder.DropTable(
                name: "indexer_settings");

            migrationBuilder.DropTable(
                name: "indexer_status");
        }
    }
}
