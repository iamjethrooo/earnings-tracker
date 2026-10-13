using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace nerve_earnings_tracker.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionEarnings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Earnings",
                table: "Sessions",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Earnings",
                table: "Sessions");
        }
    }
}
