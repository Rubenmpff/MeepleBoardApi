-- Só leitura. Executar exclusivamente DEPOIS de restaurar uma cópia com este nome.
-- Não restaura, migra, cria utilizadores, executa jobs nem consulta credenciais.
SET NOCOUNT ON;
IF DB_NAME() <> N'MeepleBoard_RealCopy'
    THROW 51000, 'Auditoria recusada: selecionar exclusivamente MeepleBoard_RealCopy.', 1;

SELECT DB_NAME() AS CopyDatabase,
       CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS SqlVersion;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory') IS NULL
    THROW 51001, 'Histórico de migrações ausente: rever a origem antes de continuar.', 1;

SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
SELECT t.name AS Entity, SUM(p.rows) AS RowCount
FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id
WHERE p.index_id IN (0, 1)
  AND t.name IN ('AspNetUsers', 'Games', 'Matches', 'MatchPlayers',
                 'MatchJournalEntries', 'GameSessions', 'UserGameLibraries')
GROUP BY t.name ORDER BY t.name;

SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE (TABLE_NAME = 'Matches' AND COLUMN_NAME IN ('CreatorId', 'GameMode', 'Result', 'SharedVictoryAllowed'))
   OR (TABLE_NAME = 'MatchPlayers' AND COLUMN_NAME = 'Outcome')
   OR (TABLE_NAME = 'MatchJournalEntries' AND COLUMN_NAME = 'PersonalRating');

-- Diagnóstico posterior às migrações, sem inferir/classificar resultados antigos.
IF COL_LENGTH(N'dbo.Matches', N'Result') IS NOT NULL
    EXEC sp_executesql N'SELECT Result, COUNT_BIG(*) AS Matches FROM dbo.Matches GROUP BY Result;';
IF COL_LENGTH(N'dbo.MatchPlayers', N'Outcome') IS NOT NULL
    EXEC sp_executesql N'SELECT Outcome, COUNT_BIG(*) AS Participants FROM dbo.MatchPlayers GROUP BY Outcome;';
IF COL_LENGTH(N'dbo.MatchJournalEntries', N'PersonalRating') IS NOT NULL
    EXEC sp_executesql N'SELECT COUNT_BIG(*) AS JournalEntries,
      SUM(CASE WHEN PersonalRating IS NULL THEN CAST(1 AS bigint) ELSE 0 END) AS Unrated,
      SUM(CASE WHEN PersonalRating = 0 THEN CAST(1 AS bigint) ELSE 0 END) AS ZeroRatings
      FROM dbo.MatchJournalEntries;';
