using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeepleBoard.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeGameSearchTokenKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
             * GameSearchToken é uma tabela totalmente derivada e reconstruível
             * a partir de GameSearchCatalog.
             *
             * Usamos SQL explícito nesta migration porque o provider do SQL Server
             * pode falhar ao reescrever uma sequência DropTable + CreateTable para
             * a mesma tabela dentro da mesma migration.
             *
             * Como os tokens podem ser recriados, a abordagem mais simples,
             * previsível e eficiente é recriar a tabela diretamente já no formato
             * final.
             */
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[GameSearchToken]', N'U') IS NOT NULL
                BEGIN
                    DROP TABLE [dbo].[GameSearchToken];
                END;

                CREATE TABLE [dbo].[GameSearchToken]
                (
                    [BggId] INT NOT NULL,
                    [Position] SMALLINT NOT NULL,
                    [Token] NVARCHAR(100) NOT NULL,

                    CONSTRAINT [PK_GameSearchToken]
                        PRIMARY KEY ([BggId], [Position])
                );

                CREATE INDEX [IX_GameSearchToken_Token_BggId]
                    ON [dbo].[GameSearchToken] ([Token], [BggId]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            /*
             * Rollback para a estrutura anterior.
             *
             * Os tokens continuam a ser dados derivados, por isso não tentamos
             * preservar o conteúdo atual durante o rollback.
             */
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[GameSearchToken]', N'U') IS NOT NULL
                BEGIN
                    DROP TABLE [dbo].[GameSearchToken];
                END;

                CREATE TABLE [dbo].[GameSearchToken]
                (
                    [Id] UNIQUEIDENTIFIER NOT NULL,
                    [BggId] INT NOT NULL,
                    [Token] NVARCHAR(100) NOT NULL,
                    [Position] SMALLINT NOT NULL,

                    CONSTRAINT [PK_GameSearchToken]
                        PRIMARY KEY ([Id])
                );

                CREATE INDEX [IX_GameSearchToken_BggId]
                    ON [dbo].[GameSearchToken] ([BggId]);

                CREATE UNIQUE INDEX [IX_GameSearchToken_BggId_Position]
                    ON [dbo].[GameSearchToken] ([BggId], [Position]);

                CREATE INDEX [IX_GameSearchToken_Token_BggId]
                    ON [dbo].[GameSearchToken] ([Token], [BggId]);
                """);
        }
    }
}