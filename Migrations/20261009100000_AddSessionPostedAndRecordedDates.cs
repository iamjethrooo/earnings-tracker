using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using nerve_earnings_tracker.Data;

#nullable disable

namespace nerve_earnings_tracker.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261009100000_AddSessionPostedAndRecordedDates")]
    public partial class AddSessionPostedAndRecordedDates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>("RecordedDate", "Sessions", type: "TEXT", nullable: false, defaultValue: DateTime.MinValue);
            migrationBuilder.AddColumn<DateTime>("PostedDate", "Sessions", type: "TEXT", nullable: true);
            migrationBuilder.Sql("UPDATE Sessions SET RecordedDate = Date");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn("RecordedDate", "Sessions");
            migrationBuilder.DropColumn("PostedDate", "Sessions");
        }
    }
}
