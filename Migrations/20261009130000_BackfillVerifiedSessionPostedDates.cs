using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using nerve_earnings_tracker.Data;

#nullable disable

namespace nerve_earnings_tracker.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261009130000_BackfillVerifiedSessionPostedDates")]
    public partial class BackfillVerifiedSessionPostedDates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // For ordinary verified sessions, Nerve's session date is also the posted date.
            // Force-verified sessions keep their own posted date because it can differ from the recording date.
            migrationBuilder.Sql("UPDATE Sessions SET PostedDate = Date WHERE PostedDate IS NULL AND Status = 1 AND IsForceVerified = 0");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Keep backfilled history intact when rolling this data-only migration back.
        }
    }
}
