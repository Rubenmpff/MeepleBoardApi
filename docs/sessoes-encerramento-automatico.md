# Encerramento automático das sessões

## Regras

- **Início real** (`StartedAt`, calculado): a hora marcada; se a primeira aceitação ainda válida de um convidado chegou depois, essa hora. Mantém-se a ativação automática à hora marcada com pelo menos um convidado aceite (decisão do utilizador, 2026-10-12); não existe ação «Iniciar».
- **Fim automático** (`AutoCloseAt`): início real + 24 h, ou a hora escolhida pelo organizador (`ClosesAt`).
- **Escolher o fim:** só o organizador, só com a sessão a decorrer, entre agora (+1 min) e início real + 48 h (`PUT /MeepleBoard/session/{id}/close-time`). Terminar já continua a ser `POST /session/{id}/close`.
- **Independente do prazo de resposta** (`ResponseDeadline`/`EffectiveDeadline`), que continua a servir só o cancelamento por falta de confirmações (`SessionCleanupJob`).
- **Ao terminar:** `EndDate` = hora prevista de fim (não a hora em que o job correu), `ClosedAutomatically = true`, estado `Closed` («Terminada» na app). Participantes, partidas, resultados e diários ficam como estavam. Entre a hora de fim e a execução do job, o estado calculado já é `Closed`, por isso a sessão deixa de aceitar partidas na hora certa.

## Partida em curso

Verificado no código: não existe estado persistido de partida em curso. As partidas são criadas já concluídas (data, jogadores e resultado); `JournalStatus` refere-se ao diário/avaliações. Por isso o encerramento não espera por nenhuma partida, e um formulário de registo aberto na app não é tratado como partida em curso.

## Execução

- `SessionAutoCloseJob` (Hangfire, `*/5 * * * *`, `session-auto-close`). O Hangfire guarda o agendamento em SQL; o estado das sessões também está em SQL, por isso tudo continua certo depois de reinícios.
- **Idempotente:** só considera sessões sem `EndDate` e não canceladas; uma segunda execução não altera nada (`DisableConcurrentExecution` evita execuções em paralelo).
- **Sessões antigas que ficaram ativas:** a mesma regra. Não há data real de início guardada; usa-se a hora marcada, ou a data de resposta da primeira aceitação quando é posterior. Aceitações antigas sem data contam como anteriores à hora marcada. Fecham com `EndDate` = esse início + 24 h.
- **DeviceTests:** sem servidor Hangfire; o anfitrião corre o mesmo job a cada minuto (`DeviceSessionAutoClose`). Na primeira execução, 9 sessões antigas do DeviceTests terminaram automaticamente, todas com as partidas preservadas.

## Esquema

Migração `20261012080128_AddSessionCloseTime`: `GameSessions.ClosesAt` (`datetime2 NULL`) e `GameSessions.ClosedAutomatically` (`bit NOT NULL DEFAULT 0`). Só adiciona colunas; auditada pelo anfitrião DeviceTests antes de aplicar.

## Verificação

```sh
dotnet run --project tests/Sessions.Tests --no-launch-profile            # 13 testes isolados
node tools/DeviceTestApi/verify-session-auto-close.cjs                   # SQL real, API DeviceTests ativa
# reiniciar a API DeviceTests e depois:
node tools/DeviceTestApi/verify-session-auto-close.cjs --recheck
```

O verificador deixa uma sessão fictícia a decorrer («Sessão de teste · fim automático»), útil para o roteiro no iPhone.

## Pendência observada

**S-CANCEL01:** `GameSessionRepository.GetByIdForUpdateAsync` não carrega `Players`, por isso a verificação `Status == "Active"` em `CancelSessionAsync` nunca vê convidados aceites e não impede cancelar (apagar) uma sessão a decorrer pelo API. A app só mostra «Cancelar» em sessões agendadas. Não alterado nesta etapa; ver também a decisão pendente sobre cancelar versus eliminar.
