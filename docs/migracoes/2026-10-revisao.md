# Revisão das migrações — integração 2026-10

**Script:** `2026-10-integracao-idempotente.sql`, gerado com `dotnet ef migrations script 20260823134606_IncreaseGameSearchCatalogNameLength --idempotent`. Só foi gerado e revisto; **não foi aplicado em lado nenhum**.

**Ponto de partida que o script pressupõe:** a última migração de `origin/develop`, `20260823134606_IncreaseGameSearchCatalogNameLength`. Antes de qualquer execução no QA, é preciso confirmar a migração aplicada nesse ambiente:

```sql
SELECT TOP 5 [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC;
```

- **Se a última for outra,** o script tem de ser gerado de novo a partir dessa migração.
- **Se faltar alguma anterior,** não se executa.

## As 16 migrações

| # | Migração | Efeito |
| --- | --- | --- |
| 1 | `20260823200024_AddGameSearchTokens` | Tabela nova `GameSearchToken` (índice de pesquisa). |
| 2 | `20260823200922_ReduceGameSearchTokenLength` | `Token` 500 → 100 caracteres, na tabela criada em 1. |
| 3 | `20260824063025_OptimizeGameSearchTokenKey` | **Recria** `GameSearchToken` com chave composta (SQL próprio: `DROP TABLE` + `CREATE TABLE`). |
| 4 | `20261006120000_AddMatchCreator` | `Matches.CreatorId` anulável. |
| 5 | `20261006130000_AddCatalogPendingDetailsIndex` | Índice filtrado em `GameSearchCatalog`. |
| 6 | `20261006140000_PreserveJournalHalfRatings` | `MatchJournalEntries.PersonalRating`: `int` → `float`, anulável. |
| 7 | `20261006150000_AddExplicitMatchOutcomes` | 4 colunas anuláveis (modo, resultado, vitória partilhada, resultado individual). |
| 8 | `20261012080128_AddSessionCloseTime` | 2 colunas de fecho de sessão; `ClosedAutomatically bit NOT NULL DEFAULT 0`. |
| 9 | `20261012081445_AddGameResources` | Tabelas novas de recursos e denúncias de recursos. |
| 10 | `20261014100853_AddGameResourceExpansionThumbnail` | 2 colunas anuláveis em `GameResources`. |
| 11 | `20261014164836_AddGameSheetAndSessionPlannedGame` | Colunas da ficha do jogo em `Games`. `GameSessions.PlannedGameId` anulável, com FK `ON DELETE SET NULL` e índice. |
| 12 | `20261016083156_AddDurationMethodAndSharing` | `Matches.DurationMethod` anulável; `AspNetUsers.ContributesDurationStats bit NOT NULL DEFAULT 0`. |
| 13 | `20261016183656_AddRatingCountsAndExperiencePublications` | `Games.MeepleBoardRaters` e `MeepleBoardRatingsCount` `int NOT NULL DEFAULT 0`. Tabelas `MatchExperiencePublications` e `ExperienceReports`. |
| 14 | `20261016201449_AddUserDeletedAt` | `AspNetUsers.DeletedAt` anulável. |
| 15 | `20261018075754_AddBlocksReportsAndSuspension` | Tabelas `UserBlocks`, `UserReports` e `PhotoReports`; 7 colunas anuláveis em `AspNetUsers`. |
| 16 | `20261018163245_AddPublicationPhotos` | Tabela `PublicationPhotos`, removida com a publicação. |

**Contagem no script:**

- 10 `CREATE TABLE`, 20 índices (3 únicos) e 15 chaves estrangeiras;
- 26 colunas novas, das quais 4 `NOT NULL` com valor por omissão;
- 2 `ALTER COLUMN`;
- 1 `DROP TABLE`;
- 0 `UPDATE`, `DELETE`, `TRUNCATE` ou mudanças de nome.

## Perdas de dados

- **`DROP TABLE [GameSearchToken]` (3):**
  - É um índice **derivado** do `GameSearchCatalog`; não tem dados de utilizadores.
  - No percurso previsto, a tabela é criada vazia em 1 e recriada em 3.
  - Se o QA já a tiver (aplicação manual anterior), os tokens perdem-se e voltam com o `BggSearchTokenRebuildJob`.
  - **Até essa reconstrução, a pesquisa do catálogo fica incompleta.**
- **`Token` 500 → 100 (2):** só é seguro com a tabela vazia, que é o percurso previsto. Se já tiver tokens com mais de 100 caracteres, a instrução falha e toda a transação é revertida. Não há perda silenciosa.
- **`PersonalRating int → float` (6):** alargamento; todos os valores inteiros ficam iguais. Sem perda.
- **Nenhuma outra remoção nem escrita de dados.**

## Valores por omissão

- **`ClosedAutomatically = 0`:** correto para sessões antigas.
- **`ContributesDurationStats = 0`:** partilha desligada por omissão; é uma escolha de privacidade.
- **`MeepleBoardRaters = 0` e `MeepleBoardRatingsCount = 0`:** correto à partida, mas os contadores só ficam certos depois de `POST /MeepleBoard/game/rankings/recompute` (só para administradores).

## Índices, restrições e bloqueios

- **Script inteiro numa só transação (`BEGIN TRANSACTION` … `COMMIT`):**
  - Ou entram as 16 migrações, ou nenhuma.
  - Os bloqueios mantêm-se até ao fim. Executar numa janela calma.
- **`PersonalRating` (6):** a mudança de tipo reescreve as linhas de `MatchJournalEntries` com bloqueio exclusivo de esquema. Os diários ficam inacessíveis durante essa operação; a tabela é pequena.
- **Índice filtrado em `GameSearchCatalog` (5):** é criado sem `ONLINE`. Bloqueia escritas no catálogo (importações) enquanto é construído; as leituras continuam. Não correr com uma importação do BGG a decorrer.
- **Colunas `NOT NULL` com constante por omissão:** no Azure SQL só alteram metadados, sem reescrever a tabela.
- **Chaves estrangeiras:**
  - A única em tabela existente (`GameSessions.PlannedGameId`, 11) é numa coluna nova, toda nula. A validação é imediata.
  - As outras estão em tabelas novas; 6 usam `ON DELETE CASCADE`, todas dentro das tabelas novas da comunidade e das publicações.
- **Índices:** quase todos são em tabelas novas, vazias. Nas existentes há o de `GameSessions` (pequena) e o de `GameSearchCatalog` (ver acima).

## Compatibilidade com o código anterior (importante para recuperar)

O código de `develop` **não funciona** com o esquema novo: `PersonalRating` passa a `float`, e o modelo antigo lê `int`. Voltar à versão anterior do código depois de migrar exige **também** repor a base de dados (ver a sequência de publicação).
