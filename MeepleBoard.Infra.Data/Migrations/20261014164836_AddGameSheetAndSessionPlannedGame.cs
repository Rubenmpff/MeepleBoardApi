using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeepleBoard.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGameSheetAndSessionPlannedGame : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlannedGameId",
                table: "GameSessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AverageWeight",
                table: "Games",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxPlayTime",
                table: "Games",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinAge",
                table: "Games",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinPlayTime",
                table: "Games",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameSessions_PlannedGameId",
                table: "GameSessions",
                column: "PlannedGameId");

            migrationBuilder.AddForeignKey(
                name: "FK_GameSessions_Games_PlannedGameId",
                table: "GameSessions",
                column: "PlannedGameId",
                principalTable: "Games",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GameSessions_Games_PlannedGameId",
                table: "GameSessions");

            migrationBuilder.DropIndex(
                name: "IX_GameSessions_PlannedGameId",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "PlannedGameId",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "AverageWeight",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "MaxPlayTime",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "MinAge",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "MinPlayTime",
                table: "Games");
        }
    }
}
