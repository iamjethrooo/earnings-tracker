using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using nerve_earnings_tracker.Data;

#nullable disable

namespace nerve_earnings_tracker.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261009090000_AddPayoutTransactionsAndAudit")]
    public partial class AddPayoutTransactionsAndAudit : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>("Date", "Payouts", type: "TEXT", nullable: false, defaultValue: DateTime.MinValue);
            migrationBuilder.AddColumn<string>("Type", "Payouts", type: "TEXT", nullable: false, defaultValue: "Payment");
            migrationBuilder.AddColumn<string>("Description", "Payouts", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>("Status", "Payouts", type: "TEXT", nullable: false, defaultValue: "Legacy");
            migrationBuilder.AddColumn<decimal>("Amount", "Payouts", type: "TEXT", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<string>("SourceId", "Payouts", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>("SourceValuesJson", "Payouts", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>("SourceRow", "Payouts", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<DateTime>("ImportedAt", "Payouts", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<bool>("IsAdjustmentIncludedInSessionEarnings", "Payouts", type: "INTEGER", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<string>("ReconciliationState", "Payouts", type: "TEXT", nullable: false, defaultValue: "Unreconciled");

            // Preserve historical range data and use its first date as the best available transaction date.
            migrationBuilder.Sql("UPDATE Payouts SET Date = StartDate");

            migrationBuilder.CreateIndex("IX_Payouts_SourceId", "Payouts", "SourceId");
            migrationBuilder.CreateTable(
                name: "PayoutChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                    PayoutId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ChangeType = table.Column<string>(type: "TEXT", nullable: false),
                    BeforeValuesJson = table.Column<string>(type: "TEXT", nullable: false),
                    AfterValuesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutChanges", x => x.Id);
                    table.ForeignKey("FK_PayoutChanges_Payouts_PayoutId", x => x.PayoutId, "Payouts", "Id", onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex("IX_PayoutChanges_PayoutId", "PayoutChanges", "PayoutId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("PayoutChanges");
            migrationBuilder.DropIndex("IX_Payouts_SourceId", "Payouts");
            migrationBuilder.DropColumn("Date", "Payouts");
            migrationBuilder.DropColumn("Type", "Payouts");
            migrationBuilder.DropColumn("Description", "Payouts");
            migrationBuilder.DropColumn("Status", "Payouts");
            migrationBuilder.DropColumn("Amount", "Payouts");
            migrationBuilder.DropColumn("SourceId", "Payouts");
            migrationBuilder.DropColumn("SourceValuesJson", "Payouts");
            migrationBuilder.DropColumn("SourceRow", "Payouts");
            migrationBuilder.DropColumn("ImportedAt", "Payouts");
            migrationBuilder.DropColumn("IsAdjustmentIncludedInSessionEarnings", "Payouts");
            migrationBuilder.DropColumn("ReconciliationState", "Payouts");
        }
    }
}
