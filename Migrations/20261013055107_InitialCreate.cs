using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EarningsTracker.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                columns: table => new
                {
                    Currency = table.Column<string>(type: "TEXT", nullable: false),
                    PerUsd = table.Column<decimal>(type: "TEXT", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Currency);
                });

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
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    RatePerHour = table.Column<decimal>(type: "TEXT", nullable: false),
                    MinutesCap = table.Column<decimal>(type: "TEXT", nullable: false),
                    CapResetDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Payouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", nullable: true),
                    SourceValuesJson = table.Column<string>(type: "TEXT", nullable: true),
                    SourceRow = table.Column<string>(type: "TEXT", nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsAdjustmentIncludedInSessionEarnings = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReconciliationState = table.Column<string>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payouts", x => x.Id);
                });

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
                    AfterValuesJson = table.Column<string>(type: "TEXT", nullable: false),
                    SourceText = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Bookmarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookmarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bookmarks_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameGenres",
                columns: table => new
                {
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameGenres", x => new { x.GameId, x.Name });
                    table.ForeignKey(
                        name: "FK_GameGenres_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameMetadata",
                columns: table => new
                {
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    CatalogId = table.Column<string>(type: "TEXT", nullable: false),
                    IgdbId = table.Column<long>(type: "INTEGER", nullable: true),
                    ItadId = table.Column<string>(type: "TEXT", nullable: true),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    CoverUrl = table.Column<string>(type: "TEXT", nullable: false),
                    CoverSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    Provenance = table.Column<string>(type: "TEXT", nullable: false),
                    Reviewed = table.Column<bool>(type: "INTEGER", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameMetadata", x => x.GameId);
                    table.ForeignKey(
                        name: "FK_GameMetadata_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameOwnership",
                columns: table => new
                {
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsOwned = table.Column<bool>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    PurchaseAmount = table.Column<decimal>(type: "TEXT", nullable: true),
                    Currency = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameOwnership", x => x.GameId);
                    table.ForeignKey(
                        name: "FK_GameOwnership_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreOffers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    Edition = table.Column<string>(type: "TEXT", nullable: false),
                    Region = table.Column<string>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false),
                    RegularPrice = table.Column<decimal>(type: "TEXT", nullable: true),
                    SalePrice = table.Column<decimal>(type: "TEXT", nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Provenance = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreOffers_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionAvailability",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    Service = table.Column<string>(type: "TEXT", nullable: false),
                    Region = table.Column<string>(type: "TEXT", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", nullable: false),
                    Included = table.Column<bool>(type: "INTEGER", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    Provenance = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionAvailability", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionAvailability_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayoutChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
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
                    table.ForeignKey(
                        name: "FK_PayoutChanges_Payouts_PayoutId",
                        column: x => x.PayoutId,
                        principalTable: "Payouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameName = table.Column<string>(type: "TEXT", nullable: false),
                    GameId = table.Column<int>(type: "INTEGER", nullable: true),
                    PayoutId = table.Column<int>(type: "INTEGER", nullable: true),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecordedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PostedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsForceVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Earnings = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessions_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sessions_Payouts_PayoutId",
                        column: x => x.PayoutId,
                        principalTable: "Payouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_GameId",
                table: "Bookmarks",
                column: "GameId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameChanges_GameId",
                table: "GameChanges",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GameMetadata_CatalogId",
                table: "GameMetadata",
                column: "CatalogId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayoutChanges_PayoutId",
                table: "PayoutChanges",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_SourceId",
                table: "Payouts",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionChanges_SessionId",
                table: "SessionChanges",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_GameId",
                table: "Sessions",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_PayoutId",
                table: "Sessions",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOffers_GameId",
                table: "StoreOffers",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAvailability_GameId",
                table: "SubscriptionAvailability",
                column: "GameId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bookmarks");

            migrationBuilder.DropTable(
                name: "CatalogSettings");

            migrationBuilder.DropTable(
                name: "ExchangeRates");

            migrationBuilder.DropTable(
                name: "GameChanges");

            migrationBuilder.DropTable(
                name: "GameGenres");

            migrationBuilder.DropTable(
                name: "GameMetadata");

            migrationBuilder.DropTable(
                name: "GameOwnership");

            migrationBuilder.DropTable(
                name: "PayoutChanges");

            migrationBuilder.DropTable(
                name: "SessionChanges");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropTable(
                name: "StoreOffers");

            migrationBuilder.DropTable(
                name: "SubscriptionAvailability");

            migrationBuilder.DropTable(
                name: "Payouts");

            migrationBuilder.DropTable(
                name: "Games");
        }
    }
}
