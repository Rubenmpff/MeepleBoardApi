# Bloqueio, denúncias, suspensão e filtro de termos

Implementado em DeviceTests a 2026-10-18, segundo `docs/proposta-bloqueio-comunidade.md` (app) e os ajustes aprovados. Produção, Azure, segredos e AutoMapper não foram alterados.

## Bloqueio

- **Tabela própria `UserBlocks`** (quem bloqueia, quem é bloqueado, data). Cada pessoa cria e remove só o seu bloqueio. Desbloquear não restaura a amizade, e um bloqueio da outra pessoa mantém-se.
- **Ao bloquear:** a amizade e os pedidos pendentes entre os dois são apagados. A outra pessoa não é avisada.
- **Num bloqueio em qualquer sentido:**
  - a pesquisa deixa de mostrar a outra pessoa;
  - o perfil, a coleção e os jogos jogados dela respondem 404;
  - as vistas em conjunto (partidas partilhadas, comparação de estatísticas) respondem como para quem não é amigo;
  - os pedidos de amizade são recusados;
  - as publicações de um deixam de aparecer ao outro, na lista e na contagem.
- **O que fica aberto:** cada pessoa continua a abrir as partidas, os diários e as sessões partilhadas a que já tinha acesso. Os nomes e os resultados ficam.
- **«Bloquear autor»:** `POST /community/blocks/experiences/{publicationId}`. A lista de publicações nunca traz o id de quem escreveu.

## Convites, partidas e campanhas

- **Convite pendente que um bloqueio tornou impossível de aceitar** (bloqueio com quem organiza, ou com um participante ou membro aceite):
  - sai de `GET /session/mine`;
  - o detalhe traz `inviteAvailable: false`, e a app mostra «Este convite já não está disponível.» sem botões;
  - aceitar responde 409 «Não é possível aceitar este convite.»; recusar continua possível.
  - Nenhuma sessão é cancelada, e ninguém aceite é retirado.
- **Novos convites:**
  - **Sessões:** só amigos aceites, sem bloqueio com quem organiza, nem com um participante aceite ou com convite pendente.
  - **Campanhas:** só os amigos aceites de quem convida, com a mesma regra de bloqueios.
  - A recusa diz «Não é possível convidar esta pessoa.»
- **Registar uma partida** (também com IDs enviados diretamente):
  - nunca com duas pessoas com bloqueio entre si, seja quem for que regista;
  - fora de sessões, cada outro jogador tem de ser amigo aceite de quem regista;
  - dentro de uma sessão, bastam os participantes aceites, mesmo sem amizade.
- **Exceção necessária:** o registo de um encontro de campanha (`CreateCampaignEncounterScreen`). O pedido traz `campaignId`, e os jogadores têm de ser membros aceites dessa campanha, mesmo sem amizade. Os bloqueios continuam a ser verificados.
- **Acrescentar um jogador depois** (`POST /matches/{id}/players`): segue as mesmas regras.
- **Juntar uma partida anterior a uma campanha:** continua possível para quem é membro e jogou essa partida. Não alarga o acesso nem cria convites.

## Denúncias e moderação

- **Pessoa:** `POST /community/people/{id}/reports`, com motivo Abuse, Harassment, OffensiveName, Spam ou Other. Uma denúncia aberta por par.
- **Fotografia do diário:** `POST /campaigns/matches/{m}/journal/photos/{entry}/{key}/reports`.
  - Só quem jogou a partida pode denunciar, e nunca uma fotografia sua.
  - A partida e a chave da fotografia têm de corresponder.
- **Nada é automático:** nenhuma denúncia suspende ninguém nem apaga fotografias.
- **Administração** (`/admin/moderation/...`):
  - **Pessoas:** «Arquivar», «Suspender» ou «Levantar suspensão».
  - **Fotografias:** «Manter» ou «Remover fotografia». O administrador só abre uma fotografia enquanto ela tem uma denúncia aberta.

## Suspensão

- **Prazo:** 7 dias, 30 dias ou até revisão, sempre com motivo. É diferente do bloqueio do Identity por tentativas falhadas.
- **Ao suspender:**
  - `TokensRevokedAt` passa a ser a data da suspensão;
  - os refresh tokens são apagados;
  - o access token passa a ter `iat`.
- **Verificação em cada pedido** (`OnTokenValidated`): recusa uma conta suspensa e qualquer token emitido até ao segundo da suspensão.
- **No fim do prazo ou ao levantar a suspensão:** os tokens anteriores nunca voltam a funcionar, e é preciso iniciar sessão de novo.
- **Início de sessão:** responde 403 com `code: account_suspended` e `suspendedUntil`, nunca com o motivo. A app diz até quando e indica que contestar a suspensão ou pedir a eliminação da conta se faz pelo apoio.
- **Contacto de apoio:** o endereço real continua pendente. Nenhum foi inventado, e o requisito continua por cumprir.

## Eliminação de conta

- Os bloqueios feitos e recebidos são apagados.
- As denúncias sobre a pessoa perdem o texto, e as abertas são fechadas («deleted»).
- O motivo da suspensão não é guardado.
- O texto das denúncias que a pessoa fez segue a retenção atual (90 dias).

## Filtro de termos (PT/EN)

- **Funcionamento:** `ContentFilter` compara palavras e expressões inteiras, sem acentos nem maiúsculas. Nunca procura dentro de outras palavras: «computador», «Barcelona», «Scunthorpe» e «therapist» passam.
- **Onde se aplica:**
  - nome de utilizador no registo;
  - local e resumo da partida;
  - nome e local da sessão;
  - nome e notas da campanha, e título do encontro;
  - etiquetas do diário;
  - comentário publicado (publicar, publicar um rascunho, editar);
  - título e autor de um recurso sugerido.
- **Onde não se aplica:** notas privadas, rascunhos por publicar e endereços (URL) dos recursos, que nunca são alterados.
- **Termos retirados por darem falsos positivos em contexto de jogo:** «cabra», «fag», «rape», «go die», «vou-te matar» e «I will kill you».
- **Limites:**
  - A lista é uma proteção inicial e incompleta. Um texto que passa no filtro não fica por isso aceitável.
  - Ameaças reais continuam proibidas pelas regras, incluindo as expressões retiradas da lista. São tratadas por denúncia e moderação (suspensão, quando se justifica).
  - O filtro não deteta ameaças escritas de outra forma, ortografia alterada, outras línguas nem contexto. Não substitui as denúncias nem a revisão humana.

## Testes

- `tests/Community.Tests` (16): filtro PT/EN com falsos positivos, quem pode jogar com quem, convites e aceitação de sessões e campanhas, HTTP de bloqueios, denúncias, moderação, suspensão e recursos.
- `tools/DeviceTestApi/verify-community.cjs`, contra SQL real: as mesmas regras com tokens reais, bloqueios nos dois sentidos e eliminação de conta.
- **Verificadores antigos:** todos passaram a usar pessoas descartáveis, com as amizades, a participação em sessões e campanhas e as avaliações fechadas que as regras atuais exigem. As recusas continuam testadas explicitamente. Ver `docs/testes-macos-docker.md`.
- **Encontros de campanha:** enviar um `campaignId` não basta. Quem regista e cada jogador têm de ser membros aceites dessa campanha nesse momento. São recusados um membro pendente, um membro removido, quem não é membro, mesmo sendo amigo, uma campanha inexistente e um bloqueio entre membros (Community.Tests e verify-community).
