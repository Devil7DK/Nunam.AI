using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nunam.Core.Data.Migrations
{
    /// <summary>
    /// Binds each indexer login to its own machine. Every indexed machine holds its indexer password,
    /// so the database itself must stop one machine's login from reading or rewriting another
    /// machine's index, or queueing work for it. Grants are explicit rather than default privileges,
    /// so a table added later starts closed. The service roles exist only in the Compose stack.
    /// </summary>
    public partial class ScopeIndexerLogins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "db_role",
                table: "machines",
                type: "text",
                nullable: true,
                defaultValueSql: "current_user");

            migrationBuilder.Sql("""
                ALTER TABLE machines ENABLE ROW LEVEL SECURITY;
                ALTER TABLE files ENABLE ROW LEVEL SECURITY;
                ALTER TABLE index_jobs ENABLE ROW LEVEL SECURITY;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nunam_indexer') THEN
                        REVOKE ALL ON machines, files, index_jobs, "__EFMigrationsHistory" FROM nunam_indexer;
                        GRANT SELECT, INSERT, UPDATE ON machines TO nunam_indexer;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON files, index_jobs TO nunam_indexer;

                        CREATE POLICY indexer_own_machine ON machines TO nunam_indexer
                            USING (db_role = current_user) WITH CHECK (db_role = current_user);
                        CREATE POLICY indexer_own_files ON files TO nunam_indexer
                            USING (machine_id IN (SELECT id FROM machines WHERE db_role = current_user))
                            WITH CHECK (machine_id IN (SELECT id FROM machines WHERE db_role = current_user));
                        CREATE POLICY indexer_own_jobs ON index_jobs TO nunam_indexer
                            USING (machine_id IN (SELECT id FROM machines WHERE db_role = current_user))
                            WITH CHECK (machine_id IN (SELECT id FROM machines WHERE db_role = current_user));
                    END IF;

                    -- Describes and embeds every machine's files, but never registers machines or adds files.
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nunam_aiworker') THEN
                        REVOKE ALL ON machines, files, index_jobs, "__EFMigrationsHistory" FROM nunam_aiworker;
                        GRANT SELECT, UPDATE ON files TO nunam_aiworker;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON index_jobs TO nunam_aiworker;
                        CREATE POLICY aiworker_files ON files TO nunam_aiworker USING (true) WITH CHECK (true);
                        CREATE POLICY aiworker_jobs ON index_jobs TO nunam_aiworker USING (true) WITH CHECK (true);
                    END IF;

                    -- Machine visibility for users is enforced in the API (MachineScope).
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nunam_api') THEN
                        REVOKE ALL ON machines, files, index_jobs, "__EFMigrationsHistory" FROM nunam_api;
                        GRANT SELECT ON machines, files, index_jobs TO nunam_api;
                        CREATE POLICY api_machines ON machines FOR SELECT TO nunam_api USING (true);
                        CREATE POLICY api_files ON files FOR SELECT TO nunam_api USING (true);
                        CREATE POLICY api_jobs ON index_jobs FOR SELECT TO nunam_api USING (true);
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS indexer_own_machine ON machines;
                DROP POLICY IF EXISTS indexer_own_files ON files;
                DROP POLICY IF EXISTS indexer_own_jobs ON index_jobs;
                DROP POLICY IF EXISTS aiworker_files ON files;
                DROP POLICY IF EXISTS aiworker_jobs ON index_jobs;
                DROP POLICY IF EXISTS api_machines ON machines;
                DROP POLICY IF EXISTS api_files ON files;
                DROP POLICY IF EXISTS api_jobs ON index_jobs;
                ALTER TABLE machines DISABLE ROW LEVEL SECURITY;
                ALTER TABLE files DISABLE ROW LEVEL SECURITY;
                ALTER TABLE index_jobs DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropColumn(
                name: "db_role",
                table: "machines");
        }
    }
}
