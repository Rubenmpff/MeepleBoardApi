using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeepleBoard.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionSchedulingAndInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "GameSessions");

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledStartDate",
                table: "GameSessions",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "InvitedAt",
                table: "GameSessionPlayers",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "RespondedAt",
                table: "GameSessionPlayers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "GameSessionPlayers",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScheduledStartDate",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "InvitedAt",
                table: "GameSessionPlayers");

            migrationBuilder.DropColumn(
                name: "RespondedAt",
                table: "GameSessionPlayers");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "GameSessionPlayers");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "GameSessions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
