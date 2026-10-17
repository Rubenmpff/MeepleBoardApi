# Companhia — Só vocês / Em grupo

Contrato integrado no serviço e controlador principais; DeviceTests fornece apenas configuração e dados fictícios. Não há migração: usa os MatchPlayers existentes.

`GET /MeepleBoard/statistics/me/company` aceita `groupContext=all|only-two|group` (predefinição all). O suporte `/matches` aceita o mesmo parâmetro com friendId. Valores inválidos são rejeitados; um contexto específico no suporte exige amigo selecionado.

As duas pessoas têm de participar na partida e ser amigas aceites. A partida tem de estar no universo autorizado do utilizador atual. A contagem SQL é `MatchPlayers.Select(UserId).Distinct().Count()`; dois participantes registados = only-two, três ou mais = group. Não conta convidados da sessão, vencedores ou metadados do catálogo. Não recupera participantes históricos ausentes; a UI explica que usa os participantes registados.

A resposta inclui GroupContext e ContextCounts (All/OnlyTwo/Group/Unclassified). A distribuição corresponde ao mesmo período, GameId e modo, antes de restringir o contexto. Matches/Results/Games/Comparisons/Evolution correspondem ao contexto escolhido. O ranking de amigos respeita também o contexto. Contagem indisponível nunca é deduzida (linhas sem contexto permanecem em all).

Resultados antigos/indefinidos continuam desconhecidos e fora das contagens conhecidas. Notas e avaliações do amigo não são consultadas nem devolvidas. Partilha anual permanece anónima e não incorpora IDs/pessoas.

## Verificação

- Compilação DeviceTests com SDK existente; aviso AutoMapper 14.0.0 conservado para análise separada, sem atualização automática.
- StatisticsChecks: partição e contagem, contexto inválido, desconhecidos preservados.
- `tools/DeviceTestApi/verify-company-context.cjs`: leitura da base exclusiva MeepleBoard_DeviceTests, confronto com participantes dos detalhes, resultados/suportes/evolução/jogos, filtros e autorização 401/403/400. Credenciais locais em memória, sem impressão ou versionamento. Nenhum dado criado por este novo verificador.

No Windows, compilar e arrancar a API habitual com a configuração apropriada basta para disponibilizar estes endpoints; não executar os scripts de dados fictícios na base habitual. Nenhuma base habitual foi alterada nesta etapa.
