using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeepleBoard.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRatingCountsAndExperiencePublications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MeepleBoardRaters",
                table: "Games",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MeepleBoardRatingsCount",
                table: "Games",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MatchExperiencePublications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingPublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CommentPublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CommentEditedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HiddenAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HiddenReason = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    HiddenById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchExperiencePublications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchExperiencePublications_MatchJournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "MatchJournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExperienceReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReporterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperienceReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExperienceReports_AspNetUsers_ReporterId",
                        column: x => x.ReporterId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExperienceReports_MatchExperiencePublications_PublicationId",
                        column: x => x.PublicationId,
                        principalTable: "MatchExperiencePublications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceReports_PublicationId_ReporterId",
                table: "ExperienceReports",
                columns: new[] { "PublicationId", "ReporterId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceReports_ReporterId",
                table: "ExperienceReports",
                column: "ReporterId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchExperiencePublications_JournalEntryId",
                table: "MatchExperiencePublications",
                column: "JournalEntryId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExperienceReports");

            migrationBuilder.DropTable(
                name: "MatchExperiencePublications");

            migrationBuilder.DropColumn(
                name: "MeepleBoardRaters",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "MeepleBoardRatingsCount",
                table: "Games");
        }
    }
}
