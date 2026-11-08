using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EarningsTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddGameMultiplayerMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MultiplayerProvenance",
                table: "GameMetadata",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "MultiplayerVerifiedAt",
                table: "GameMetadata",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsMultiplayer",
                table: "GameMetadata",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MultiplayerProvenance",
                table: "GameMetadata");

            migrationBuilder.DropColumn(
                name: "MultiplayerVerifiedAt",
                table: "GameMetadata");

            migrationBuilder.DropColumn(
                name: "SupportsMultiplayer",
                table: "GameMetadata");
        }
    }
}
