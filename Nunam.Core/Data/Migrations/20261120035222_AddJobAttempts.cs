using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nunam.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempts",
                table: "index_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attempts",
                table: "index_jobs");
        }
    }
}
