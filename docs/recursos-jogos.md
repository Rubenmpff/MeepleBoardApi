# Recursos dos jogos

Catálogo de links externos por jogo (regras, vídeos, modelos 3D, unboxings, materiais). Não aloja ficheiros: cada recurso aponta para a página original.

## Modelo

`GameResource`: jogo, tipo (`Rules`, `HowToPlay`, `Playthrough`, `PrintModel`, `Unboxing`, `Material`), categoria 3D (`Organizer`, `Accessory`, `ReplacementPart`, `Decorative`, obrigatória só em `PrintModel`), idioma (ISO 639-1; nulo quando não depende de idioma), título, autor, plataforma, URL (https, até 500), edição/expansões compatíveis (texto livre), `IsOfficial` (da editora), `IsFree` (nulo se não confirmado), estado (`Pending`/`Approved`/`Rejected`), quem sugeriu e revisão.
`GameResourceReport`: recurso, utilizador, motivo (`BrokenLink`, `WrongGame`, `WrongLanguage`, `Incompatible`, `Other` com descrição obrigatória), comentário, resolução. Um relato aberto por utilizador e recurso.

Migração `20261012081445_AddGameResources`: duas tabelas novas e quatro índices. Apagar um jogo apaga os seus recursos; apagar um recurso apaga os relatos.

## API

| Rota | Quem | Função |
| --- | --- | --- |
| `GET /MeepleBoard/game/{gameId}/resources?language=pt\|en\|…\|all` | autenticado | Só aprovados. Um idioma inclui também os recursos sem idioma; devolve contagens por idioma e tipo para oferecer alternativas |
| `POST /MeepleBoard/game/{gameId}/resources/suggestions` | autenticado | Sugestão, fica `Pending` |
| `POST /MeepleBoard/game-resources/{id}/reports` | autenticado | Reportar problema (só recursos públicos) |
| `GET /MeepleBoard/game-resources/permissions` | autenticado | `canManage` para a app mostrar a gestão |
| `/MeepleBoard/admin/game-resources…` e `POST /MeepleBoard/admin/game/{gameId}/resources` | papel `Admin` | Listar por estado, criar (aprovado), editar, aprovar, rejeitar (com nota), remover, relatos e resolver |

Endereços repetidos no mesmo jogo são recusados (409), exceto se o anterior foi rejeitado. Ordenação: da editora primeiro, depois por tipo, idioma pedido e título.

## Atribuição BoardGameGeek

A página oficial de termos (`boardgamegeek.com/wiki/page/XML_API_Terms_of_Use`, política citada como versão de 2025-07-02 em `boardgamegeek.com/using_the_xml_api`) recusa leitura automática (HTTP 403); o resumo abaixo vem de fontes secundárias e tem de ser confirmado à mão antes de uma publicação pública:

- apps públicas mostram o logótipo **«Powered by BGG»** com ligação a boardgamegeek.com, em tamanho legível;
- uso comercial exige licença comercial do BGG; registo da aplicação e token para a XML API;
- pedidos limitados e com cache; não reencaminhar dados BGG para terceiros.

Na app, os dados BGG estão identificados junto da informação («Ficha do BoardGameGeek» com ligação à página do jogo, «Avaliação BGG», origem da descrição). **Pendente:** colocar o ficheiro oficial do logótipo «Powered by BGG» (página de logótipos inacessível a leitura automática) e confirmar a classificação comercial/não comercial do MeepleBoard.

## Descrições traduzidas

Não existe tradução de descrições na API nem na app: as descrições são o texto original importado do BGG. A app identifica-o como «Texto original do BoardGameGeek, sem tradução.» Se for introduzida tradução, guardar a origem (BGG) e o método (tradução automática ou humana) junto do texto.

## DeviceTests

- Conta fictícia `admin@meepleboard.test` com papel `Admin` (palavra-passe em `.device-tests/accounts.json`, não versionado); não é amiga de ninguém.
- Jogo real **Wingspan** (BGG 266192, Stonemaier Games, 2019, 1–5 jogadores, factos verificados) sem descrição, imagem ou avaliações BGG copiadas.
- `tools/DeviceTestApi/game-resources.fixture.json`: 9 links reais verificados a 2026-10-12 (página de regras da editora com HTTP 200; títulos e canais dos vídeos via oEmbed do YouTube; nome, autor e gratuitidade dos modelos via API da Printables). Sem vídeos de partida completa, unboxings, materiais ou conteúdos em português que tenham sido confirmados: essas áreas mostram estados vazios.

```sh
dotnet run --project tests/GameResources.Tests --no-launch-profile   # 9 testes HTTP isolados
node tools/DeviceTestApi/verify-game-resources.cjs                   # semeia (idempotente) e verifica com SQL real
node tools/DeviceTestApi/verify-game-resources.cjs --recheck         # depois de reiniciar a API
```
