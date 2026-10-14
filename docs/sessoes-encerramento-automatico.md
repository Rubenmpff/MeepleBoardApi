# Encerramento automático das sessões

## Regras

- **Início:** sempre a hora marcada (`ScheduledStartDate`). Aceitar um convite não muda o início, mesmo quando chega depois dessa hora (decisão do utilizador, 2026-10-14: aceitar não significa que já se está a jogar). Não existe ação «Iniciar».
- **Fim automático** (`AutoCloseAt`, sempre definido): hora marcada + 24 h, ou a hora escolhida pelo organizador (`ClosesAt`). Início e fim são conhecidos desde a criação e aparecem a todos, com ou sem convidados confirmados.
- **Quando decorre:** fica `Active` entre a hora marcada e o fim, desde que haja pelo menos um convidado aceite. Uma aceitação tardia ativa a sessão até ao mesmo fim; não a prolonga. Sem nenhum convidado aceite, a sessão nunca decorre nem termina sozinha: chegado o prazo de resposta, o `SessionCleanupJob` cancela-a (regra existente).
- **Escolher o fim:** só o organizador, só com a sessão a decorrer, entre agora (+1 min) e hora marcada + 48 h (`PUT /MeepleBoard/session/{id}/close-time`). Terminar já continua a ser `POST /session/{id}/close`.
- **Independente do prazo de resposta** (`ResponseDeadline`/`EffectiveDeadline`), que continua a servir só o cancelamento por falta de confirmações (`SessionCleanupJob`).
- **Ao terminar:** `EndDate` = hora prevista de fim (não a hora em que o job correu), `ClosedAutomatically = true`, estado `Closed` («Terminada» na app). Participantes, partidas, resultados e diários ficam como estavam. Entre a hora de fim e a execução do job, o estado calculado já é `Closed`, por isso a sessão deixa de aceitar partidas na hora certa.

## Partida em curso

Verificado no código: não existe estado persistido de partida em curso. As partidas são criadas já concluídas (data, jogadores e resultado); `JournalStatus` refere-se ao diário/avaliações. Por isso o encerramento não espera por nenhuma partida, e um formulário de registo aberto na app não é tratado como partida em curso.

## Execução

- `SessionAutoCloseJob` (Hangfire, `*/5 * * * *`, `session-auto-close`). O Hangfire guarda o agendamento em SQL; o estado das sessões também está em SQL, por isso tudo continua certo depois de reinícios.
- **Idempotente:** só considera sessões sem `EndDate` e não canceladas; uma segunda execução não altera nada (`DisableConcurrentExecution` evita execuções em paralelo).
- **Sessões antigas que ficaram ativas:** a mesma regra: `EndDate` = hora marcada + 24 h. A primeira versão (2026-10-12) usava a primeira aceitação quando chegava depois da hora marcada; nas 9 sessões do DeviceTests a diferença era inferior a 1 ms e foram alinhadas com a regra atual.
- **DeviceTests:** sem servidor Hangfire; o anfitrião corre o mesmo job a cada minuto (`DeviceSessionAutoClose`). Na primeira execução, 9 sessões antigas do DeviceTests terminaram automaticamente, todas com as partidas preservadas.

## Jogo previsto

Opcional (`GameSessions.PlannedGameId`, chave para `Games` com `ON DELETE SET NULL`): indica a intenção do organizador e não limita os jogos registados na sessão, que pode ter partidas de vários jogos.

- Criação: `plannedGameId` no `POST /MeepleBoard/session` (jogo inexistente: 400). Sem ele, a sessão é criada como antes.
- `PUT /MeepleBoard/session/{id}/planned-game` `{ gameId }` (null retira): só o organizador (403), só com a sessão agendada ou a decorrer (409), sessão inexistente 404, sem sessão iniciada 401. Muda apenas este campo: partidas, prazo de resposta, início e fim ficam iguais.
- O DTO devolve `plannedGameId`, `plannedGameName` e `plannedGameImageUrl` na lista e no detalhe. Sessões existentes ficam sem jogo.

## Esquema

Migração `20261012080128_AddSessionCloseTime`: `GameSessions.ClosesAt` (`datetime2 NULL`) e `GameSessions.ClosedAutomatically` (`bit NOT NULL DEFAULT 0`). Só adiciona colunas; auditada pelo anfitrião DeviceTests antes de aplicar.
Migração `20261014164836_AddGameSheetAndSessionPlannedGame`: `GameSessions.PlannedGameId` (`uniqueidentifier NULL`, índice e chave para `Games` com `ON DELETE SET NULL`) e a ficha BGG em `Games`; também auditada.

## Verificação

```sh
dotnet run --project tests/Sessions.Tests --no-launch-profile            # 16 testes isolados (inclui o jogo previsto)
node tools/DeviceTestApi/verify-session-auto-close.cjs                   # SQL real, API DeviceTests ativa
# reiniciar a API DeviceTests e depois:
node tools/DeviceTestApi/verify-session-auto-close.cjs --recheck
```

O verificador deixa uma sessão fictícia a decorrer («Sessão de teste · fim automático»), útil para o roteiro no iPhone.

## Pendência observada

**S-CANCEL01:** `GameSessionRepository.GetByIdForUpdateAsync` não carrega `Players`, por isso a verificação `Status == "Active"` em `CancelSessionAsync` nunca vê convidados aceites e não impede cancelar (apagar) uma sessão a decorrer pelo API. A app só mostra «Cancelar» em sessões agendadas. Não alterado nesta etapa; ver também a decisão pendente sobre cancelar versus eliminar.
