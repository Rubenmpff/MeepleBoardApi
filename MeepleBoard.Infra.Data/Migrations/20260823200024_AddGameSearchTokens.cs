using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeepleBoard.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGameSearchTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameSearchToken",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BggId = table.Column<int>(type: "int", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Position = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSearchToken", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameSearchToken_BggId",
                table: "GameSearchToken",
                column: "BggId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSearchToken_BggId_Position",
                table: "GameSearchToken",
                columns: new[] { "BggId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameSearchToken_Token_BggId",
                table: "GameSearchToken",
                columns: new[] { "Token", "BggId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameSearchToken");
        }
    }
}
