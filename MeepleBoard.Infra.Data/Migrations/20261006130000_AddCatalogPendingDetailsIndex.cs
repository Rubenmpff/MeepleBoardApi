using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeepleBoard.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogPendingDetailsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_GameSearchCatalog_RatingsCount_BggRank_AverageRating_Name_BggId",
                table: "GameSearchCatalog",
                columns: new[] { "RatingsCount", "BggRank", "AverageRating", "Name", "BggId" },
                descending: new[] { true, false, true, false, false },
                filter: "[DetailsSyncedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GameSearchCatalog_RatingsCount_BggRank_AverageRating_Name_BggId",
                table: "GameSearchCatalog");
        }
    }
}
