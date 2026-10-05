using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace MeepleBoard.Infra.Data.Repositories
{
    public class GameSearchCatalogRepository
        : IGameSearchCatalogRepository
    {
        private const int MaxSearchLimit = 100;
        private const int SqlBatchSize = 200;
        private const int MaximumNameLength = 500;
        private const int MaximumTokenLength = 100;

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
            int? playerCount = null,
            double? minBggRating = null,
            string sort = "relevance",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                return new List<GameSearchCatalog>();
            }

            var searchTerm = normalizedQuery.Trim();

            if (searchTerm.Length < 2 ||
                searchTerm.Length > MaximumNameLength)
            {
                return new List<GameSearchCatalog>();
            }

            var safeOffset = Math.Max(0, offset);

            var safeLimit = Math.Clamp(
                limit,
                1,
                MaxSearchLimit);

            var normalizedSort = NormalizeSort(sort);

            /*
             * Escapamos os caracteres especiais do LIKE para que a pesquisa
             * trate %, _, [ e \ como texto normal introduzido pelo utilizador.
             */
            var titlePrefix = EscapeLikePattern(searchTerm) + "%";
            var tokenPrefix = EscapeLikePattern(searchTerm) + "%";

            /*
             * A ordenação é escolhida apenas entre valores internos conhecidos.
             * Nunca é inserido SQL fornecido pelo utilizador.
             */
            var orderBy = normalizedSort switch
            {
                "most_known" => """
                    ISNULL(g.[RatingsCount], 0) DESC,
                    CASE WHEN g.[NormalizedName] = @searchTerm THEN 1 ELSE 0 END DESC,
                    m.[MatchType] DESC,
                    ISNULL(g.[BggRank], 2147483647) ASC,
                    ISNULL(g.[AverageRating], 0) DESC,
                    g.[Name] ASC,
                    g.[BggId] ASC
                    """,

                "bgg_rating" => """
                    ISNULL(g.[AverageRating], 0) DESC,
                    ISNULL(g.[RatingsCount], 0) DESC,
                    CASE WHEN g.[NormalizedName] = @searchTerm THEN 1 ELSE 0 END DESC,
                    m.[MatchType] DESC,
                    ISNULL(g.[BggRank], 2147483647) ASC,
                    g.[Name] ASC,
                    g.[BggId] ASC
                    """,

                "year_desc" => """
                    ISNULL(g.[YearPublished], -2147483648) DESC,
                    CASE WHEN g.[NormalizedName] = @searchTerm THEN 1 ELSE 0 END DESC,
                    m.[MatchType] DESC,
                    ISNULL(g.[RatingsCount], 0) DESC,
                    ISNULL(g.[BggRank], 2147483647) ASC,
                    g.[Name] ASC,
                    g.[BggId] ASC
                    """,

                "name_asc" => """
                    g.[Name] ASC,
                    CASE WHEN g.[NormalizedName] = @searchTerm THEN 1 ELSE 0 END DESC,
                    m.[MatchType] DESC,
                    ISNULL(g.[RatingsCount], 0) DESC,
                    g.[BggId] ASC
                    """,

                _ => """
                    CASE WHEN g.[NormalizedName] = @searchTerm THEN 1 ELSE 0 END DESC,
                    m.[MatchType] DESC,
                    ISNULL(g.[RatingsCount], 0) DESC,
                    ISNULL(g.[BggRank], 2147483647) ASC,
                    ISNULL(g.[AverageRating], 0) DESC,
                    g.[Name] ASC,
                    g.[BggId] ASC
                    """
            };

            /*
             * Materializamos primeiro os candidatos numa tabela temporária.
             *
             * Razão:
             * - os dois ramos (NormalizedName e Token) conseguem usar os seus índices;
             * - eliminamos duplicados antes do JOIN/ranking;
             * - o SQL Server obtém estatísticas reais sobre #SearchMatches;
             * - evitamos o plano instável observado com UNION ALL + GROUP BY + JOIN
             *   dentro do mesmo CTE;
             * - título tem prioridade sobre token (MatchType 2 > 1).
             */
            var sql = $"""
                SET NOCOUNT ON;

                CREATE TABLE #SearchMatches
                (
                    [BggId] INT NOT NULL PRIMARY KEY,
                    [MatchType] TINYINT NOT NULL
                );

                INSERT INTO #SearchMatches
                (
                    [BggId],
                    [MatchType]
                )
                SELECT
                    c.[BggId],
                    CAST(2 AS tinyint)
                FROM [dbo].[GameSearchCatalog] AS c
                WHERE c.[NormalizedName] LIKE @titlePrefix ESCAPE '\';

                INSERT INTO #SearchMatches
                (
                    [BggId],
                    [MatchType]
                )
                SELECT DISTINCT
                    t.[BggId],
                    CAST(1 AS tinyint)
                FROM [dbo].[GameSearchToken] AS t
                WHERE t.[Token] LIKE @tokenPrefix ESCAPE '\'
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM #SearchMatches AS existing
                      WHERE existing.[BggId] = t.[BggId]
                  );

                SELECT
                    g.[Id],
                    g.[AverageRating],
                    g.[BggId],
                    g.[BggRank],
                    g.[CreatedAt],
                    g.[DetailsSyncedAt],
                    g.[IsCooperative],
                    g.[IsExpansion],
                    g.[LastSyncedAt],
                    g.[MaxPlayers],
                    g.[MinPlayers],
                    g.[Name],
                    g.[NormalizedName],
                    g.[RatingsCount],
                    g.[SupportsCampaign],
                    g.[ThumbnailUrl],
                    g.[YearPublished]
                FROM #SearchMatches AS m
                INNER JOIN [dbo].[GameSearchCatalog] AS g
                    ON g.[BggId] = m.[BggId]
                WHERE
                    (
                        @isExpansion IS NULL
                        OR g.[IsExpansion] = @isExpansion
                    )
                    AND
                    (
                        @playerCount IS NULL
                        OR @playerCount <= 0
                        OR
                        (
                            @playerCount = 5
                            AND g.[MinPlayers] IS NOT NULL
                            AND g.[MaxPlayers] IS NOT NULL
                            AND g.[MaxPlayers] >= 5
                        )
                        OR
                        (
                            @playerCount <> 5
                            AND @playerCount > 0
                            AND g.[MinPlayers] IS NOT NULL
                            AND g.[MaxPlayers] IS NOT NULL
                            AND g.[MinPlayers] <= @playerCount
                            AND g.[MaxPlayers] >= @playerCount
                        )
                    )
                    AND
                    (
                        @minBggRating IS NULL
                        OR
                        (
                            g.[AverageRating] IS NOT NULL
                            AND g.[AverageRating] >= @minBggRating
                        )
                    )
                ORDER BY
                    {orderBy}
                OFFSET @offset ROWS
                FETCH NEXT @limit ROWS ONLY
                OPTION (RECOMPILE);
                """;

            var parameters = new[]
            {
                new SqlParameter(
                    "@titlePrefix",
                    SqlDbType.NVarChar,
                    MaximumNameLength + 1)
                {
                    Value = titlePrefix
                },

                new SqlParameter(
                    "@tokenPrefix",
                    SqlDbType.NVarChar,
                    MaximumTokenLength + 1)
                {
                    Value = tokenPrefix
                },

                new SqlParameter(
                    "@searchTerm",
                    SqlDbType.NVarChar,
                    MaximumNameLength)
                {
                    Value = searchTerm
                },

                new SqlParameter(
                    "@isExpansion",
                    SqlDbType.Bit)
                {
                    Value = isExpansion.HasValue
                        ? isExpansion.Value
                        : DBNull.Value
                },

                new SqlParameter(
                    "@playerCount",
                    SqlDbType.Int)
                {
                    Value = playerCount.HasValue
                        ? playerCount.Value
                        : DBNull.Value
                },

                new SqlParameter(
                    "@minBggRating",
                    SqlDbType.Float)
                {
                    Value = minBggRating.HasValue
                        ? minBggRating.Value
                        : DBNull.Value
                },

                new SqlParameter(
                    "@offset",
                    SqlDbType.Int)
                {
                    Value = safeOffset
                },

                new SqlParameter(
                    "@limit",
                    SqlDbType.Int)
                {
                    Value = safeLimit
                }
            };

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                await LogSearchDiagnosticsAsync(
                    titlePrefix,
                    tokenPrefix,
                    cancellationToken);

                Console.WriteLine(
                    $"[SEARCH] Repository START | query={searchTerm} | offset={safeOffset} | limit={safeLimit} | sort={normalizedSort}");

                Console.WriteLine(
                    $"[SEARCH] SQL START | query={searchTerm}");

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                var results = await _context.GameSearchCatalog
                    .FromSqlRaw(
                        sql,
                        parameters)
                    .AsNoTracking()
                    .ToListAsync(
                        cancellationToken);

                stopwatch.Stop();

                Console.WriteLine(
                    $"[SEARCH] SQL END | query={searchTerm} | {stopwatch.ElapsedMilliseconds}ms | count={results.Count}");

                Console.WriteLine(
                    $"[SEARCH] Repository END | query={searchTerm} | offset={safeOffset} | count={results.Count}");

                return results;
            }
            catch (SqlException ex)
                when (
                    cancellationToken.IsCancellationRequested &&
                    ex.Message.Contains(
                        "Operation cancelled by user",
                        StringComparison.OrdinalIgnoreCase))
            {
                throw new OperationCanceledException(
                    "A pesquisa foi cancelada porque o pedido deixou de ser atual.",
                    ex,
                    cancellationToken);
            }
        }


        private async Task LogSearchDiagnosticsAsync(
            string titlePrefix,
            string tokenPrefix,
            CancellationToken cancellationToken)
        {
            var connection =
                _context.Database.GetDbConnection();

            var openedHere = false;

            try
            {
                if (connection.State != ConnectionState.Open)
                {
                    var openWatch =
                        System.Diagnostics.Stopwatch.StartNew();

                    await connection.OpenAsync(
                        cancellationToken);

                    openWatch.Stop();

                    openedHere = true;

                    Console.WriteLine(
                        $"[SEARCH-DIAG] CONNECTION OPEN | {openWatch.ElapsedMilliseconds}ms");
                }
                else
                {
                    Console.WriteLine(
                        "[SEARCH-DIAG] CONNECTION ALREADY OPEN");
                }

                await using (
                    var titleCommand =
                        connection.CreateCommand())
                {
                    titleCommand.CommandText =
                        """
                        SELECT COUNT_BIG(*)
                        FROM [dbo].[GameSearchCatalog] AS c
                        WHERE c.[NormalizedName] LIKE @titlePrefix ESCAPE '\';
                        """;

                    var parameter =
                        titleCommand.CreateParameter();

                    parameter.ParameterName =
                        "@titlePrefix";

                    parameter.DbType =
                        DbType.String;

                    parameter.Size =
                        MaximumNameLength + 1;

                    parameter.Value =
                        titlePrefix;

                    titleCommand.Parameters.Add(
                        parameter);

                    var watch =
                        System.Diagnostics.Stopwatch.StartNew();

                    var count =
                        Convert.ToInt64(
                            await titleCommand.ExecuteScalarAsync(
                                cancellationToken));

                    watch.Stop();

                    Console.WriteLine(
                        $"[SEARCH-DIAG] TITLE PREFIX | {watch.ElapsedMilliseconds}ms | count={count}");
                }

                await using (
                    var tokenCommand =
                        connection.CreateCommand())
                {
                    tokenCommand.CommandText =
                        """
                        SELECT COUNT_BIG(*)
                        FROM [dbo].[GameSearchToken] AS t
                        WHERE t.[Token] LIKE @tokenPrefix ESCAPE '\';
                        """;

                    var parameter =
                        tokenCommand.CreateParameter();

                    parameter.ParameterName =
                        "@tokenPrefix";

                    parameter.DbType =
                        DbType.String;

                    parameter.Size =
                        MaximumTokenLength + 1;

                    parameter.Value =
                        tokenPrefix;

                    tokenCommand.Parameters.Add(
                        parameter);

                    var watch =
                        System.Diagnostics.Stopwatch.StartNew();

                    var count =
                        Convert.ToInt64(
                            await tokenCommand.ExecuteScalarAsync(
                                cancellationToken));

                    watch.Stop();

                    Console.WriteLine(
                        $"[SEARCH-DIAG] TOKEN PREFIX | {watch.ElapsedMilliseconds}ms | count={count}");
                }
            }
            catch (Exception ex)
                when (
                    ex is not OperationCanceledException)
            {
                /*
                 * Diagnóstico nunca deve impedir a pesquisa real.
                 */
                Console.WriteLine(
                    $"[SEARCH-DIAG] FAILED | {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (openedHere &&
                    connection.State ==
                    ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
        }

        public async Task<List<GameSearchCatalog>> GetCandidatesForEnrichmentAsync(
            int limit,
            CancellationToken cancellationToken = default)
        {
            var safeLimit =
                Math.Clamp(
                    limit,
                    1,
                    10_000);

            return await _context.GameSearchCatalog
                .AsNoTracking()
                .Where(game =>
                    game.DetailsSyncedAt == null)
                .OrderByDescending(game =>
                    game.RatingsCount ?? 0)
                .ThenBy(game =>
                    game.BggRank ?? int.MaxValue)
                .ThenByDescending(game =>
                    game.AverageRating ?? 0)
                .ThenBy(game =>
                    game.Name)
                .ThenBy(game =>
                    game.BggId)
                .Take(safeLimit)
                .ToListAsync(
                    cancellationToken);
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
                    createCommand.CommandTimeout = 300;

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

                    bulkCopy.BulkCopyTimeout = 300;

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

                        target.[IsExpansion] =
                            source.[IsExpansion],

                        target.[AverageRating] =
                            source.[AverageRating],

                        target.[RatingsCount] =
                            source.[RatingsCount],

                        target.[BggRank] =
                            source.[BggRank],

                        target.[LastSyncedAt] =
                            SYSUTCDATETIME()

                    FROM [dbo].[GameSearchCatalog] AS target

                    INNER JOIN #GameSearchCatalogImport AS source
                        ON source.[BggId] = target.[BggId]

                    WHERE
                        target.[Name] <> source.[Name]

                        OR target.[NormalizedName] <>
                           source.[NormalizedName]

                        OR
                        (
                            source.[YearPublished] IS NOT NULL
                            AND
                            (
                                target.[YearPublished] IS NULL
                                OR
                                target.[YearPublished] <>
                                source.[YearPublished]
                            )
                        )

                        OR target.[IsExpansion] <>
                           source.[IsExpansion]

                        OR
                        (
                            target.[AverageRating] <>
                            source.[AverageRating]

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
                            target.[RatingsCount] <>
                            source.[RatingsCount]

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
                            target.[BggRank] <>
                            source.[BggRank]

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
                    updateCommand.CommandTimeout = 300;

                    await updateCommand.ExecuteNonQueryAsync(
                        cancellationToken);
                }

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

                        WHERE target.[BggId] =
                              source.[BggId]
                    );
                    """;

                await using (
                    var insertCommand =
                        new SqlCommand(
                            insertSql,
                            sqlConnection,
                            transaction))
                {
                    insertCommand.CommandTimeout = 300;

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
                        // Ignorar erro durante rollback.
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
                    sqlConnection.State !=
                    ConnectionState.Closed)
                {
                    await sqlConnection.CloseAsync();
                }
            }
        }

        public async Task<int> RebuildSearchTokensAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dbConnection =
                _context.Database.GetDbConnection();

            if (dbConnection is not SqlConnection sqlConnection)
            {
                throw new InvalidOperationException(
                    "RebuildSearchTokensAsync requer Microsoft SQL Server.");
            }

            var shouldCloseConnection =
                sqlConnection.State != ConnectionState.Open;

            if (shouldCloseConnection)
            {
                await sqlConnection.OpenAsync(
                    cancellationToken);
            }

            try
            {
                await using (
                    var truncateCommand =
                        new SqlCommand(
                            "TRUNCATE TABLE [dbo].[GameSearchToken];",
                            sqlConnection))
                {
                    truncateCommand.CommandTimeout = 60;

                    await truncateCommand.ExecuteNonQueryAsync(
                        cancellationToken);
                }

                const int catalogBatchSize = 2_000;
                const int tokenBatchSize = 2_000;

                var tokenTable =
                    CreateSearchTokenDataTable();

                var insertedTokens = 0;
                var lastBggId = 0;

                using var bulkCopy =
                    new SqlBulkCopy(
                        sqlConnection,
                        SqlBulkCopyOptions.TableLock |
                        SqlBulkCopyOptions.UseInternalTransaction,
                        externalTransaction: null);

                bulkCopy.DestinationTableName =
                    "[dbo].[GameSearchToken]";

                bulkCopy.BulkCopyTimeout = 60;
                bulkCopy.BatchSize = tokenBatchSize;
                bulkCopy.EnableStreaming = true;

                bulkCopy.ColumnMappings.Add(
                    "BggId",
                    "BggId");

                bulkCopy.ColumnMappings.Add(
                    "Token",
                    "Token");

                bulkCopy.ColumnMappings.Add(
                    "Position",
                    "Position");

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var catalogRows =
                        await _context.GameSearchCatalog
                            .AsNoTracking()
                            .Where(x =>
                                x.BggId > lastBggId)
                            .OrderBy(x =>
                                x.BggId)
                            .Select(x => new
                            {
                                x.BggId,
                                x.NormalizedName
                            })
                            .Take(catalogBatchSize)
                            .ToListAsync(
                                cancellationToken);

                    if (catalogRows.Count == 0)
                    {
                        break;
                    }

                    foreach (var catalogRow in catalogRows)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (catalogRow.BggId <= 0 ||
                            string.IsNullOrWhiteSpace(
                                catalogRow.NormalizedName))
                        {
                            continue;
                        }

                        var words =
                            catalogRow.NormalizedName.Split(
                                ' ',
                                StringSplitOptions.RemoveEmptyEntries |
                                StringSplitOptions.TrimEntries);

                        /*
                         * Começamos no índice 1 porque a primeira
                         * palavra já é coberta pelo NormalizedName.
                         */
                        for (
                            var index = 1;
                            index < words.Length;
                            index++)
                        {
                            var token =
                                words[index];

                            if (token.Length < 2 ||
                                token.Length > MaximumTokenLength)
                            {
                                continue;
                            }

                            var row =
                                tokenTable.NewRow();

                            row["BggId"] =
                                catalogRow.BggId;

                            row["Token"] =
                                token;

                            row["Position"] =
                                checked((short)index);

                            tokenTable.Rows.Add(
                                row);

                            if (tokenTable.Rows.Count <
                                tokenBatchSize)
                            {
                                continue;
                            }

                            await bulkCopy.WriteToServerAsync(
                                tokenTable,
                                cancellationToken);

                            insertedTokens +=
                                tokenTable.Rows.Count;

                            tokenTable.Clear();

                            await Task.Yield();
                        }
                    }

                    lastBggId =
                        catalogRows[^1].BggId;

                    if (catalogRows.Count <
                        catalogBatchSize)
                    {
                        break;
                    }

                    await Task.Yield();
                }

                if (tokenTable.Rows.Count > 0)
                {
                    await bulkCopy.WriteToServerAsync(
                        tokenTable,
                        cancellationToken);

                    insertedTokens +=
                        tokenTable.Rows.Count;

                    tokenTable.Clear();
                }

                return insertedTokens;
            }
            finally
            {
                if (shouldCloseConnection &&
                    sqlConnection.State !=
                    ConnectionState.Closed)
                {
                    await sqlConnection.CloseAsync();
                }
            }
        }

        private static DataTable CreateSearchTokenDataTable()
        {
            var table =
                new DataTable();

            table.Columns.Add(
                "BggId",
                typeof(int));

            table.Columns.Add(
                "Token",
                typeof(string));

            table.Columns.Add(
                "Position",
                typeof(short));

            return table;
        }

        public void ClearTracking()
        {
            _context.ChangeTracker.Clear();
        }

        private static string EscapeLikePattern(
            string value)
        {
            ArgumentNullException.ThrowIfNull(value);

            return value
                .Replace(@"\", @"\\")
                .Replace("%", @"\%")
                .Replace("_", @"\_")
                .Replace("[", @"\[");
        }

        private static string NormalizeSort(
            string? sort)
        {
            return sort?
                .Trim()
                .ToLowerInvariant() switch
            {
                "most_known" => "most_known",
                "bgg_rating" => "bgg_rating",
                "year_desc" => "year_desc",
                "name_asc" => "name_asc",
                _ => "relevance"
            };
        }

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

            if (string.IsNullOrWhiteSpace(
                    game.Name))
            {
                throw new InvalidOperationException(
                    $"O jogo BGG {game.BggId} não possui nome.");
            }

            if (string.IsNullOrWhiteSpace(
                    game.NormalizedName))
            {
                throw new InvalidOperationException(
                    $"O jogo BGG {game.BggId} não possui nome normalizado.");
            }

            if (game.Name.Length >
                MaximumNameLength)
            {
                throw new InvalidOperationException(
                    $"O nome do jogo BGG {game.BggId} possui " +
                    $"{game.Name.Length} caracteres e excede o limite " +
                    $"{MaximumNameLength}.");
            }

            if (game.NormalizedName.Length >
                MaximumNameLength)
            {
                throw new InvalidOperationException(
                    $"O nome normalizado do jogo BGG {game.BggId} possui " +
                    $"{game.NormalizedName.Length} caracteres e excede o " +
                    $"limite {MaximumNameLength}.");
            }
        }
    }
}