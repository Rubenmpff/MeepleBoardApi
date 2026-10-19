# Sequência de publicação no QA — integração 2026-10

**Estado: preparada, não executada.** Nada foi integrado, aplicado ou alterado no Azure. Cada passo precisa de autorização própria.

**Ordem:** API → app.

- A app depende de endpoints novos: bloqueios, denúncias, eliminação de conta, publicações e fotografias.
- Uma integração em `develop` (API) **publica automaticamente** no Azure QA (`meepleboard-api-qa`), pelo workflow `deploy-meepleboard-api-qa.yml` (push para `develop`).
- A API **não** aplica migrações ao arrancar.

## Antes de integrar

1. **Confirmar a migração aplicada no QA:** `SELECT TOP 5 [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC;`
   - Esperado: `20260823134606_IncreaseGameSearchCatalogNameLength`.
   - Se for outra, gerar o script de novo a partir dela (`docs/migracoes/2026-10-revisao.md`).
2. **Registar o ponto de retorno:**
   - o commit publicado no QA (hoje `origin/develop` = `3af0f8b`);
   - a hora UTC exata antes do passo 3, para um restauro para um momento anterior (point-in-time restore) do Azure SQL;
   - se o nível da base não garantir esse restauro, exportar antes um ficheiro `.bacpac`.
3. **Aplicar o script** `docs/migracoes/2026-10-integracao-idempotente.sql`:
   - numa janela calma;
   - sem importação do BGG a decorrer;
   - é uma só transação.
4. **Verificar:** a última migração passa a ser `20261018163245_AddPublicationPhotos` e existem 16 linhas novas no histórico.
5. **Definições da aplicação no QA:** criar `Bgg__Token` (e, se usado, `Bgg__CatalogPageUrl`) com o valor atual de `BoardGameGeek__ApplicationToken`. A antiga só se retira depois de a nova versão estar verificada.

## Integrar e verificar

6. **Integrar o pedido da API em `develop`** (squash). O workflow publica no QA. Confirmar no GitHub Actions que terminou sem erros.
7. **Depois de publicar:**
   - **Pesquisa:** executar o `BggSearchTokenRebuildJob`, que reconstrói o índice de pesquisa; até lá, a pesquisa fica incompleta.
   - **Contadores:** `POST /MeepleBoard/game/rankings/recompute` com uma conta de administração, para os contadores MeepleBoard.
8. **Verificações mínimas no QA:**
   - saúde e entrada;
   - pesquisa de um jogo;
   - registar uma partida e avaliá-la;
   - nota privada;
   - publicar e retirar uma fotografia;
   - bloquear e denunciar;
   - eliminar uma conta de teste.

   Usar só contas de teste do QA.
9. **App:**
   - integrar o pedido da app em `main` (squash);
   - fazer um build com `EXPO_PUBLIC_API_MODE=qa`;
   - repetir o essencial do passo 8 no aparelho.

## Recuperação em caso de falha

- **O script falha (passo 3):** a transação é revertida por inteiro.
  - Confirmar no histórico que nenhuma das 16 entrou.
  - O QA continua com o código anterior, compatível. Corrigir e repetir.
- **A publicação falha ou a nova versão não funciona (passos 6–8):**
  - Voltar a correr o workflow para o commit anterior não chega, porque o código antigo não lê `PersonalRating` como `float`.
  - **Recuperação completa:**
    1. restauro para um momento anterior da base QA, até antes do passo 3;
    2. publicar outra vez `3af0f8b`, com `workflow_dispatch` nesse commit;
    3. repor `BoardGameGeek__ApplicationToken`, se tiver sido removida.
  - Os dados escritos no QA entre os passos 3 e 8 perdem-se. Manter essa janela curta.
- **Só a pesquisa ou os contadores parecem errados:** não é preciso recuar. Repetir o passo 7.

## Fora desta sequência

- **Produção:** não há fluxo automático a partir de `main`. Fica para uma etapa própria, com o mesmo método (confirmar a migração aplicada, cópia de segurança, script e verificações).
- **Verificação dos metadados das fotografias em produção:** pendente.
- **Ficheiros órfãos em quarentena no DeviceTests:** decisão pendente.
