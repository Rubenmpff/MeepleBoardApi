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

**Âmbito (2026-10-14):** o MeepleBoard tem intenção de monetização e não é tratado como projeto só não comercial. O responsável tem licença do BGG; o âmbito aplicável confirma-se pelos termos e pela autorização que vai fornecer, não por excertos de pesquisa.

**Configuração existente (verificada sem ler valores):**
- `Program.cs` regista o cliente da XML API com `Bgg:BaseUrl` (`https://boardgamegeek.com/xmlapi2/`), envia `Authorization: Bearer` quando `Bgg:Token` existe e identifica-se como `MeepleBoard/1.0`.
- Nenhum token configurado neste computador: `Bgg:Token` vazio em `appsettings.json`, ausente em `appsettings.Development.json`, nos user-secrets e nas variáveis de ambiente; `.env.example` não documenta a chave. A configuração do servidor de produção não está no repositório e não foi verificada.
- O repositório não identifica a aplicação registada no BGG (nome ou ID).
- Outra fonte de dados BGG: importação do catálogo a partir do ficheiro de rankings (`Bgg:CatalogLocalFilePath`, `Bgg:CatalogPageUrl`); deve estar coberta pela mesma autorização.
- DeviceTests não contacta o BGG (`OfflineCatalog`).
- Mac (2026-10-14): `Bgg__Token` não está definido em nenhum ponto (shell, perfis zsh, `launchctl`, Keychain, user-secrets nem no processo da API DeviceTests): ausente, não apenas não herdado. Os scripts de arranque não o definem.
- Token local: `scripts/bgg-token.sh set` guarda-o no Keychain de início de sessão (o próprio `security` pede o valor, oculto); `scripts/bgg-token.sh run -- <comando>` passa-o como `Bgg__Token` (lido como `Bgg:Token`) só a esse processo; `status` indica apenas se existe. O Azure não foi alterado.

**Pendente (BGG-ATTR01):** confirmar o âmbito com os termos e a autorização fornecidos; configurar o token como segredo (user-secrets ou variável `Bgg__Token`, nunca em ficheiros versionados); colocar o ficheiro oficial «Powered by BGG», sem alterações e nas condições da autorização, junto dos dados BGG. Não fechar com base em excertos de pesquisa.

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
