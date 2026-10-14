# Recursos dos jogos

Catálogo de links externos por jogo (regras, vídeos, modelos 3D, unboxings, materiais). Não aloja ficheiros: cada recurso aponta para a página original.

## Modelo

`GameResource`: jogo, tipo (`Rules`, `HowToPlay`, `Playthrough`, `PrintModel`, `Unboxing`, `Material`), categoria 3D (`Organizer`, `Accessory`, `ReplacementPart`, `Decorative`, obrigatória só em `PrintModel`), idioma (ISO 639-1; nulo quando não depende de idioma), título, autor, plataforma, URL (https, até 500), edição/expansões compatíveis (texto livre), `Expansion` (a expansão a que se destina; nulo = jogo base), `ThumbnailUrl` (imagem de pré-visualização no site original, https; nula quando não existe), `IsOfficial` (da editora), `IsFree` (nulo se não confirmado), estado (`Pending`/`Approved`/`Rejected`), quem sugeriu e revisão.
`GameResourceReport`: recurso, utilizador, motivo (`BrokenLink`, `WrongGame`, `WrongLanguage`, `Incompatible`, `Other` com descrição obrigatória), comentário, resolução. Um relato aberto por utilizador e recurso.

Migração `20261012081445_AddGameResources`: duas tabelas novas e quatro índices. Apagar um jogo apaga os seus recursos; apagar um recurso apaga os relatos.
Migração `20261014100853_AddGameResourceExpansionThumbnail`: duas colunas anuláveis em `GameResources` (`Expansion` nvarchar(100), `ThumbnailUrl` nvarchar(500)); aprovada na auditoria do anfitrião DeviceTests.

## API

| Rota | Quem | Função |
| --- | --- | --- |
| `GET /MeepleBoard/game/{gameId}/resources?language=pt\|en\|…\|all` | autenticado | Só aprovados. Um idioma inclui também os recursos sem idioma; devolve contagens por idioma e tipo para oferecer alternativas |
| `POST /MeepleBoard/game/{gameId}/resources/suggestions` | autenticado | Sugestão, fica `Pending` |
| `POST /MeepleBoard/game-resources/{id}/reports` | autenticado | Reportar problema (só recursos públicos) |
| `GET /MeepleBoard/game-resources/permissions` | autenticado | `canManage` para a app mostrar a gestão |
| `/MeepleBoard/admin/game-resources…` e `POST /MeepleBoard/admin/game/{gameId}/resources` | papel `Admin` | Listar por estado, criar (aprovado), editar, aprovar, rejeitar (com nota), remover, relatos e resolver |

Endereços repetidos no mesmo jogo são recusados (409), exceto se o anterior foi rejeitado. Ordenação: jogo base primeiro e depois cada expansão; dentro de cada grupo, da editora primeiro, depois por tipo, idioma pedido e título.

## Amigos que têm o jogo

`GET /MeepleBoard/game/{gameId}/friend-owners` (autenticado) devolve `{ total, items: [{ userId, userName, profilePictureUrl }] }`, ordenado por nome. Regras, aplicadas no serviço (`UserGameLibraryService.GetFriendOwnersAsync`) sobre as entradas devolvidas pelo repositório:

- só amizades `Accepted` (pedidos pendentes e não-amigos ficam de fora);
- só o estado «Tenho» (`Owned`); «Quero jogar» e «Já joguei» não são posse;
- só bibliotecas que o utilizador pode ver, a mesma regra de `GetUserLibraryAsync`: `Public` ou `FriendsOnly`; `Private` nunca aparece;
- não devolve preço, estado nem privacidade; jogo inexistente 404, sem sessão 401.

DeviceTests: o anfitrião cria contas fictícias `Teste-…` ligadas a `participante` para o Wingspan: cinco que aparecem (incluindo um nome longo) e cinco que não podem aparecer (biblioteca privada, lista de desejos, «Já joguei», pedido pendente, não amigo). A lista de amigos do `autor` não muda. `verify-game-friends.cjs` verifica-o com SQL real, incluindo tornar uma biblioteca privada (e repô-la).

## Ficha BGG: duração, idade e complexidade

`Game.MinPlayTime`, `MaxPlayTime`, `MinAge` (int) e `AverageWeight` (1–5), todos anuláveis, no `GameDto`. Vêm do XML do BGG (`minplaytime`, `maxplaytime`, `playingtime`, `minage`, `averageweight`) na importação de um jogo e na sincronização (`BGGSyncJob` → `UpdateFromBggAsync`), através de `Game.SetBggSheet`: 0 ou valores fora do intervalo = sem dado; sem mínimo nem máximo usa-se `playingtime`; mínimo e máximo trocados são corrigidos; a complexidade fica com duas casas. Os jogos existentes recebem a ficha na próxima sincronização. A app só mostra o que existe e identifica a origem («Ficha do BoardGameGeek»).

Migração `20261014164836_AddGameSheetAndSessionPlannedGame`: quatro colunas anuláveis em `Games` e `GameSessions.PlannedGameId` (ver `sessoes-encerramento-automatico.md`); auditada pelo anfitrião DeviceTests.

## Expansões e biblioteca

O detalhe do jogo base já devolve as expansões do catálogo (`GameDto.Expansions`, por `BaseGameId`). A app cruza-as com a biblioteca do utilizador para mostrar o estado; não há rota nova.

## Atribuição BoardGameGeek

Estado em 2026-10-14: as páginas oficiais (`boardgamegeek.com/wiki/page/XML_API_Terms_of_Use`, `boardgamegeek.com/using_the_xml_api`, `boardgamegeek.com/wiki/page/BGG_XML_API_Commercial_Use`) e as cópias no Wayback Machine recusam leitura automática (HTTP 403, desafio Cloudflare). Os excertos do texto oficial devolvidos por pesquisa dizem:

