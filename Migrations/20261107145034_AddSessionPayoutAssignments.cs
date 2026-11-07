using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EarningsTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionPayoutAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignedPayoutId",
                table: "Sessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasExplicitPayoutAssignment",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_AssignedPayoutId",
                table: "Sessions",
                column: "AssignedPayoutId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_Payouts_AssignedPayoutId",
                table: "Sessions",
                column: "AssignedPayoutId",
                principalTable: "Payouts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_Payouts_AssignedPayoutId",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_AssignedPayoutId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "AssignedPayoutId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "HasExplicitPayoutAssignment",
                table: "Sessions");
        }
    }
}
