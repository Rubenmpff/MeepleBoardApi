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

Estado em 2026-10-14: as páginas oficiais (`boardgamegeek.com/wiki/page/XML_API_Terms_of_Use`, `boardgamegeek.com/using_the_xml_api`, `boardgamegeek.com/wiki/page/BGG_XML_API_Commercial_Use`) e as cópias no Wayback Machine recusam leitura automática (HTTP 403, desafio Cloudflare). Os excertos do texto oficial devolvidos por pesquisa dizem:

- em todas as utilizações, creditar o BoardGameGeek pelo nome como origem dos dados;
- apps públicas incluem o logótipo **«Powered by BGG»** com ligação ao BoardGameGeek (ficheiros de exemplo na página `using_the_xml_api`);
- licença só para fins estritamente não comerciais; qualquer fim comercial (por exemplo cobrar funcionalidades ou suporte) exige licença comercial, caso a caso;
- os dados obtidos pela XML API não podem ser modificados; é proibido usá-los para treinar IA.

Cumprido na app: origem visível junto dos dados («Ficha do BoardGameGeek» com ligação à página do jogo; «Avaliação BGG» separada de «Avaliação MeepleBoard» e «A tua avaliação»; descrição mostrada sem alterações, só recolhida, com «Texto original do BoardGameGeek, sem tradução.»).

**Pendente (BGG-ATTR01), precisa do responsável do projeto:** abrir as três páginas num navegador e guardar o texto (PDF ou colado em `docs/`); descarregar os ficheiros oficiais do logótipo «Powered by BGG» de `using_the_xml_api` sem os alterar; confirmar se o MeepleBoard é não comercial e se a aplicação está registada com token. Com os ficheiros, coloca-se o logótipo com ligação ao BoardGameGeek no detalhe do jogo e no Perfil (Sobre).

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
