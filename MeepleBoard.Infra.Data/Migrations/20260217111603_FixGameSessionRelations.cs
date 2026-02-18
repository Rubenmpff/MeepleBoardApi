using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeepleBoard.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixGameSessionRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GameSessionPlayers_AspNetUsers_UserId",
                table: "GameSessionPlayers");

            migrationBuilder.DropForeignKey(
                name: "FK_Matches_AspNetUsers_WinnerId",
                table: "Matches");

            migrationBuilder.DropForeignKey(
                name: "FK_Matches_GameSessions_GameSessionId",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_GameSessionPlayers_SessionId",
                table: "GameSessionPlayers");

            migrationBuilder.CreateIndex(
                name: "IX_GameSessions_OrganizerId",
                table: "GameSessions",
                column: "OrganizerId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSessionPlayers_SessionId_UserId",
                table: "GameSessionPlayers",
                columns: new[] { "SessionId", "UserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GameSessionPlayers_AspNetUsers_UserId",
                table: "GameSessionPlayers",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GameSessions_AspNetUsers_OrganizerId",
                table: "GameSessions",
                column: "OrganizerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Matches_AspNetUsers_WinnerId",
                table: "Matches",
                column: "WinnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Matches_GameSessions_GameSessionId",
                table: "Matches",
                column: "GameSessionId",
                principalTable: "GameSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GameSessionPlayers_AspNetUsers_UserId",
                table: "GameSessionPlayers");

            migrationBuilder.DropForeignKey(
                name: "FK_GameSessions_AspNetUsers_OrganizerId",
                table: "GameSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_Matches_AspNetUsers_WinnerId",
                table: "Matches");

            migrationBuilder.DropForeignKey(
                name: "FK_Matches_GameSessions_GameSessionId",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_GameSessions_OrganizerId",
                table: "GameSessions");

            migrationBuilder.DropIndex(
                name: "IX_GameSessionPlayers_SessionId_UserId",
                table: "GameSessionPlayers");

            migrationBuilder.CreateIndex(
                name: "IX_GameSessionPlayers_SessionId",
                table: "GameSessionPlayers",
                column: "SessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_GameSessionPlayers_AspNetUsers_UserId",
                table: "GameSessionPlayers",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Matches_AspNetUsers_WinnerId",
                table: "Matches",
                column: "WinnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Matches_GameSessions_GameSessionId",
                table: "Matches",
                column: "GameSessionId",
                principalTable: "GameSessions",
                principalColumn: "Id");
        }
    }
}
