using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeepleBoard.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGameSearchCatalogDetailsSyncedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DetailsSyncedAt",
                table: "GameSearchCatalog",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameSearchCatalog_DetailsSyncedAt",
                table: "GameSearchCatalog",
                column: "DetailsSyncedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GameSearchCatalog_DetailsSyncedAt",
                table: "GameSearchCatalog");

            migrationBuilder.DropColumn(
                name: "DetailsSyncedAt",
                table: "GameSearchCatalog");
        }
    }
}
