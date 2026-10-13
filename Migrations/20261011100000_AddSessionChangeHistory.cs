using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using nerve_earnings_tracker.Data;

#nullable disable

namespace nerve_earnings_tracker.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261011100000_AddSessionChangeHistory")]
    public partial class AddSessionChangeHistory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SessionChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ChangeType = table.Column<string>(type: "TEXT", nullable: false),
                    BeforeValuesJson = table.Column<string>(type: "TEXT", nullable: false),
                    AfterValuesJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_SessionChanges", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_SessionChanges_SessionId", table: "SessionChanges", column: "SessionId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SessionChanges");
        }
    }
}
