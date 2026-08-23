using System.Data;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    public class GameSearchCatalogRepository
        : IGameSearchCatalogRepository
    {
        private const int MaxSearchLimit = 100;

        /*
         * Este limite continua a ser utilizado pelos métodos EF tradicionais.
         *
         * A importação massiva passa a utilizar BulkUpsertAsync,
         * portanto deixa de depender deste valor para os INSERTs.
         */
        private const int SqlBatchSize = 200;

        private const int MaximumNameLength = 500;

        private readonly MeepleBoardDbContext _context;

        public GameSearchCatalogRepository(
            MeepleBoardDbContext context)
        {
            _context = context;
        }

        public async Task<GameSearchCatalog?> GetByBggIdAsync(
            int bggId,
            CancellationToken cancellationToken = default)
        {
            if (bggId <= 0)
            {
                return null;
            }

            return await _context.GameSearchCatalog
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.BggId == bggId,
                    cancellationToken);
        }

        public async Task<Dictionary<int, GameSearchCatalog>> GetByBggIdsAsync(
            IReadOnlyCollection<int> bggIds,
            CancellationToken cancellationToken = default)
        {
            if (bggIds == null ||
                bggIds.Count == 0)
            {
                return new Dictionary<int, GameSearchCatalog>();
            }

            var ids =
                bggIds
                    .Where(x => x > 0)
                    .Distinct()
                    .ToArray();

            if (ids.Length == 0)
            {
                return new Dictionary<int, GameSearchCatalog>();
            }

            var result =
                new Dictionary<int, GameSearchCatalog>(
                    ids.Length);

            /*
             * Estes registos ficam tracked de propósito.
             *
             * Este método continua disponível para operações normais.
             *
             * A importação massiva do catálogo já não depende deste
             * método depois de passar a utilizar BulkUpsertAsync.
             */
            foreach (var batch in ids.Chunk(SqlBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var games =
                    await _context.GameSearchCatalog
                        .Where(x =>
                            EF.Constant(batch)
                                .Contains(x.BggId))
                        .ToListAsync(
                            cancellationToken);

                foreach (var game in games)
                {
                    result[game.BggId] = game;
                }
            }

            return result;
        }

        public async Task<List<GameSearchCatalog>> SearchAsync(
            string normalizedQuery,
            int offset = 0,
            int limit = 10,
            bool? isExpansion = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                return new List<GameSearchCatalog>();
            }

            var searchTerm =
                normalizedQuery.Trim();

            var safeOffset =
                Math.Max(
                    0,
                    offset);

            var safeLimit =
                Math.Clamp(
                    limit,
                    1,
                    MaxSearchLimit);

            /*
             * Primeiro procuramos nomes que começam pelo termo.
             *
             * Esta é a pesquisa principal do autocomplete
             * e permite aproveitar melhor o índice de NormalizedName.
             */
            var prefixQuery =
                BuildBaseSearchQuery(
                    isExpansion)
                    .Where(x =>
                        x.NormalizedName.StartsWith(
                            searchTerm));

            var prefixResults =
                await prefixQuery
                    .OrderByDescending(x =>
                        x.NormalizedName == searchTerm)
                    .ThenByDescending(x =>
                        x.RatingsCount ?? 0)
                    .ThenBy(x =>
                        x.BggRank ?? int.MaxValue)
                    .ThenByDescending(x =>
                        x.AverageRating ?? 0)
                    .ThenBy(x =>
                        x.Name)
                    .Skip(safeOffset)
                    .Take(safeLimit)
                    .ToListAsync(
                        cancellationToken);

            /*
             * Se já temos resultados suficientes de prefixo,
             * não executamos a pesquisa mais pesada por Contains.
             */
            if (prefixResults.Count >= safeLimit)
            {
                return prefixResults;
            }

            var remaining =
                safeLimit - prefixResults.Count;

            /*
             * Contains é apenas fallback.
             *
             * Evitamos executar "%texto%" quando uma pesquisa
             * simples por prefixo já é suficiente.
             */
            var containsQuery =
                BuildBaseSearchQuery(
                    isExpansion)
                    .Where(x =>
                        x.NormalizedName.Contains(searchTerm) &&
                        !x.NormalizedName.StartsWith(searchTerm));

            var containsResults =
                await containsQuery
                    .OrderByDescending(x =>
                        x.RatingsCount ?? 0)
                    .ThenBy(x =>
                        x.BggRank ?? int.MaxValue)
                    .ThenByDescending(x =>
                        x.AverageRating ?? 0)
                    .ThenBy(x =>
                        x.Name)
                    .Take(remaining)
                    .ToListAsync(
                        cancellationToken);

            prefixResults.AddRange(
                containsResults);

            return prefixResults;
        }

        public async Task AddAsync(
            GameSearchCatalog game,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(game);

            await _context.GameSearchCatalog
                .AddAsync(
                    game,
                    cancellationToken);
        }

        public async Task AddRangeAsync(
            IEnumerable<GameSearchCatalog> games,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(games);

            await _context.GameSearchCatalog
                .AddRangeAsync(
                    games,
                    cancellationToken);
        }

        public Task UpdateAsync(
            GameSearchCatalog game,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(game);

            _context.GameSearchCatalog.Update(
                game);

            return Task.CompletedTask;
        }

        public async Task<int> CommitAsync(
            CancellationToken cancellationToken = default)
        {
            return await _context
                .SaveChangesAsync(
                    cancellationToken);
        }

        /// <summary>
        /// Insere ou atualiza em bloco os registos do catálogo.
        ///
        /// Utiliza:
        /// - tabela temporária SQL;
        /// - SqlBulkCopy;
        /// - UPDATE em bloco;
        /// - INSERT em bloco.
        ///
        /// Desta forma a importação massiva não passa pelo
        /// ChangeTracker nem gera centenas de INSERTs individuais
        /// através do Entity Framework.
        /// </summary>
        public async Task<int> BulkUpsertAsync(
            IReadOnlyCollection<GameSearchCatalog> games,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(games);

            if (games.Count == 0)
            {
                return 0;
            }

            cancellationToken.ThrowIfCancellationRequested();

            /*
             * Proteção adicional contra BGG IDs duplicados no mesmo lote.
             */
            var uniqueGames =
                games
                    .Where(x => x.BggId > 0)
                    .GroupBy(x => x.BggId)
                    .Select(x => x.First())
                    .ToList();

            if (uniqueGames.Count == 0)
            {
                return 0;
            }

            foreach (var game in uniqueGames)
            {
                ValidateBulkGame(game);
            }

            /*
             * Usamos diretamente a ligação SQL gerida pelo DbContext,
             * sem criar uma nova connection string nem duplicar
             * configuração da base de dados.
             */
            var dbConnection =
                _context.Database.GetDbConnection();

            if (dbConnection is not SqlConnection sqlConnection)
            {
                throw new InvalidOperationException(
                    "BulkUpsertAsync requer Microsoft SQL Server.");
            }

            var shouldCloseConnection =
                sqlConnection.State != ConnectionState.Open;

            if (shouldCloseConnection)
            {
                await sqlConnection.OpenAsync(
                    cancellationToken);
            }

            SqlTransaction? transaction = null;

            try
            {
                transaction =
                    (SqlTransaction)
                    await sqlConnection.BeginTransactionAsync(
                        cancellationToken);

                const string createTempTableSql =
                    """
                    CREATE TABLE #GameSearchCatalogImport
                    (
                        [BggId] INT NOT NULL PRIMARY KEY,
                        [Name] NVARCHAR(500) NOT NULL,
                        [NormalizedName] NVARCHAR(500) NOT NULL,
                        [YearPublished] INT NULL,
                        [IsExpansion] BIT NOT NULL,
                        [AverageRating] DECIMAL(8, 5) NULL,
                        [RatingsCount] INT NULL,
                        [BggRank] INT NULL
                    );
                    """;

                await using (
                    var createCommand =
                        new SqlCommand(
                            createTempTableSql,
                            sqlConnection,
                            transaction))
                {
                    createCommand.CommandTimeout = 120;

                    await createCommand.ExecuteNonQueryAsync(
                        cancellationToken);
                }

                var dataTable =
                    CreateBulkDataTable(
                        uniqueGames);

                using (
                    var bulkCopy =
                        new SqlBulkCopy(
                            sqlConnection,
                            SqlBulkCopyOptions.TableLock,
                            transaction))
                {
                    bulkCopy.DestinationTableName =
                        "#GameSearchCatalogImport";

                    /*
                     * O bulk copy tem timeout próprio.
                     *
                     * 120 segundos é suficientemente superior ao esperado
                     * para um lote e evita operações infinitamente presas.
                     */
                    bulkCopy.BulkCopyTimeout = 120;

                    /*
                     * Permite que o SqlBulkCopy envie internamente os dados
                     * em grupos eficientes sem gerar INSERTs individuais.
                     */
                    bulkCopy.BatchSize =
                        uniqueGames.Count;

                    bulkCopy.ColumnMappings.Add(
                        "BggId",
                        "BggId");

                    bulkCopy.ColumnMappings.Add(
                        "Name",
                        "Name");

                    bulkCopy.ColumnMappings.Add(
                        "NormalizedName",
                        "NormalizedName");

                    bulkCopy.ColumnMappings.Add(
                        "YearPublished",
                        "YearPublished");

                    bulkCopy.ColumnMappings.Add(
                        "IsExpansion",
                        "IsExpansion");

                    bulkCopy.ColumnMappings.Add(
                        "AverageRating",
                        "AverageRating");

                    bulkCopy.ColumnMappings.Add(
                        "RatingsCount",
                        "RatingsCount");

                    bulkCopy.ColumnMappings.Add(
                        "BggRank",
                        "BggRank");

                    await bulkCopy.WriteToServerAsync(
                        dataTable,
                        cancellationToken);
                }

                /*
                 * Primeiro atualizamos apenas registos que realmente mudaram.
                 *
                 * YearPublished mantém o valor existente quando o dump
                 * não possui ano, reproduzindo o comportamento que já
                 * existia no importador EF:
                 *
                 * row.YearPublished ?? existing.YearPublished
                 *
                 * LastSyncedAt só muda se algum dado básico mudou.
                 */
                const string updateSql =
                    """
                    UPDATE target
                    SET
                        target.[Name] = source.[Name],
                        target.[NormalizedName] = source.[NormalizedName],
                        target.[YearPublished] =
                            COALESCE(
                                source.[YearPublished],
                                target.[YearPublished]),
                        target.[IsExpansion] = source.[IsExpansion],
                        target.[AverageRating] = source.[AverageRating],
                        target.[RatingsCount] = source.[RatingsCount],
                        target.[BggRank] = source.[BggRank],
                        target.[LastSyncedAt] = SYSUTCDATETIME()
                    FROM [dbo].[GameSearchCatalog] AS target
                    INNER JOIN #GameSearchCatalogImport AS source
                        ON source.[BggId] = target.[BggId]
                    WHERE
                        target.[Name] <> source.[Name]
                        OR target.[NormalizedName] <> source.[NormalizedName]

                        OR
                        (
                            source.[YearPublished] IS NOT NULL
                            AND
                            (
                                target.[YearPublished] IS NULL
                                OR target.[YearPublished] <> source.[YearPublished]
                            )
                        )

                        OR target.[IsExpansion] <> source.[IsExpansion]

                        OR
                        (
                            target.[AverageRating] <> source.[AverageRating]
                            OR
                            (
                                target.[AverageRating] IS NULL
                                AND source.[AverageRating] IS NOT NULL
                            )
                            OR
                            (
                                target.[AverageRating] IS NOT NULL
                                AND source.[AverageRating] IS NULL
                            )
                        )

                        OR
                        (
                            target.[RatingsCount] <> source.[RatingsCount]
                            OR
                            (
                                target.[RatingsCount] IS NULL
                                AND source.[RatingsCount] IS NOT NULL
                            )
                            OR
                            (
                                target.[RatingsCount] IS NOT NULL
                                AND source.[RatingsCount] IS NULL
                            )
                        )

                        OR
                        (
                            target.[BggRank] <> source.[BggRank]
                            OR
                            (
                                target.[BggRank] IS NULL
                                AND source.[BggRank] IS NOT NULL
                            )
                            OR
                            (
                                target.[BggRank] IS NOT NULL
                                AND source.[BggRank] IS NULL
                            )
                        );
                    """;

                await using (
                    var updateCommand =
                        new SqlCommand(
                            updateSql,
                            sqlConnection,
                            transaction))
                {
                    updateCommand.CommandTimeout = 120;

                    await updateCommand.ExecuteNonQueryAsync(
                        cancellationToken);
                }

                /*
                 * Depois inserimos apenas os BGG IDs que ainda não existem.
                 *
                 * Os campos que só são obtidos através do /thing ficam
                 * inicialmente vazios/nulos, tal como acontecia quando
                 * construíamos GameSearchCatalog através do EF.
                 */
                const string insertSql =
                    """
                    INSERT INTO [dbo].[GameSearchCatalog]
                    (
                        [Id],
                        [BggId],
                        [Name],
                        [NormalizedName],
                        [YearPublished],
                        [ThumbnailUrl],
                        [IsExpansion],
                        [MinPlayers],
                        [MaxPlayers],
                        [IsCooperative],
                        [SupportsCampaign],
                        [AverageRating],
                        [RatingsCount],
                        [BggRank],
                        [CreatedAt],
                        [LastSyncedAt],
                        [DetailsSyncedAt]
                    )
                    SELECT
                        NEWID(),
                        source.[BggId],
                        source.[Name],
                        source.[NormalizedName],
                        source.[YearPublished],
                        N'',
                        source.[IsExpansion],
                        NULL,
                        NULL,
                        0,
                        0,
                        source.[AverageRating],
                        source.[RatingsCount],
                        source.[BggRank],
                        SYSUTCDATETIME(),
                        SYSUTCDATETIME(),
                        NULL
                    FROM #GameSearchCatalogImport AS source
                    WHERE NOT EXISTS
                    (
                        SELECT 1
                        FROM [dbo].[GameSearchCatalog] AS target
                        WHERE target.[BggId] = source.[BggId]
                    );
                    """;

                await using (
                    var insertCommand =
                        new SqlCommand(
                            insertSql,
                            sqlConnection,
                            transaction))
                {
                    insertCommand.CommandTimeout = 120;

                    await insertCommand.ExecuteNonQueryAsync(
                        cancellationToken);
                }

                await transaction.CommitAsync(
                    cancellationToken);

                return uniqueGames.Count;
            }
            catch
            {
                if (transaction != null)
                {
                    try
                    {
                        await transaction.RollbackAsync(
                            CancellationToken.None);
                    }
                    catch
                    {
                        /*
                         * Preservamos a exceção original.
                         *
                         * Se a própria ligação tiver falhado, o rollback
                         * também pode deixar de ser possível.
                         */
                    }
                }

                throw;
            }
            finally
            {
                if (transaction != null)
                {
                    await transaction.DisposeAsync();
                }

                if (shouldCloseConnection &&
                    sqlConnection.State != ConnectionState.Closed)
                {
                    await sqlConnection.CloseAsync();
                }
            }
        }

        public void ClearTracking()
        {
            _context.ChangeTracker.Clear();
        }

        private IQueryable<GameSearchCatalog> BuildBaseSearchQuery(
            bool? isExpansion)
        {
            IQueryable<GameSearchCatalog> query =
                _context.GameSearchCatalog
                    .AsNoTracking();

            if (isExpansion.HasValue)
            {
                query =
                    query.Where(x =>
                        x.IsExpansion ==
                        isExpansion.Value);
            }

            return query;
        }

        /// <summary>
        /// Constrói a tabela em memória enviada ao SqlBulkCopy.
        ///
        /// Contém apenas os dados básicos existentes no dump oficial
        /// do BGG.
        /// </summary>
        private static DataTable CreateBulkDataTable(
            IReadOnlyCollection<GameSearchCatalog> games)
        {
            var table =
                new DataTable();

            table.Columns.Add(
                "BggId",
                typeof(int));

            table.Columns.Add(
                "Name",
                typeof(string));

            table.Columns.Add(
                "NormalizedName",
                typeof(string));

            table.Columns.Add(
                "YearPublished",
                typeof(int));

            table.Columns.Add(
                "IsExpansion",
                typeof(bool));

            table.Columns.Add(
                "AverageRating",
                typeof(decimal));

            table.Columns.Add(
                "RatingsCount",
                typeof(int));

            table.Columns.Add(
                "BggRank",
                typeof(int));

            foreach (var game in games)
            {
                var row =
                    table.NewRow();

                row["BggId"] =
                    game.BggId;

                row["Name"] =
                    game.Name;

                row["NormalizedName"] =
                    game.NormalizedName;

                row["YearPublished"] =
                    game.YearPublished.HasValue
                        ? game.YearPublished.Value
                        : DBNull.Value;

                row["IsExpansion"] =
                    game.IsExpansion;

                row["AverageRating"] =
                    game.AverageRating.HasValue
                        ? Convert.ToDecimal(
                            game.AverageRating.Value)
                        : DBNull.Value;

                row["RatingsCount"] =
                    game.RatingsCount.HasValue
                        ? game.RatingsCount.Value
                        : DBNull.Value;

                row["BggRank"] =
                    game.BggRank.HasValue
                        ? game.BggRank.Value
                        : DBNull.Value;

                table.Rows.Add(
                    row);
            }

            return table;
        }

        private static void ValidateBulkGame(
            GameSearchCatalog game)
        {
            if (game.BggId <= 0)
            {
                throw new InvalidOperationException(
                    "O catálogo contém um BGG ID inválido.");
            }

            if (string.IsNullOrWhiteSpace(game.Name))
            {
                throw new InvalidOperationException(
                    $"O jogo BGG {game.BggId} não possui nome.");
            }

            if (string.IsNullOrWhiteSpace(game.NormalizedName))
            {
                throw new InvalidOperationException(
                    $"O jogo BGG {game.BggId} não possui nome normalizado.");
            }

            if (game.Name.Length > MaximumNameLength)
            {
                throw new InvalidOperationException(
                    $"O nome do jogo BGG {game.BggId} possui " +
                    $"{game.Name.Length} caracteres e excede o limite " +
                    $"{MaximumNameLength}.");
            }

            if (game.NormalizedName.Length > MaximumNameLength)
            {
                throw new InvalidOperationException(
                    $"O nome normalizado do jogo BGG {game.BggId} possui " +
                    $"{game.NormalizedName.Length} caracteres e excede o " +
                    $"limite {MaximumNameLength}.");
            }
        }
    }
}