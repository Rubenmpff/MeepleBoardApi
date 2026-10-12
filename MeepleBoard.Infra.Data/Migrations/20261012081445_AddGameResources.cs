using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeepleBoard.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGameResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameResources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PrintCategory = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Language = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Author = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Platform = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Compatibility = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsOfficial = table.Column<bool>(type: "bit", nullable: false),
                    IsFree = table.Column<bool>(type: "bit", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SuggestedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameResources_AspNetUsers_SuggestedById",
                        column: x => x.SuggestedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GameResources_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameResourceReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameResourceReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameResourceReports_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GameResourceReports_GameResources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "GameResources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameResourceReports_ResourceId_UserId",
                table: "GameResourceReports",
                columns: new[] { "ResourceId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_GameResourceReports_UserId",
                table: "GameResourceReports",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameResources_GameId_Status",
                table: "GameResources",
                columns: new[] { "GameId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_GameResources_SuggestedById",
                table: "GameResources",
                column: "SuggestedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameResourceReports");

            migrationBuilder.DropTable(
                name: "GameResources");
        }
    }
}