- em todas as utilizações, creditar o BoardGameGeek pelo nome como origem dos dados;
- apps públicas incluem o logótipo **«Powered by BGG»** com ligação ao BoardGameGeek (ficheiros de exemplo na página `using_the_xml_api`);
- licença só para fins estritamente não comerciais; qualquer fim comercial (por exemplo cobrar funcionalidades ou suporte) exige licença comercial, caso a caso;
- os dados obtidos pela XML API não podem ser modificados; é proibido usá-los para treinar IA.

Cumprido na app: origem visível junto dos dados («Ficha do BoardGameGeek» com ligação à página do jogo; «Avaliação BGG» separada de «Avaliação MeepleBoard» e «A tua avaliação»; descrição mostrada sem alterações, só recolhida, com «Texto original do BoardGameGeek, sem tradução.»).

**Âmbito (2026-10-14):** o MeepleBoard tem intenção de monetização e não é tratado como projeto só não comercial. O responsável tem licença do BGG; o âmbito aplicável confirma-se pelos termos e pela autorização que vai fornecer, não por excertos de pesquisa.

**Configuração existente (verificada sem ler valores):**
- `Program.cs` regista o cliente da XML API com `Bgg:BaseUrl` (`https://boardgamegeek.com/xmlapi2/`), envia `Authorization: Bearer` quando `Bgg:Token` existe e identifica-se como `MeepleBoard/1.0`.
- Nenhum token configurado neste computador: `Bgg:Token` vazio em `appsettings.json`, ausente em `appsettings.Development.json`, nos user-secrets e nas variáveis de ambiente; `.env.example` não documenta a chave. A configuração do servidor de produção não está no repositório e não foi verificada.
- O repositório não identifica a aplicação registada no BGG (nome ou ID).
- Outra fonte de dados BGG: importação do catálogo a partir do ficheiro de rankings (`Bgg:CatalogLocalFilePath`, `Bgg:CatalogPageUrl`); deve estar coberta pela mesma autorização.
- Mecanismo em uso: só a chave de configuração `Bgg:Token`, lida pelos fornecedores normais do ASP.NET e usada pelo cliente da XML API e pela importação do catálogo (no Windows, variável de ambiente `Bgg__Token`). Nenhum script, perfil do IDE (`launchSettings.json`), Docker (`Dockerfile`, `docker-compose.yml`) ou workflow a define; nunca esteve em `appsettings*.json` no histórico.
- Mac (2026-10-14): `Bgg__Token` ausente (shell, perfis zsh, `launchctl`, user-secrets e processo da API), não apenas não herdado. Basta defini-la no Mac, por exemplo em `~/.zprofile`, que o Terminal e os IDEs abertos pelo Dock herdam.
- DeviceTests fica isolado: o anfitrião usa configuração em memória (não lê variáveis de ambiente para o BGG) e `OfflineCatalog`.

**Pendente (BGG-ATTR01):** confirmar o âmbito com os termos e a autorização fornecidos; definir `Bgg__Token` no Mac (nunca em ficheiros versionados); colocar o ficheiro oficial «Powered by BGG», sem alterações e nas condições da autorização, junto dos dados BGG. Não fechar com base em excertos de pesquisa.

## Descrições traduzidas

Não existe tradução de descrições na API nem na app: as descrições são o texto original importado do BGG. A app identifica-o como «Texto original do BoardGameGeek, sem tradução.» Se for introduzida tradução, guardar a origem (BGG) e o método (tradução automática ou humana) junto do texto.

## DeviceTests

- Conta fictícia `admin@meepleboard.test` com papel `Admin` (palavra-passe em `.device-tests/accounts.json`, não versionado); não é amiga de ninguém.
- Jogo real **Wingspan** (BGG 266192, Stonemaier Games, 2019, 1–5 jogadores, factos verificados) sem descrição, imagem ou avaliações BGG copiadas. A 2026-10-14 ganhou a duração 40–70 min e a idade 10+, tal como a página do BGG as mostrava nas capturas do utilizador desse dia; a complexidade (estatística da comunidade BGG) não é copiada, por isso a ficha fica incompleta. Ganhou também duas expansões reais, só com o nome (`Wingspan: European Expansion`, `Wingspan: Oceania Expansion`); a conta `participante` tem a European com «Tenho». Tudo reposto em cada arranque.
- `tools/DeviceTestApi/game-resources.fixture.json`: 9 links reais verificados a 2026-10-12 (página de regras da editora com HTTP 200; títulos e canais dos vídeos via oEmbed do YouTube; nome, autor e gratuitidade dos modelos via API da Printables). Sem vídeos de partida completa, unboxings, materiais ou conteúdos em português que tenham sido confirmados: essas áreas mostram estados vazios. A 2026-10-14 ganharam expansão (European, Oceania, Wingspan Asia) e miniatura: a do próprio YouTube (`i.ytimg.com`) para os vídeos e a pré-visualização da Printables (`media.printables.com`) para os modelos, todas com HTTP 200; a página de regras não tem imagem adequada e mostra o ícone neutro.

```sh
dotnet run --project tests/GameResources.Tests --no-launch-profile   # 11 testes isolados (inclui a ficha BGG)
dotnet run --project tests/Library.Tests --no-launch-profile         # inclui amigos com o jogo
node tools/DeviceTestApi/verify-game-friends.cjs                     # amigos com o jogo, SQL real
node tools/DeviceTestApi/verify-game-page.cjs                        # ficha, expansões e jogo previsto, SQL real
node tools/DeviceTestApi/verify-game-resources.cjs                   # semeia (idempotente) e verifica com SQL real
node tools/DeviceTestApi/verify-game-resources.cjs --recheck         # depois de reiniciar a API
```
