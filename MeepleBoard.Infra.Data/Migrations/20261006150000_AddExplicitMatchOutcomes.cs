using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace MeepleBoard.Infra.Data.Migrations;
public partial class AddExplicitMatchOutcomes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "GameMode", table: "Matches", type: "nvarchar(16)", maxLength: 16, nullable: true);
        migrationBuilder.AddColumn<string>(name: "Result", table: "Matches", type: "nvarchar(16)", maxLength: 16, nullable: true);
        migrationBuilder.AddColumn<bool>(name: "SharedVictoryAllowed", table: "Matches", type: "bit", nullable: true);
        migrationBuilder.AddColumn<string>(name: "Outcome", table: "MatchPlayers", type: "nvarchar(16)", maxLength: 16, nullable: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => throw new InvalidOperationException("Rollback requires review: explicit match results must not be discarded.");
}
