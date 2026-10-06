# Pesquisa — validação no ambiente descartável

## Diagnóstico e dados confirmados

API `http://192.168.1.83:5099/MeepleBoard`; Expo `exp://192.168.1.83:8082`; SQL `MeepleBoardDeviceTests` / `MeepleBoard_DeviceTests`, com CreatorId. A base habitual não foi consultada nem alterada.

Antes desta verificação, `autor@meepleboard.test` tinha Biblioteca vazia (HTTP 204), mas o histórico devolvia «Meeple Teste Competitivo» (HTTP 200). A Biblioteca agrega entradas e jogos jogados: em Todos/Já joguei esse jogo podia aparecer; em Tenho/Quero a ausência era legítima. A pesquisa da coleção é local sobre esses dados e respeita o separador e filtros, não consulta o catálogo por cada texto escrito.

O catálogo descartável contém apenas três jogos fictícios: «Meeple Teste Competitivo», «Meeple Teste Cooperativo» e «Meeple Teste Solo». `IBGGService` é substituído por `OfflineCatalog`; o BGG real não é contactado. A pesquisa do catálogo passa pelo serviço/repositório real sobre `GameSearchCatalog`, independentemente da coleção. Pesquisa por «Catan» ou outro jogo real ausente devolve HTTP 200 com `[]`, não uma falha de comunicação.

Falha confirmada na preparação das fixtures: faltavam as palavras em `GameSearchToken`. «Meeple» e o nome completo funcionavam, mas «competitivo» devolvia uma lista vazia indevida. O anfitrião de testes passa a preparar/reparar as palavras apenas dos três IDs fictícios, sem substituir tokens existentes. SQL confirmou seis tokens. Não houve alteração do modelo ou nova migração.

Para permitir um teste inequívoco na coleção, a verificação acrescentou uma entrada fictícia «Meeple Teste Solo», estado Tenho, preço 0, exclusivamente para o autor na base descartável. Repetir a verificação preserva a entrada e o preço existente. HTTP e SQL confirmaram a gravação e recarga; o histórico anterior foi preservado.

## Pedidos e respostas

| Percurso | Pedido/comportamento | Resultado confirmado |
| --- | --- | --- |
| Biblioteca → Pesquisar na coleção | Carrega `GET /users/{autor}/games` e `/played-games`; filtra os nomes localmente | Biblioteca: Solo com preço 0; histórico: Competitivo. Não dispara `/game/suggestions` ao escrever. |
| Mais → Pesquisar jogos | `GET /game/suggestions?query=Meeple%20Teste%20Competitivo&offset=0&limit=10&sort=relevance` | HTTP 200, um jogo Competitivo; detalhe real abre por ID. |
| Biblioteca → Adicionar jogo | Mesmo destino e pesquisa de catálogo; pesquisar `Meeple Teste Cooperativo` | HTTP 200, um jogo Cooperativo, disponível para adicionar. |
| Pesquisa por palavra | `GET /game/suggestions?query=competitivo&offset=0&limit=10&sort=relevance` | Antes: 200 `[]`; depois: 200, Competitivo. |
| Ausência legítima | `query=Catan`, `query=zzzinexistente`, ou `query=Meeple&playerCount=5` | HTTP 200 `[]`; jogos fictícios admitem até quatro jogadores. |

Os filtros de catálogo podem excluir jogos: para repetir os exemplos, limpar filtros, escolher Todos/jogos base e não impor avaliação mínima (os jogos de teste não têm avaliação BGG). Não foram inventadas avaliações para contornar filtros.

## Estados de interface e verificações

Corrigida a perda da mensagem de erro ao carregar os jogos jogados: Redux guarda o erro separadamente; a Biblioteca apresenta falha mesmo com dados anteriores e permite repetir ambos os pedidos. Mantém os dados carregados; em caso de erro não apresenta uma coleção vazia enganadora. Pull-to-refresh recarrega também o histórico. A pesquisa do catálogo apresenta mensagem PT/EN específica de comunicação, carregamento e ausência de resultados distintos, com repetição do pedido após falha.

Verificação repetível no backend: `node tools/DeviceTestApi/verify-catalog-search.cjs`, com API descartável ativa. Valida saúde/identidade do destino antes de escrever a fixture, login, nomes completos/palavras, resultados vazios, filtros, detalhes, adição/recarga e preservação do histórico. Regressão de leitura de sessões/campanhas voltou a passar. TypeScript e 155 testes frontend passaram, incluindo estados PT/EN e preservação de dados após falhas. Estes testes não provam a experiência visual no dispositivo.

## Repetir no iPhone

A API foi reiniciada para reparar as fixtures: terminar a sessão e voltar a entrar como `autor@meepleboard.test`, com a palavra-passe local em `.device-tests/accounts.json`. O Expo de testes foi também reiniciado com cache limpa; o Expo habitual na 8081 foi preservado. A verificação do bundle deve usar o endereço do manifesto Expo, incluindo a raiz `src/app`; a primeira consulta sem essa raiz não incluía os ecrãs da aplicação e não demonstrava um bundle antigo. Recarregar o projeto no Expo Go na porta 8082 para receber as alterações; atualizar a Biblioteca para recarregar a entrada fictícia.

1. Biblioteca → **Todos ou Tenho**, sem filtros → Pesquisar na coleção: escrever **Meeple Teste Solo**. Deve aparecer um jogo. Em Todos/Já joguei, **Meeple Teste Competitivo** também deve aparecer.
2. Mais → Pesquisar jogos: limpar filtros e escrever **Meeple Teste Competitivo**. Deve aparecer um jogo; **competitivo** também deve funcionar.
3. Biblioteca → Adicionar jogo: escrever **Meeple Teste Cooperativo**. Deve aparecer um jogo; abrir detalhe ou usar o fluxo de adição existente.
4. Escrever **zzzinexistente** no catálogo: deve aparecer ausência de resultados, sem mensagem de falha.

Se falhar, registar percurso, texto, filtros, mensagem e se o carregamento termina; não enviar credenciais/tokens. A confirmação do iPhone precede a etapa de criação/convites/registo. UPD01/JRN01 continuam pendentes. A preferência visual para autenticação foi guardada em [preferencias-visuais-autenticacao.md](preferencias-visuais-autenticacao.md), para uma etapa posterior.
