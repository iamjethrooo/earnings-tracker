using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace nerve_earnings_tracker.Migrations
{
    /// <inheritdoc />
    public partial class RemoveIsBookmarked : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBookmarked",
                table: "Games");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBookmarked",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
