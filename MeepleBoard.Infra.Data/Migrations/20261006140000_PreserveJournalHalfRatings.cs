using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace MeepleBoard.Infra.Data.Migrations;
public partial class PreserveJournalHalfRatings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AlterColumn<double>(
        name: "PersonalRating", table: "MatchJournalEntries", type: "float", nullable: true,
        oldClrType: typeof(int), oldType: "int", oldNullable: true);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new InvalidOperationException(
        "Rollback requires review: converting ratings to integers would lose half points.");
}
