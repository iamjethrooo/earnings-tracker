using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EarningsTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionAfkDuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "AfkDuration",
                table: "Sessions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AfkDuration",
                table: "Sessions");
        }
    }
}
