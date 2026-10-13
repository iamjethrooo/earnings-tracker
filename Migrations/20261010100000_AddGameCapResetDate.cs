using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using nerve_earnings_tracker.Data;

#nullable disable

namespace nerve_earnings_tracker.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261010100000_AddGameCapResetDate")]
    public partial class AddGameCapResetDate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CapResetDate",
                table: "Games",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CapResetDate", table: "Games");
        }
    }
}
