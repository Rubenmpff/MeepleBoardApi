# Próxima decisão — resultados e estatísticas

Proposta apenas; **não implementar resultados/migrações/estatísticas nesta etapa**. Ver [modos, resultados, histórico e denominadores](../../MeepleBoardApp/docs/proposta-modos-resultados.md) e [etapas de produto](../../MeepleBoardApp/docs/proximas-etapas-produto.md). Incluem Autenticação (duas propostas visuais com mascote/logótipo), Estatísticas, «O teu ano à mesa» e auditoria dos ecrãs. Desenvolvimento permanece exclusivamente em DeviceTests com dados fictícios; base habitual intacta.

## Causas técnicas confirmadas

- CreateMatchDto/Match não persistem o `gameMode` enviado pelo frontend, nem resultado de equipa/empate; apenas IsSoloGame e WinnerId. O mapper do frontend omite WinnerId no Solo e escolhe o primeiro IsWinner no cooperativo. A API não Solo exige vencedor individual. Não há contrato de resultado definido versus desconhecido.
- UserService.GetTotalWinsAsync usa WinnerId; MatchPlayerRepository usa IsWinner. As taxas usam todas as partidas, incluindo resultados desconhecidos.
- FriendshipRepository usa Game.IsCooperative e marcas de vitória dos dois amigos para escolher teamLoss/draw quando eles não ganham. Falta de resultado e vitória de terceiro podem ser classificadas incorretamente. Capacidade do catálogo não identifica modo efetivamente jogado.
- O histórico partilhado assume máximo como melhor score. Falta sentido de pontuação por jogo/modo/variante.
- MatchDate/GameId/participação permitem agregação de atividade autorizada; DurationInMinutes é nullable e precisa de cobertura. Diário pessoal tem avaliações nullable em meios pontos, zero válido; coleção tem AddedAt/PricePaid/status, mas não data comprovada de compra/moeda/transações históricas. Listas paginadas e contadores desnormalizados não devem ser tratados como fonte completa sem validação.

Propor campos nullable novos e resultados explícitos por pessoa/equipa, manter o legado original sem backfill por inferência. Novo contrato não deve ser ativado antes de adaptar todos os leitores e agregadores. Taxa de vitória = vitórias/(vitórias+derrotas+empates) confirmados por pessoa; desconhecidos ficam separados, com n/cobertura visíveis. Proposta de empate, vitória Solo e equipa está no documento frontend; precisa de aprovação antes de código.

## Etapa atual — zero

Avaliação 0 confirmada novamente com HTTP/JWT na API marcada `DeviceTests`, base `MeepleBoard_DeviceTests`, externalDelivery false. Criada uma única partida fictícia competitiva com dois participantes, vencedor manual, scores -17 e 0 e avaliação 0. Duas leituras independentes do diário conservaram 0; só o autor teve avaliação, não o outro participante. Nenhum segredo foi apresentado ou versionado. Sem alteração funcional backend nem nova migração; a correção de apresentação de zero/estrelas ocorreu no frontend.
