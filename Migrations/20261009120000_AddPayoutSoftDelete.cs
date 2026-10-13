using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using nerve_earnings_tracker.Data;

#nullable disable

namespace nerve_earnings_tracker.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261009120000_AddPayoutSoftDelete")]
    public partial class AddPayoutSoftDelete : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>("IsDeleted", "Payouts", type: "INTEGER", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<DateTime>("DeletedAt", "Payouts", type: "TEXT", nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn("IsDeleted", "Payouts");
            migrationBuilder.DropColumn("DeletedAt", "Payouts");
        }
    }
}
