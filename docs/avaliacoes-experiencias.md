# Avaliações por experiência, médias, ranking e publicação de experiências

Proposta aprovada a 16/10/2026 (`MeepleBoardApp/docs/mockups/app/diario/proposta.html`). Só DeviceTests.

## Avaliações

- Uma avaliação por participante e partida (índice único existente), 0 a 10 em meios pontos; zero é avaliação. A avaliação do registo é a do criador. Depois de guardada fica fechada: `MatchJournalEntry.Update` recusa outro valor (409 em `PUT …/journal`); notas e etiquetas continuam a guardar-se; enviar sem avaliação mantém a guardada.
- Dados existentes: nada foi apagado nem alterado; as avaliações atuais ficam fechadas com o valor que têm. Não há histórico de alterações anteriores. Avaliações antigas só em `Match.PersonalRating` (sem entradas) continuam a contar como antes.

## Médias e ranking

- «A tua média» (`GET /matches/rating/game/{id}`): todas as tuas avaliações desse jogo, incluindo partidas não oficiais, enquanto fores jogador da partida.
- Avaliação MeepleBoard: média de todas as avaliações elegíveis — partidas oficiais (`IsOfficialMode`), uma por experiência, de quem ainda é jogador da partida — mais as avaliações antigas sem entradas. Arredondamento igual ao anterior. Guardada no jogo com `MeepleBoardRatingsCount` e `MeepleBoardRaters` (autores desconhecidos contam como um).
- Uma só rotina (`GameRepository.RecomputeMeepleBoardRatingAsync`), chamada ao registar, ao avaliar no diário, ao apagar uma partida (antes não recalculava), ao mudar a partida de jogo, ao acrescentar ou retirar um jogador, ao apagar uma conta e no recálculo geral (`POST /game/rankings/recompute`, administradores). Só lê avaliações e escreve a média do jogo.
- Ranking MeepleBoard: só jogos com pelo menos 5 avaliações de 3 jogadores distintos; a mesma média dá a mesma posição (`MeepleBoardRank` = 1 + jogos com média superior); número de avaliações e nome só ordenam o empate.
- Recálculo no DeviceTests: 4 jogos atualizados no primeiro recálculo (médias desatualizadas por partidas apagadas); nenhuma partida não oficial avaliada.

## Publicação de experiências

- `MatchExperiencePublications` (1:1 com a entrada do diário, apagada com ela): mostrar a avaliação com o nome e/ou um comentário (500), cada um explícito e retirado à parte. Nunca cria nem muda avaliações nem médias. Exige a avaliação dada.
- Rotas do autor: `GET/PUT /campaigns/matches/{id}/journal/publication`, `POST …/publication/withdraw-rating|withdraw-comment|delete-comment`. Limite: 20 publicações novas em 24 h.
- Público: `GET /game/{id}/experiences` devolve só nome, avaliação publicada, comentário publicado, modo, n.º de jogadores, mês, «editado», «é teu», «pode reportar». Nunca a partida, outros participantes, local, notas ou o id do autor. Não mostra comentários ocultos nem os que quem lê reportou.
- Reportar: `POST /experiences/{id}/reports` (Abusive, Spam, PersonalData, OffTopic, Other; detalhe até 300); não o próprio; um aberto por pessoa; 3 pessoas diferentes ocultam até revisão. Moderação (administradores): `GET /admin/experiences/reported`, `POST /admin/experiences/{id}/keep|hide|restore`. O autor vê «oculto» e o motivo.
- Limitação conhecida (igual aos relatórios de recursos): quem reporta fica ligado ao relatório com remoção restrita, por isso apagar essa conta exige primeiro remover os seus relatórios.
- Migração `20261016183656_AddRatingCountsAndExperiencePublications`: duas colunas em `Games` e duas tabelas; auditada no anfitrião DeviceTests.

## Verificação

- `tests/Experiences.Tests` (4), as suites existentes e `node tools/DeviceTestApi/verify-ratings-experiences.cjs` contra SQL real: recálculo só por administradores, zero como avaliação, avaliação fechada (409), permissões (403), contagens por experiência, não oficiais fora da média MeepleBoard e dentro da tua, apagar recalcula, publicar e retirar sem mexer na avaliação, sem dados privados nas respostas, relatórios e moderação, ranking com mínimo e posições partilhadas, limpeza.
