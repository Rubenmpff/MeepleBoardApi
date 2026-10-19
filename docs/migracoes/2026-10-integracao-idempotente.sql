BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823200024_AddGameSearchTokens'
)
BEGIN
    CREATE TABLE [GameSearchToken] (
        [Id] uniqueidentifier NOT NULL,
        [BggId] int NOT NULL,
        [Token] nvarchar(500) NOT NULL,
        [Position] smallint NOT NULL,
        CONSTRAINT [PK_GameSearchToken] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823200024_AddGameSearchTokens'
)
BEGIN
    CREATE INDEX [IX_GameSearchToken_BggId] ON [GameSearchToken] ([BggId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823200024_AddGameSearchTokens'
)
BEGIN
    CREATE UNIQUE INDEX [IX_GameSearchToken_BggId_Position] ON [GameSearchToken] ([BggId], [Position]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823200024_AddGameSearchTokens'
)
BEGIN
    CREATE INDEX [IX_GameSearchToken_Token_BggId] ON [GameSearchToken] ([Token], [BggId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823200024_AddGameSearchTokens'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823200024_AddGameSearchTokens', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823200922_ReduceGameSearchTokenLength'
)
BEGIN
    DROP INDEX [IX_GameSearchToken_Token_BggId] ON [GameSearchToken];
    DECLARE @var sysname;
    SELECT @var = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[GameSearchToken]') AND [c].[name] = N'Token');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [GameSearchToken] DROP CONSTRAINT [' + @var + '];');
    ALTER TABLE [GameSearchToken] ALTER COLUMN [Token] nvarchar(100) NOT NULL;
    CREATE INDEX [IX_GameSearchToken_Token_BggId] ON [GameSearchToken] ([Token], [BggId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823200922_ReduceGameSearchTokenLength'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823200922_ReduceGameSearchTokenLength', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824063025_OptimizeGameSearchTokenKey'
)
BEGIN
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
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824063025_OptimizeGameSearchTokenKey'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824063025_OptimizeGameSearchTokenKey', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120000_AddMatchCreator'
)
BEGIN
    ALTER TABLE [Matches] ADD [CreatorId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120000_AddMatchCreator'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261006120000_AddMatchCreator', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006130000_AddCatalogPendingDetailsIndex'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_GameSearchCatalog_RatingsCount_BggRank_AverageRating_Name_BggId] ON [GameSearchCatalog] ([RatingsCount] DESC, [BggRank], [AverageRating] DESC, [Name], [BggId]) WHERE [DetailsSyncedAt] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006130000_AddCatalogPendingDetailsIndex'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261006130000_AddCatalogPendingDetailsIndex', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006140000_PreserveJournalHalfRatings'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[MatchJournalEntries]') AND [c].[name] = N'PersonalRating');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [MatchJournalEntries] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [MatchJournalEntries] ALTER COLUMN [PersonalRating] float NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006140000_PreserveJournalHalfRatings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261006140000_PreserveJournalHalfRatings', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006150000_AddExplicitMatchOutcomes'
)
BEGIN
    ALTER TABLE [Matches] ADD [GameMode] nvarchar(16) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006150000_AddExplicitMatchOutcomes'
)
BEGIN
    ALTER TABLE [Matches] ADD [Result] nvarchar(16) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006150000_AddExplicitMatchOutcomes'
)
BEGIN
    ALTER TABLE [Matches] ADD [SharedVictoryAllowed] bit NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006150000_AddExplicitMatchOutcomes'
)
BEGIN
    ALTER TABLE [MatchPlayers] ADD [Outcome] nvarchar(16) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006150000_AddExplicitMatchOutcomes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261006150000_AddExplicitMatchOutcomes', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012080128_AddSessionCloseTime'
)
BEGIN
    ALTER TABLE [GameSessions] ADD [ClosedAutomatically] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012080128_AddSessionCloseTime'
)
BEGIN
    ALTER TABLE [GameSessions] ADD [ClosesAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012080128_AddSessionCloseTime'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261012080128_AddSessionCloseTime', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012081445_AddGameResources'
)
BEGIN
    CREATE TABLE [GameResources] (
        [Id] uniqueidentifier NOT NULL,
        [GameId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(16) NOT NULL,
        [PrintCategory] nvarchar(16) NULL,
        [Language] nvarchar(2) NULL,
        [Title] nvarchar(200) NOT NULL,
        [Author] nvarchar(120) NULL,
        [Platform] nvarchar(60) NOT NULL,
        [Url] nvarchar(500) NOT NULL,
        [Compatibility] nvarchar(200) NULL,
        [IsOfficial] bit NOT NULL,
        [IsFree] bit NULL,
        [Status] nvarchar(16) NOT NULL,
        [SuggestedById] uniqueidentifier NULL,
        [ReviewedById] uniqueidentifier NULL,
        [ReviewedAt] datetime2 NULL,
        [ReviewNote] nvarchar(300) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_GameResources] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_GameResources_AspNetUsers_SuggestedById] FOREIGN KEY ([SuggestedById]) REFERENCES [AspNetUsers] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_GameResources_Games_GameId] FOREIGN KEY ([GameId]) REFERENCES [Games] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012081445_AddGameResources'
)
BEGIN
    CREATE TABLE [GameResourceReports] (
        [Id] uniqueidentifier NOT NULL,
        [ResourceId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Reason] nvarchar(16) NOT NULL,
        [Comment] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ResolvedAt] datetime2 NULL,
        [ResolvedById] uniqueidentifier NULL,
        CONSTRAINT [PK_GameResourceReports] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_GameResourceReports_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_GameResourceReports_GameResources_ResourceId] FOREIGN KEY ([ResourceId]) REFERENCES [GameResources] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012081445_AddGameResources'
)
BEGIN
    CREATE INDEX [IX_GameResourceReports_ResourceId_UserId] ON [GameResourceReports] ([ResourceId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012081445_AddGameResources'
)
BEGIN
    CREATE INDEX [IX_GameResourceReports_UserId] ON [GameResourceReports] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012081445_AddGameResources'
)
BEGIN
    CREATE INDEX [IX_GameResources_GameId_Status] ON [GameResources] ([GameId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012081445_AddGameResources'
)
BEGIN
    CREATE INDEX [IX_GameResources_SuggestedById] ON [GameResources] ([SuggestedById]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012081445_AddGameResources'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261012081445_AddGameResources', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014100853_AddGameResourceExpansionThumbnail'
)
BEGIN
    ALTER TABLE [GameResources] ADD [Expansion] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014100853_AddGameResourceExpansionThumbnail'
)
BEGIN
    ALTER TABLE [GameResources] ADD [ThumbnailUrl] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014100853_AddGameResourceExpansionThumbnail'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261014100853_AddGameResourceExpansionThumbnail', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014164836_AddGameSheetAndSessionPlannedGame'
)
BEGIN
    ALTER TABLE [GameSessions] ADD [PlannedGameId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014164836_AddGameSheetAndSessionPlannedGame'
)
BEGIN
    ALTER TABLE [Games] ADD [AverageWeight] float NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014164836_AddGameSheetAndSessionPlannedGame'
)
BEGIN
    ALTER TABLE [Games] ADD [MaxPlayTime] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014164836_AddGameSheetAndSessionPlannedGame'
)
BEGIN
    ALTER TABLE [Games] ADD [MinAge] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014164836_AddGameSheetAndSessionPlannedGame'
)
BEGIN
    ALTER TABLE [Games] ADD [MinPlayTime] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014164836_AddGameSheetAndSessionPlannedGame'
)
BEGIN
    CREATE INDEX [IX_GameSessions_PlannedGameId] ON [GameSessions] ([PlannedGameId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014164836_AddGameSheetAndSessionPlannedGame'
)
BEGIN
    ALTER TABLE [GameSessions] ADD CONSTRAINT [FK_GameSessions_Games_PlannedGameId] FOREIGN KEY ([PlannedGameId]) REFERENCES [Games] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014164836_AddGameSheetAndSessionPlannedGame'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261014164836_AddGameSheetAndSessionPlannedGame', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016083156_AddDurationMethodAndSharing'
)
BEGIN
    ALTER TABLE [Matches] ADD [DurationMethod] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016083156_AddDurationMethodAndSharing'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [ContributesDurationStats] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016083156_AddDurationMethodAndSharing'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261016083156_AddDurationMethodAndSharing', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016183656_AddRatingCountsAndExperiencePublications'
)
BEGIN
    ALTER TABLE [Games] ADD [MeepleBoardRaters] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016183656_AddRatingCountsAndExperiencePublications'
)
BEGIN
    ALTER TABLE [Games] ADD [MeepleBoardRatingsCount] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016183656_AddRatingCountsAndExperiencePublications'
)
BEGIN
    CREATE TABLE [MatchExperiencePublications] (
        [Id] uniqueidentifier NOT NULL,
        [JournalEntryId] uniqueidentifier NOT NULL,
        [RatingPublishedAt] datetime2 NULL,
        [Comment] nvarchar(500) NULL,
        [CommentPublishedAt] datetime2 NULL,
        [CommentEditedAt] datetime2 NULL,
        [HiddenAt] datetime2 NULL,
        [HiddenReason] nvarchar(32) NULL,
        [HiddenById] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_MatchExperiencePublications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MatchExperiencePublications_MatchJournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [MatchJournalEntries] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016183656_AddRatingCountsAndExperiencePublications'
)
BEGIN
    CREATE TABLE [ExperienceReports] (
        [Id] uniqueidentifier NOT NULL,
        [PublicationId] uniqueidentifier NOT NULL,
        [ReporterId] uniqueidentifier NOT NULL,
        [Reason] nvarchar(16) NOT NULL,
        [Detail] nvarchar(300) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ResolvedAt] datetime2 NULL,
        [ResolvedById] uniqueidentifier NULL,
        [Outcome] nvarchar(16) NULL,
        CONSTRAINT [PK_ExperienceReports] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ExperienceReports_AspNetUsers_ReporterId] FOREIGN KEY ([ReporterId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ExperienceReports_MatchExperiencePublications_PublicationId] FOREIGN KEY ([PublicationId]) REFERENCES [MatchExperiencePublications] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016183656_AddRatingCountsAndExperiencePublications'
)
BEGIN
    CREATE INDEX [IX_ExperienceReports_PublicationId_ReporterId] ON [ExperienceReports] ([PublicationId], [ReporterId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016183656_AddRatingCountsAndExperiencePublications'
)
BEGIN
    CREATE INDEX [IX_ExperienceReports_ReporterId] ON [ExperienceReports] ([ReporterId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016183656_AddRatingCountsAndExperiencePublications'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MatchExperiencePublications_JournalEntryId] ON [MatchExperiencePublications] ([JournalEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016183656_AddRatingCountsAndExperiencePublications'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261016183656_AddRatingCountsAndExperiencePublications', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016201449_AddUserDeletedAt'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [DeletedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016201449_AddUserDeletedAt'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261016201449_AddUserDeletedAt', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018075754_AddBlocksReportsAndSuspension'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [SuspendedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018075754_AddBlocksReportsAndSuspension'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [SuspendedById] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018075754_AddBlocksReportsAndSuspension'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [SuspendedUntil] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018075754_AddBlocksReportsAndSuspension'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [SuspensionLiftedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018075754_AddBlocksReportsAndSuspension'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [SuspensionLiftedById] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018075754_AddBlocksReportsAndSuspension'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [SuspensionReason] nvarchar(300) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018075754_AddBlocksReportsAndSuspension'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [TokensRevokedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018075754_AddBlocksReportsAndSuspension'
)
BEGIN
    CREATE TABLE [PhotoReports] (
        [Id] uniqueidentifier NOT NULL,
        [ReporterId] uniqueidentifier NOT NULL,
        [JournalEntryId] uniqueidentifier NOT NULL,
        [PhotoUrl] nvarchar(500) NOT NULL,
        [Reason] nvarchar(16) NOT NULL,
        [Detail] nvarchar(300) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ResolvedAt] datetime2 NULL,
        [ResolvedById] uniqueidentifier NULL,
        [Outcome] nvarchar(16) NULL,
        CONSTRAINT [PK_PhotoReports] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PhotoReports_AspNetUsers_ReporterId] FOREIGN KEY ([ReporterId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PhotoReports_MatchJournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [MatchJournalEntries] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018075754_AddBlocksReportsAndSuspension'
)
BEGIN
    CREATE TABLE [UserBlocks] (
        [BlockerId] uniqueidentifier NOT NULL,
        [BlockedId] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_UserBlocks] PRIMARY KEY ([BlockerId], [BlockedId]),
        CONSTRAINT [FK_UserBlocks_AspNetUsers_BlockedId] FOREIGN KEY ([BlockedId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_UserBlocks_AspNetUsers_BlockerId] FOREIGN KEY ([BlockerId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018075754_AddBlocksReportsAndSuspension'
)
BEGIN
    CREATE TABLE [UserReports] (
        [Id] uniqueidentifier NOT NULL,
        [ReporterId] uniqueidentifier NOT NULL,
        [ReportedUserId] uniqueidentifier NOT NULL,
        [Reason] nvarchar(16) NOT NULL,
        [Detail] nvarchar(300) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ResolvedAt] datetime2 NULL,
        [ResolvedById] uniqueidentifier NULL,
        [Outcome] nvarchar(16) NULL,
        CONSTRAINT [PK_UserReports] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserReports_AspNetUsers_ReportedUserId] FOREIGN KEY ([ReportedUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_UserReports_AspNetUsers_ReporterId] FOREIGN KEY ([ReporterId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018075754_AddBlocksReportsAndSuspension'
)
BEGIN
    CREATE INDEX [IX_PhotoReports_JournalEntryId_ReporterId] ON [PhotoReports] ([JournalEntryId], [ReporterId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018075754_AddBlocksReportsAndSuspension'
)
BEGIN
    CREATE INDEX [IX_PhotoReports_ReporterId] ON [PhotoReports] ([ReporterId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018075754_AddBlocksReportsAndSuspension'
)
BEGIN
    CREATE INDEX [IX_UserBlocks_BlockedId] ON [UserBlocks] ([BlockedId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018075754_AddBlocksReportsAndSuspension'
)
BEGIN
    CREATE INDEX [IX_UserReports_ReportedUserId_ReporterId] ON [UserReports] ([ReportedUserId], [ReporterId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018075754_AddBlocksReportsAndSuspension'
)
BEGIN
    CREATE INDEX [IX_UserReports_ReporterId] ON [UserReports] ([ReporterId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018075754_AddBlocksReportsAndSuspension'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261018075754_AddBlocksReportsAndSuspension', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018163245_AddPublicationPhotos'
)
BEGIN
    CREATE TABLE [PublicationPhotos] (
        [Id] uniqueidentifier NOT NULL,
        [PublicationId] uniqueidentifier NOT NULL,
        [PhotoUrl] nvarchar(500) NOT NULL,
        [Position] int NOT NULL,
        [PublishedAt] datetime2 NOT NULL,
        [HiddenAt] datetime2 NULL,
        [HiddenReason] nvarchar(32) NULL,
        [HiddenById] uniqueidentifier NULL,
        [RemovedAt] datetime2 NULL,
        CONSTRAINT [PK_PublicationPhotos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PublicationPhotos_MatchExperiencePublications_PublicationId] FOREIGN KEY ([PublicationId]) REFERENCES [MatchExperiencePublications] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018163245_AddPublicationPhotos'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PublicationPhotos_PublicationId_PhotoUrl] ON [PublicationPhotos] ([PublicationId], [PhotoUrl]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018163245_AddPublicationPhotos'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261018163245_AddPublicationPhotos', N'9.0.2');
END;

COMMIT;
GO

