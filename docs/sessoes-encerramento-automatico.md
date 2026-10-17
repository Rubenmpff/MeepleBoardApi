# Encerramento automático das sessões

## Regras

- **Início:** sempre a hora marcada (`ScheduledStartDate`). Aceitar um convite não muda o início, mesmo quando chega depois dessa hora (decisão do utilizador, 2026-10-14: aceitar não significa que já se está a jogar). Não existe ação «Iniciar».
- **Fim automático** (`AutoCloseAt`, sempre definido): hora marcada + 24 h, ou a hora escolhida pelo organizador (`ClosesAt`). Início e fim são conhecidos desde a criação e aparecem a todos, com ou sem convidados confirmados.
- **Quando decorre:** fica `Active` entre a hora marcada e o fim, desde que haja pelo menos um convidado aceite. Uma aceitação tardia ativa a sessão até ao mesmo fim; não a prolonga. Sem nenhum convidado aceite, a sessão nunca decorre nem termina sozinha: chegado o prazo de resposta, o `SessionCleanupJob` cancela-a (regra existente).
- **Escolher o fim:** só o organizador, só com a sessão a decorrer, entre agora (+1 min) e hora marcada + 48 h (`PUT /MeepleBoard/session/{id}/close-time`). Terminar já continua a ser `POST /session/{id}/close`.
- **Independente do prazo de resposta** (`ResponseDeadline`/`EffectiveDeadline`), que continua a servir só o cancelamento por falta de confirmações (`SessionCleanupJob`).
- **Ao terminar:** `EndDate` = hora prevista de fim (não a hora em que o job correu), `ClosedAutomatically = true`, estado `Closed` («Terminada» na app). Participantes, partidas, resultados e diários ficam como estavam. Entre a hora de fim e a execução do job, o estado calculado já é `Closed`, por isso a sessão deixa de aceitar partidas na hora certa.

## Terminar sessão (manual, 2026-10-17)

`POST /MeepleBoard/session/{id}/close`, na app em «Gerir sessão» › «Terminar sessão», com confirmação.

| Caso | Resposta | O que fica |
| --- | --- | --- |
| Organizador, sessão a decorrer | 200 | `EndDate` = hora efetiva, `ClosedAutomatically = false`. Participantes e partidas ficam iguais. |
| Organizador, depois do fim automático (ainda não gravado) | 200 | `EndDate` = fim automático, `ClosedAutomatically = true` |
| Repetir numa sessão já terminada (à mão ou automaticamente) | 200 | Nada muda: nem a hora nem o tipo de fim |
| Sessão agendada, incluindo uma que passou da hora mas não tem convidados aceites | 409, «… usa Cancelar sessão» | Nada muda |
| Sessão cancelada | 409 | Nada muda |
| Convidado ou outra pessoa | 403 | Nada muda |
| Sem sessão iniciada / sessão inexistente | 401 / 404 | — |

**Terminar e cancelar são ações diferentes:**

- **Terminar:** aplica-se a uma sessão a decorrer; fica guardada, com as partidas.
- **Cancelar** (`POST /session/{id}/cancel`): aplica-se a uma sessão agendada e apaga-a. Responde 204; 403 a quem não organiza; 404 se a sessão não existir; 409 numa sessão a decorrer ou já terminada. Antes, o cancelamento devolvia 500 nos casos recusados.

**Concorrência entre o fecho manual e o automático.** O fecho manual e o `SessionAutoCloseJob` gravam através de `IGameSessionRepository.SaveCloseAsync`. É um único `UPDATE … WHERE EndDate IS NULL AND IsCancelled = 0`, que escreve `EndDate` e `ClosedAutomatically` juntos.

- O primeiro fecho fica; os seguintes não mudam nada.
- Quem perde volta a ler o que ficou guardado: o fecho manual responde 200, o job não o conta.
- Antes, as duas gravações podiam misturar-se: a hora de um fecho com o indicador do outro.

**Sessão carregada com os jogadores.** Terminar e cancelar passam a ler a sessão com os jogadores (`GetByIdWithPlayersForUpdateAsync`), porque o estado depende de quem aceitou. Sem eles:

- terminar à mão depois do fim automático guardava a hora do pedido;
- era possível cancelar (apagar) uma sessão a decorrer (S-CANCEL01).

**Registar depois de terminar:** recusado com 409, «Esta sessão já foi encerrada e não aceita novas partidas.» A app esconde o botão «Registar partida nesta sessão». Se o formulário for aberto para uma sessão terminada, mostra «Terminou {data}» e «Esta sessão terminou e já não aceita partidas. Podes registar a partida fora da sessão, em Novo.» Associar partidas esquecidas a uma sessão terminada fica como decisão futura (SESS-ESQ01, nas pendências da app).

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
dotnet run --project tests/Sessions.Tests --no-launch-profile            # 21 testes isolados (jogo previsto, terminar e cancelar)
node tools/DeviceTestApi/verify-session-close.cjs                       # SQL real: terminar, cancelar e corrida com o fecho automático
node tools/DeviceTestApi/verify-session-auto-close.cjs                   # SQL real, API DeviceTests ativa
# reiniciar a API DeviceTests e depois:
node tools/DeviceTestApi/verify-session-auto-close.cjs --recheck
```

O verificador deixa uma sessão fictícia a decorrer («Sessão de teste · fim automático»), útil para o roteiro no iPhone.

**`verify-session-close.cjs`** usa duas contas próprias, «sessao-organiza» e «sessao-convidada» (`.device-tests/session-close-accounts.json`). Não estão em `accounts.json` nem são amigas de ninguém. O anfitrião cria-as no arranque.

- **Sessões:** são criadas por `SessionCloseFixture`, através de `/device-test/session-close/…`, que só responde a pedidos do próprio Mac: uma a decorrer, uma agendada, uma depois do fim automático e cinco corridas.
- **O que verifica:**
  - autorização e estados;
  - fim efetivo, partidas e participantes preservados;
  - repetição sem efeito;
  - partida recusada depois de terminar;
  - cinco corridas de 6 fechos manuais com 6 automáticos (o mesmo código do job, com a hora do fim automático): um único fim coerente, e fechos posteriores sem efeito.
- **No fim:** apaga a partida e as sessões descartáveis, e confirma que a média do jogo voltou ao valor inicial.
- **O que não toca:** as contas e as sessões do roteiro do iPhone.

## Pendência observada

**S-CANCEL01 (resolvida a 2026-10-17):** `GameSessionRepository.GetByIdForUpdateAsync` não carrega `Players`, por isso a verificação `Status == "Active"` em `CancelSessionAsync` nunca vê convidados aceites e não impede cancelar (apagar) uma sessão a decorrer pelo API. A app só mostra «Cancelar» em sessões agendadas. Cancelar e terminar usam agora `GetByIdWithPlayersForUpdateAsync`; cancelar uma sessão a decorrer dá 409. Continua pendente a decisão sobre cancelar versus eliminar.
