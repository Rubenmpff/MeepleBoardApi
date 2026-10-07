# Estatísticas — Companhia, Explorar e retrospetiva (07/10/2026)

Implementadas nos serviços/controllers da API principal; DeviceTests fornece apenas configuração, SQL fictício e substitutos externos. Nenhuma migração nova nesta etapa. A base habitual não foi consultada nem alterada.

## Contratos

Todas as rotas exigem JWT e usam o utilizador autenticado, nunca um `userId` arbitrário:

- `GET /MeepleBoard/statistics/me/company`: período local `start`, `endExclusive`, `timeZone`, jogo por `gameId`, `mode`, e `friendId` opcional. Sem amigo devolve a lista de amigos aceites e contagens conjuntas; com amigo exige amizade aceite e agrega apenas partidas em que ambos participam e que o próprio pode consultar.
- `GET /MeepleBoard/statistics/me/matches`: suporta `friendId` com métricas de Companhia, bucket e paginação. Para recordes pessoais suporta `metric=scores` e `scoreValue`, incluindo zero e negativos. Mantém todos os filtros do agregado.
- `GET /MeepleBoard/statistics/me/explore`: jogos/recordes por identificador e modo, avaliações do diário próprio e entradas atuais da coleção adicionadas no período.
- `GET /MeepleBoard/statistics/me/year`: `year`, `timeZone`, `gameId`, `mode`; ano completo no fuso escolhido, usando as mesmas agregações. Não devolve nomes ou identificadores de amigos.

## Companhia e amostras

Uma partida por par e MatchId. As minhas vitórias e as do amigo incluem vitórias partilhadas; o indicador partilhadas identifica a interseção, não uma partida extra. Resultados com apenas um dos dois a vencer são distintos dos casos em que outro participante vence e nenhum dos dois vence. Também identificamos outro participante que partilha a vitória com um dos dois.

Empates: pelo menos um dos dois está entre os empatados em primeiro; empate entre ambos tem indicador próprio. Se ambos perderam e outros empataram em primeiro, é mostrado separadamente. Nunca eleger vencedores pelas pontuações. Cooperativo exige resultado explícito coerente da equipa e de ambos; apresenta vitória/derrota/empate da equipa, separado do competitivo.

Sem resultado explícito, modo incoerente ou resultados inconsistentes: amostra desconhecida, excluída de conhecidos. Antigas mantêm informação original, sem reclassificação por catálogo, vencedor antigo ou ausência de vencedor. Drilldown distingue resultado não definido explícito, indisponível e antigo por confirmar. Pontuações incompletas continuam nulas; mostra amostra de pares com os dois valores. Zero e negativos são valores, maior não significa melhor.

A lista de Companhia inclui apenas amigos atualmente aceites, incluindo zero para permitir selecionar um amigo sem atividade no período. Não significa todas as pessoas de toda a vida. Não consulta partidas separadas do amigo, diário, notas, avaliação pessoal, coleção ou email dele. Scores/Outcome limitam-se aos campos partilhados das partidas autorizadas. Remover amizade revoga os pedidos de comparação, sem apagar partidas.

## Jogos, avaliações e coleção

Agrupar jogos por GameId e separar modos. Mínimo/máximo pessoais são valores registados, não «melhor pontuação»: faltam direção, variante/unidade e condições comparáveis. A avaliação média usa exclusivamente JournalEntry próprio; null não avaliado, zero e meios pontos preservados, cobertura visível.

Coleção: entradas atuais com AddedAt no período, estado atual, PricePaid nullable e correspondência com o histórico completo próprio. Preço zero é conhecido. Não existe moeda guardada ou data de compra; apresentar valores individuais sem somar despesas ou inventar moeda. AddedAt não é data de aquisição. Remoções/alterações não têm histórico de eventos. «Sem partidas no teu histórico» não significa nunca jogado. Com modo selecionado, restringir aos jogos com esse modo no histórico próprio, pois a entrada de coleção não tem modo intrínseco.

## Retrospetiva

Mais jogados, mais vitórias e maior taxa são destaques distintos; os dois últimos separados por modo. Taxa = vitórias / resultados conhecidos, mínimo cinco conhecidos para o destaque por taxa. Lideranças empatadas mantidas, incluindo comparação de taxas exatas antes de arredondar apresentação. Sem conhecidos, sem taxa inventada; desconhecidos não são derrotas.

Duração: apenas valores existentes, cobertura visível; nenhuma duração = indisponível, duração zero explícita continua zero. Primeiros jogos consultam todo o histórico anterior: «pela primeira vez segundo o histórico registado». Ano vazio não cria destaques fictícios. Companhia no DTO anual contém só contagem e quantidade de líderes empatados.

## Verificação e reprodução

- `dotnet run --project tools/StatisticsChecks`: verificações determinísticas de períodos/DST, filtros, cobertura, resultados e categorias de pares, sem SQL.
- Node 22.14.0: `node tools/DeviceTestApi/verify-statistics.cjs` e `node tools/DeviceTestApi/verify-company.cjs`, depois de arrancar DeviceTests. Ambos recusam health diferente de DeviceTests/MeepleBoard_DeviceTests/externalDelivery=false.
- Verificados por gravação/releitura SQL/API: competitivo individual/partilhado, empate parcial, outro vencedor, partilha com terceiro, cooperativo vitória/derrota/empate/não definido, resultados antigos, zero/negativos, avaliação própria zero/meios pontos, duração ausente/zero, filtros e partidas de suporte.
- Verificados 401/403/400, amizade aceite, participação, tentativa de userId arbitrário, ausência de partidas privadas do amigo, notas/avaliações privadas, nomes/emails de amigos no DTO anual, biblioteca própria e preço zero.
- Fixtures/marcadores/credenciais ficam em `.device-tests`, ignorados; dados criados são exclusivamente fictícios. As datas adicionais de Companhia são 28/10/2024, preservando os oito casos do resumo de 27/10/2024.

Não substitui confirmação nativa no iPhone, nem encerra testes manuais de Solo/cooperativo/campanhas/autenticação. Configuração/migrações futuras no Windows ou cópia isolada: [runbook](integracao-copia-real.md).
