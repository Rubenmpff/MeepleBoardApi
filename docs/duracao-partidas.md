# Duração das partidas e médias por jogo

Proposta aprovada a 16/10/2026 (`MeepleBoardApp/docs/mockups/app/registo/proposta.html`). Só DeviceTests.

## Dados

- `Matches.DurationInMinutes` (existente) e `Matches.DurationMethod` (novo, opcional): `Measured`, `Estimated`, `Unknown` («Não sei»). Vazio = partida anterior à pergunta: com minutos positivos é «método não indicado»; com 0 fica guardado mas nunca conta.
- `SetDuration`: medida ou estimativa exige minutos ≥ 1; «Não sei» não tem minutos. Clientes antigos que só enviam minutos continuam a funcionar; mudar os minutos sem método deixa o método por indicar.
- `AspNetUsers.ContributesDurationStats` (novo, desligado por omissão), alterado só pela própria conta: `PATCH /MeepleBoard/users/me/duration-sharing { enabled }`; lido em `GET /MeepleBoard/users/me`.
- Migração `20261016083156_AddDurationMethodAndSharing`: duas colunas, auditadas no anfitrião DeviceTests.

## Rotas

- `PATCH /MeepleBoard/matches/{id}/duration { durationInMinutes, durationMethod }`: corrige a duração; só o criador (mesma regra de editar a partida).
- `GET /MeepleBoard/game/{gameId}/durations`: `personal` (as partidas em que participaste) e `community` (partidas em que todos os participantes contribuem), cada um com `solo`, `multiplayer` e `byPlayers` (2, 3, 4, 5+), e `viewerContributes`.

## Regras (`GameDurationCalculator`)

- Cada partida conta uma vez. Modo: o explícito; partidas anteriores usam o indicador solo guardado.
- Válidas: minutos > 0 e método diferente de «Não sei». «Não sei», sem duração e 0 antigos ficam de fora e são contados à parte só na média pessoal.
- Comunidade, por grupo apresentado: pelo menos 5 partidas, de pelo menos 3 pessoas que as registaram, nenhuma com mais de metade (sem criador conhecido = uma pessoa). Abaixo disso só `available: false`, sem números. Proteção inicial, não garantia de anonimato. Se só um grupo por número de jogadores ficasse escondido com o total visível, esconde-se também o mais pequeno dos visíveis.
- Indicadores: média, contagens por método, mediana, Q1–Q3 (interpolação linear) e partidas muito afastadas (fora de Q1 − 1,5 × IQR e Q3 + 1,5 × IQR), estas três só com 5 ou mais valores. Na comunidade tudo arredondado a 5 min e das afastadas só a contagem; na pessoal os valores exatos e quais são as afastadas.
- Sem cache: correções, partidas apagadas, participantes e preferências contam na leitura seguinte. Nunca se devolvem identificadores, nomes, datas nem durações individuais da comunidade. As regras de acesso às partidas não mudam.

## Verificação

- `dotnet run --project tests/GameDurations.Tests --no-launch-profile` (9 verificações) e as suites existentes (Authorization 60, PlayerScores 72, Sessions 16, GameResources 11, Library 16).
- `node tools/DeviceTestApi/verify-durations.cjs` contra SQL real: guardar e reabrir, corrigir (só o criador), «Não sei», 0 antigo, modo solo, comunidade com consentimento de todos, proteção mínima, mesmo valor para todos, sem identificadores, retirada e reposição, correção e eliminação. Deixa seis partidas de Wingspan de três amigas que contribuem, para o iPhone.
