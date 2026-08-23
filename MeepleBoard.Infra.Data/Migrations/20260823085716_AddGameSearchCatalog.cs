using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeepleBoard.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGameSearchCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameSearchCatalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BggId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    YearPublished = table.Column<int>(type: "int", nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsExpansion = table.Column<bool>(type: "bit", nullable: false),
                    MinPlayers = table.Column<int>(type: "int", nullable: true),
                    MaxPlayers = table.Column<int>(type: "int", nullable: true),
                    IsCooperative = table.Column<bool>(type: "bit", nullable: false),
                    SupportsCampaign = table.Column<bool>(type: "bit", nullable: false),
                    AverageRating = table.Column<double>(type: "float(8)", precision: 8, scale: 5, nullable: true),
                    RatingsCount = table.Column<int>(type: "int", nullable: true),
                    BggRank = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSearchCatalog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameSearchCatalog_BggId",
                table: "GameSearchCatalog",
                column: "BggId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameSearchCatalog_IsExpansion_NormalizedName",
                table: "GameSearchCatalog",
                columns: new[] { "IsExpansion", "NormalizedName" });

            migrationBuilder.CreateIndex(
                name: "IX_GameSearchCatalog_LastSyncedAt",
                table: "GameSearchCatalog",
                column: "LastSyncedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GameSearchCatalog_NormalizedName",
                table: "GameSearchCatalog",
                column: "NormalizedName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameSearchCatalog");
        }
    }
}
