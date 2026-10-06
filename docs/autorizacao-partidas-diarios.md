# Autorização de partidas e diários

## Regras

- Leituras de partidas autenticadas e filtradas por participação: listagem paginada, detalhe, histórico, última partida e pendências.
- Resumo, avaliações, tags e fotografias partilhados pelos participantes, independentemente de já terem avaliado ou de o diário estar fechado. Notas do diário devolvidas apenas ao autor; entradas alheias não incluem `notes`.
- Escrita/limpeza dos campos e remoção de fotografias usam o autor autenticado, nunca o `userId` enviado no corpo.
- Atualização/eliminação da partida e fecho manual do diário apenas pelo criador. O fecho automático mantém-se.
- Detalhes de sessões exigem pertença; partidas incluídas apenas quando o leitor participou. Convites continuam a permitir consultar os dados da sessão. A listagem geral passa a devolver apenas as sessões do leitor.
- Campanhas mantêm pertença e notas gerais. `canReadJournal` indica participação no encontro; pertencer à campanha não permite consultar/escrever o diário de uma partida alheia. Associar uma partida exige também participação. Remover a associação mantém a regra de pertença e não elimina a partida.
- Amigos mantém DTOs sem notas, amizade aceite e participação de ambos. Rankings e médias agregadas conservam as regras existentes.

## Dados antigos e esquema

`Matches.Notes` permanece guardado, mas é ignorado no mapeamento/serialização. Novas partidas não criam essa cópia. O histórico usa `personalNotes` da entrada do próprio leitor, sem fallback para notas antigas ou inferência de autoria.

A migração preparada `20261006120000_AddMatchCreator` acrescenta apenas `Matches.CreatorId` nullable. Novas partidas registam o utilizador autenticado; antigas ficam `NULL`, sem backfill. Sem autoria comprovada, atualização/eliminação/fecho manual ficam bloqueados. As entradas individuais com autor conhecido continuam editáveis pelo próprio participante. O rollback automático é bloqueado para preservar autoria entretanto registada.

**Nenhuma migração foi executada.** Antes de usar o backend atualizado, preparar o esquema numa base descartável; não arrancar contra a base atual para experimentar.

Diferença pré-existente: o índice `IX_GameSearchCatalog_RatingsCount_BggRank_AverageRating_Name_BggId`, definido no contexto, não consta do snapshot. A verificação EF continua a detetá-lo. Não foi escondido nem incluído nesta migração; rever historial/esquema do catálogo separadamente antes de aplicar migrações via EF9. A geração de SQL confirmou apenas a adição de `CreatorId`, sem atualização/eliminação de dados.

## Fotografias

Novos uploads usam recursos `authenticated` do Cloudinary; resultados públicos são rejeitados antes de guardar a entrada. O diário devolve referências relativas a:

`GET /MeepleBoard/campaigns/matches/{matchId}/journal/photos/{entryId}/{photoKey}`

Esse endpoint autentica, verifica participação e confirma a associação da fotografia à entrada/partida. Obtém a imagem por um URL assinado utilizado apenas no servidor e devolve bytes com `Cache-Control: no-store`. O cliente nunca recebe o URL assinado. O frontend envia bearer token em headers apenas para esse caminho da API, pede recarregamento e rejeita URLs externos. A remoção resolve a referência dentro da entrada do próprio autor.

**Legado por proteger:** recursos antigos do tipo `upload` podem continuar públicos no Cloudinary mesmo sem aparecer nos DTOs. Não foi consultado/alterado o armazenamento atual. Ficam preservados e em quarentena na aplicação, com `unavailablePhotoCount` e aviso ao autor; continuam a contar para o limite.

A proteção completa exige inventário autorizado, conversão para entrega autenticada, invalidação dos URLs/CDN públicos e atualização verificada das referências, preservando ficheiros/registos. Testar o URL antigo sem autenticação depois dessa operação. **A exposição de URLs públicos antigos já conhecidos não está resolvida.**

## Testes e limites

`tests/Authorization.Tests` usa Kestrel em porto efémero de localhost, controllers/serviços reais, autenticação de fixtures, repositórios em memória e armazenamento simulado. Não arranca o `Program` da API, não lê segredos nem regista SQL, Hangfire, BGG, notificações externas ou Cloudinary. Metadados/geração de SQL usam contexto separado com endereço fictício, sem abrir ligação.

Cobertura HTTP: visitante, criador, outro participante, utilizador alheio e membros de sessão/campanha sem participação; listagens/detalhes/históricos/pendências; leitura/escrita/limpeza do diário; upload/remoção/leitura de fotografias, cache e referências alheias; criação com zero e autoria autenticada; edição/eliminação/fecho pelo criador; legado preservado e uploads públicos rejeitados. A migração é verificada por geração de SQL, sem execução.

A partir da raiz do backend:

```text
dotnet run --project tests/Authorization.Tests --no-launch-profile
dotnet run --project tests/PlayerScores.Tests --no-launch-profile
```

No frontend: PT/EN, notas alheias ocultas mesmo com fixtures antigas, zero, histórico próprio, limites com fotografias em quarentena, participação, headers das imagens e rascunhos isolados entre encontros, incluindo respostas fora de ordem. Testes anteriores de pontuações/navegação mantidos.

Não valida JWT real, restrições SQL em execução, entrega/cache do Cloudinary real, revogação do legado ou comportamento nativo. A compilação mantém o aviso pré-existente `NU1903` do AutoMapper 14.0.0; dependências não atualizadas.

Resultados: 58 testes HTTP e uma verificação offline do modelo/migração passaram; nove testes de pontuações passaram. No frontend, 139 testes, TypeScript e exportação iOS passaram. A validação visual no iPhone está pendente. Nenhuma base de dados atual foi consultada ou alterada.

## iPhone

Usar backend atualizado, esquema preparado numa base descartável e armazenamento de teste protegido. Verificar registo com zero; resumo/avaliações/tags/fotografias entre participantes e notas apenas próprias; bloqueio a membros sem participação; edição refletida no histórico; troca rápida de encontros sem misturar rascunhos; fotografias após logout; restrições do legado; navegação, teclado e PT/EN.
