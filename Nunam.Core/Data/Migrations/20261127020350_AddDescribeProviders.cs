using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nunam.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDescribeProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "described_at",
                table: "files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description_model",
                table: "files",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description_provider",
                table: "files",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "describe_providers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    base_url = table.Column<string>(type: "text", nullable: false),
                    api_key_protected = table.Column<string>(type: "text", nullable: true),
                    model = table.Column<string>(type: "text", nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: true),
                    max_output_tokens = table.Column<int>(type: "integer", nullable: false),
                    disable_reasoning = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    concurrency = table.Column<int>(type: "integer", nullable: false),
                    takes_overflow = table.Column<bool>(type: "boolean", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    input_price_per_million = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    output_price_per_million = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    budget_usd = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    budget_period = table.Column<string>(type: "text", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    last_error_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_describe_providers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "describe_usage",
                columns: table => new
                {
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    requests = table.Column<int>(type: "integer", nullable: false),
                    failures = table.Column<int>(type: "integer", nullable: false),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric(14,6)", precision: 14, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_describe_usage", x => new { x.provider_id, x.day });
                    table.ForeignKey(
                        name: "fk_describe_usage_describe_providers_provider_id",
                        column: x => x.provider_id,
                        principalTable: "describe_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_describe_providers_name",
                table: "describe_providers",
                column: "name",
                unique: true);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nunam_api') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON describe_providers, describe_usage TO nunam_api;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "describe_usage");

            migrationBuilder.DropTable(
                name: "describe_providers");

            migrationBuilder.DropColumn(
                name: "described_at",
                table: "files");

            migrationBuilder.DropColumn(
                name: "description_model",
                table: "files");

            migrationBuilder.DropColumn(
                name: "description_provider",
                table: "files");
        }
    }
}
