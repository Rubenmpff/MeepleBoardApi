# Participantes e avaliação própria — 6 de outubro de 2026

Na criação, partidas não Solo exigem pelo menos dois IDs distintos depois da normalização. O registo rápido continua a incluir o utilizador autenticado; repetir o mesmo ID não conta como outra pessoa. `GameSessionId` com `IsSoloGame=true` é recusado. A API não muda o modo automaticamente. Todas as validações anteriores de participantes aceites, identidade JWT e pontuação mantêm-se.

`CreateMatchDto.PersonalRating` é obrigatório: **0–10 em passos de 0,5**, sem arredondamento. Null/ausência, fora da escala, não finitos e frações como 7,25 são recusados antes de consultar/importar jogos ou escrever. Zero é válido por `HasValue`, não por truthiness. Só existe uma nova entrada de diário, associada ao utilizador JWT, nunca aos restantes participantes. A edição própria do diário aceita meios pontos; null mantém o significado legado de entrada ainda não avaliada (por exemplo, só com fotografias/notas). Privacidade e permissões não foram alteradas.

## Migração

`20261006140000_PreserveJournalHalfRatings` altera exclusivamente `MatchJournalEntries.PersonalRating`: int nullable → float nullable. Inteiros existentes e null mantêm-se; nenhum resultado/avaliação é reconstruído. A fonte antiga já arredondada não permite recuperar meios pontos históricos. O rollback é recusado para impedir truncagem; exige revisão específica. Não houve atualizações de dependências.

O anfitrião de testes audita exatamente a operação e o SQL. EF remove apenas uma eventual constraint de default da coluna identificada antes de alterar o tipo; essa linha específica é a única exceção à proibição de DROP. Continuam bloqueados DROP de tabelas/colunas, DELETE e UPDATE de Matches. Validam-se configuração exclusiva, marca de propriedade e correspondência de modelo antes de migrar.

Aplicada **apenas a MeepleBoard_DeviceTests**, contentor `meepleboard-device-tests-sql`, porta 14339. Base/contentor habitual intactos. Proteções e fallback LocalDB Windows conservados; não executado Windows nesta sessão.

## Evidência e limites

- 47 testes de regras/contrato em memória; 59 HTTP de autorização e auditoria offline de modelo/migrações.
- 32 cenários HTTP/JWT/SQL reais: um jogador/IDs repetidos recusados no registo rápido e na sessão, Solo em sessão recusado, avaliação ausente/7,25 recusada sem escrita; diário 0/0,5/7,5/10 criado e relido sem arredondamento, sem avaliar os pares. Contribuição independente 6,5 do segundo jogador preserva a avaliação original e as notas privadas.
- Mais 5 cenários reais de catálogo/fotografias: upload/leitura protegida, bloqueio de visitantes/estranhos e remoção pelo autor. Esquema confirmado com 24 migrações e coluna float nullable.
- Frontend Node 22.14.0, TypeScript e 219 testes; revisão visual no iPhone ainda pendente.
- RESULT01 continua pendente. O contrato ainda só distingue `IsSoloGame`, sem modo cooperativo/empate explícito; a contagem mínima aplica-se a pedidos não Solo. Não mudaram interpretação de resultados, escolha manual do vencedor nem leitura de registos antigos.

Ver [formulário e percurso no iPhone](../../MeepleBoardApp/docs/formulario-registo-partidas.md). API 5099 e Expo 8082 continuam exclusivos de testes; reiniciar a API exige novo login.
