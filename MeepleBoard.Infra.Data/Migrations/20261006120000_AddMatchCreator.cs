using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace MeepleBoard.Infra.Data.Migrations;
public partial class AddMatchCreator : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<Guid>(name: "CreatorId", table: "Matches", type: "uniqueidentifier", nullable: true);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new InvalidOperationException("Automatic rollback would discard recorded authorship. Prepare a reviewed data-preserving rollback instead.");
}
