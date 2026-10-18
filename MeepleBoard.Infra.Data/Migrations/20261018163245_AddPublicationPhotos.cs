using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeepleBoard.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicationPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublicationPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhotoUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HiddenAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HiddenReason = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    HiddenById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RemovedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicationPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublicationPhotos_MatchExperiencePublications_PublicationId",
                        column: x => x.PublicationId,
                        principalTable: "MatchExperiencePublications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationPhotos_PublicationId_PhotoUrl",
                table: "PublicationPhotos",
                columns: new[] { "PublicationId", "PhotoUrl" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublicationPhotos");
        }
    }
}
