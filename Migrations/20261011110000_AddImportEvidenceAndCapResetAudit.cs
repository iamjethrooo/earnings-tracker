using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using nerve_earnings_tracker.Data;

#nullable disable

namespace nerve_earnings_tracker.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261011110000_AddImportEvidenceAndCapResetAudit")]
    public partial class AddImportEvidenceAndCapResetAudit : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Historical source text is unknown; leave it null rather than reconstructing it.
            migrationBuilder.AddColumn<string>(name: "SourceText", table: "SessionChanges", type: "TEXT", nullable: true);

            migrationBuilder.CreateTable(
                name: "GameChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ChangeType = table.Column<string>(type: "TEXT", nullable: false),
                    BeforeValuesJson = table.Column<string>(type: "TEXT", nullable: false),
                    AfterValuesJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_GameChanges", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_GameChanges_GameId", table: "GameChanges", column: "GameId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "GameChanges");
            migrationBuilder.DropColumn(name: "SourceText", table: "SessionChanges");
        }
    }
}
