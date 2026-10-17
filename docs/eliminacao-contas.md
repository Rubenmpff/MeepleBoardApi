# Eliminação de contas (decidida e implementada a 2026-10-16, em DeviceTests)

**Antes:** apagar uma conta com qualquer atividade falhava. Havia 12 chaves estrangeiras restritivas para `AspNetUsers`:

- denúncias de experiências e de recursos;
- jogadores das partidas e vencedor;
- diário;
- amizades (3 chaves);
- organizador e jogador de sessões;
- criador e membro de campanhas.

Só se apagavam contas sem atividade.

**Decisão do utilizador:**

- A conta é **anonimizada**, não removida.
- As avaliações **saem da média MeepleBoard**.

## O que acontece (`IUserRepository.AnonymizeAsync`, tudo numa transação)

Corre dentro da estratégia de execução do EF (`EnableRetryOnFailure`), por isso uma repetição volta a executar a unidade inteira. Nunca fica nada guardado a meio. O recálculo das médias acontece na mesma transação.

| Sai | Fica |
| --- | --- |
| Nome, email, telefone, palavra-passe, fotografia, token de push, 2FA, acessos externos, tokens, claims e perfis (a conta fica bloqueada para sempre) | A linha da conta, com o nome neutro `deleted:xxxxxxxx` (a app mostra «Conta eliminada» / «Deleted account») e `DeletedAt` |
| Biblioteca, amizades (e pedidos), convites de sessão não aceites | Lugar nas partidas, sessões aceites e campanhas: os resultados dos outros não mudam |
| Diário: notas, fotografias, avaliações e publicações (e as denúncias feitas a essas publicações) | Denúncias feitas pela pessoa: continuam a contar para a moderação, sem a identificar |
| Avaliação e nota antigas guardadas na própria partida (`Match.PersonalRating`/`Notes`) das partidas que criou | Recursos que sugeriu |

Também acontece:

- Os jogos em que a pessoa contava são recalculados.
- As fotografias são apagadas do armazenamento depois da transação. Uma falha deixa só um aviso no registo e um ficheiro inacessível, nunca uma conta meio apagada.
- As contas eliminadas não aparecem na pesquisa de pessoas.
- A limpeza de contas não confirmadas (`UserCleanupJob`) ignora-as.
- Só administradores eliminam (`DELETE /MeepleBoard/users/{id}`). Uma conta já eliminada dá 404.

**Migração `20261016201449_AddUserDeletedAt`:** acrescenta uma coluna `AspNetUsers.DeletedAt` (`datetime2`, opcional). Os dados existentes não mudam. O anfitrião DeviceTests audita esta migração no arranque.

## Verificação

`tools/DeviceTestApi/verify-account-deletion.cjs` corre com SQL real e usa a conta descartável «eliminar» (`.device-tests/disposable-account.json`, que o anfitrião recria vazia no arranque seguinte).

Cria atividade:

- amizade;
- biblioteca;
- partida partilhada avaliada 7 e 9, com nota;
- duas publicações;
- uma denúncia.

Depois confirma:

- a eliminação: 403 para outra conta, 204 para o administrador, 404 à segunda tentativa;
- o login fechado;
- que o diário, a nota, a avaliação, a publicação e a amizade saíram;
- que a conta já não se encontra na pesquisa e não tem email;
- que o jogador anónimo continua na partida, com o marcador neutro;
- que a denúncia continua a contar;
- que um token e um refresh token emitidos antes da eliminação deixam de dar acesso;
- que a conta deixa de contribuir para a duração da comunidade, enquanto o outro jogador mantém a partida e a duração;
- que as médias voltam ao valor inicial no fim.

## Limitações conhecidas

- **Token de acesso já emitido** (resolvido a 2026-10-17): cada pedido autenticado confirma que a conta existe e não foi eliminada (`JwtBearerEvents.OnTokenValidated`, uma consulta por chave primária). Um token anterior à eliminação dá 401, e o refresh token anterior não renova, porque foi apagado com a conta. O verificador testa-o e falha sem a correção.
- **Nome da conta eliminada** (resolvido a 2026-10-17): a base guarda o marcador neutro `deleted:xxxxxxxx`, que nenhuma conta pode registar porque o Identity não aceita `:` nos nomes. A app traduz-o (PT «Conta eliminada», EN «Deleted account»). O anfitrião DeviceTests converte no arranque as contas eliminadas antes desta mudança.
- **Pedido de eliminação pela própria pessoa.** A app ainda não o tem. Hoje só um administrador elimina contas.

## Eliminação pela própria pessoa (2026-10-17, DeviceTests)

As decisões D1–D9 e a tabela completa de tratamento estão em `MeepleBoardApp/docs/proposta-eliminacao-conta.md`. As duas vias acabam em `UserService.DeleteAsync` → `AnonymizeAsync`, numa só transação.

**Rotas:**

| Rota | O que faz |
| --- | --- |
| `GET /MeepleBoard/users/me/deletion-summary` | Sessões agendadas e a decorrer que a pessoa organiza; campanhas que criou com outros membros ativos, com os membros e se há notas; campanhas só suas. |
| `DELETE /MeepleBoard/users/me` com `{ password, campaignTransfers: [{ campaignId, newManagerId }] }` | 204 eliminada; 400 palavra-passe errada ou gestor inválido; 429 bloqueio do Identity; 401 sem sessão. |
| `POST /MeepleBoard/admin/account-deletion/requests` com `{ email }` (administrador) | 202, com a mesma resposta exista ou não a conta. Envia para o endereço da conta uma ligação de uso único (token do Identity, finalidade `AccountDeletion`, 24 h). |
| `GET /MeepleBoard/account-deletion/confirm?u&t` | Página HTML que só mostra o formulário. Os programas de correio abrem ligações sozinhos, por isso abrir não elimina. |
| `POST /MeepleBoard/account-deletion/confirm` | Elimina a conta e envia um e-mail de conclusão para o endereço capturado antes de anonimizar. Uma ligação já usada ou expirada dá 400 e não muda nada. |

**O que `AnonymizeAsync` passou a fazer, além do que já fazia:**

- **Partidas sem outro participante ativo:** são apagadas, com os diários e as ligações a campanhas.
- **Partidas partilhadas criadas pela pessoa:**
  - apaga `Location`, `ScoreSummary` e `Tags`, e o `SessionTitle` dos encontros;
  - os dados estruturados ficam: jogo, jogadores, pontuações, resultados e duração.
- **Sessões que organiza:**
  - as agendadas ficam canceladas (`IsCancelled`), com push aos convidados (uma falha não impede a eliminação);
  - a que está a decorrer termina, com o mesmo fecho condicional de «Terminar sessão»;
  - todas ficam com nome neutro `deleted:session:AAAA-MM-DD` e sem local.
- **Campanhas que criou:**
  - sem outros membros ativos: são apagadas;
  - com outros membros: passam para o novo gestor escolhido, ou ficam com a gestão só de leitura;
  - em ambos os casos, o nome passa a `deleted:campaign` e as notas ficam.
- **Recursos sugeridos:** `SuggestedById` passa a nulo.
- **Denúncias feitas pela pessoa:** o texto das resolvidas é apagado logo; o das abertas, quando forem resolvidas (`Resolve`) ou, no máximo, 90 dias depois da eliminação (`UserCleanupJob`).
- **Conta:** `LastActiveAt` é apagado; o ficheiro da fotografia de perfil, se estiver no Cloudinary, também.
- **Pedidos simultâneos:** a linha da conta fica bloqueada (`UPDLOCK`) durante a transação, por isso há uma única eliminação; os outros pedidos recebem 404.

**Fotografias:**

- Três tentativas imediatas.
- Se continuar a falhar: `PhotoDeletionJob` no Hangfire (10 tentativas). O argumento é só a referência da fotografia.

**Registos:** o início de sessão e o `UserCleanupJob` deixaram de registar e-mails.

**Verificação:**

- **`tests/Accounts.Tests` (8 testes isolados):**
  - marcadores neutros;
  - texto das denúncias;
  - tentativas e job no armazenamento;
  - push que falha;
  - repetição;
  - contrato HTTP e páginas da ligação.
- **`tools/DeviceTestApi/verify-own-account-deletion.cjs` (SQL real):**
  - **Contas:** «conta-fica-a» e «conta-fica-b» ficam (`account-deletion-accounts.json`). As pessoas a eliminar são criadas de novo em cada execução (`account-deletion-people.json`). O `AccountDeletionFixture` só responde no próprio Mac.
  - **O que cobre:**
    - app e web;
    - falhas simuladas do armazenamento (2 falhas recuperadas; 5 deixam um job registado) e do push;
    - três pedidos simultâneos;
    - retenção das denúncias.
  - **No fim:** limpeza, e a média do jogo volta ao valor inicial.

## Gestão e autoria das campanhas (2026-10-17, DeviceTests)

Decisão do utilizador: uma campanha mantém um gestor enquanto tiver outros membros aceites e ativos. Substitui a opção anterior de deixar campanhas sem gestor.

### Significado dos campos (sem migração)

Revi todos os usos de `CreatorId` e `IsCreator`.

| Campo | Significa | Usado em |
| --- | --- | --- |
| `Campaign.CreatorId` | **Quem gere agora.** O nome ficou de quando era sempre quem criou. Muda apenas na eliminação da conta de quem gere. | Permissões em `CampaignService` (remover membros, concluir, abandonar, apagar; quem gere não pode ser removido nem sair; acesso de quem gere); listas e detalhe em `CampaignRepository`; aviso «convite aceite»; `CampaignDto.CreatorId` e `CreatorUserName`, que na app servem só para saber quem gere; sucessão em `UserRepository.AnonymizeAsync` |
| `CampaignMember.IsCreator` | **Quem escreveu a campanha, para sempre**, também como «Conta eliminada». Não dá permissões. | Etiqueta «Criador» na app; o domínio impede que essa pessoa seja removida ou saia; título neutro na eliminação (só quando quem elimina a conta foi quem criou) |

A app mostra «Criador» no autor e «Gestor» em quem gere, quando não é o autor.

### Sucessão quando quem gere elimina a conta

A regra é a mesma na app e pelo apoio, e acontece na mesma transação da eliminação:

1. **Sucessor escolhido:** é validado de novo no momento da eliminação. Se já não for membro ativo, a API responde 400 e a app volta a carregar o resumo.
2. **Sem escolha (sempre, pelo apoio):** fica o membro aceite e ativo há mais tempo. A ordem é por `RespondedAt`, depois `InvitedAt`, depois `UserId` (`CampaignSuccession.Order`). O ecrã mostra o resultado desta ordem antes da confirmação, e a app envia sempre o sucessor que mostrou.
3. **Sem outros membros ativos:** a campanha é apagada.
4. **Estado da campanha:** mantém-se (ativa, concluída ou abandonada).
5. **Aviso ao sucessor:** push «Passaste a gerir…». Não pede aceitação, e uma falha no envio não bloqueia nada.
6. **Concorrência:** a eliminação bloqueia as linhas das campanhas que gere e dos respetivos membros. Sair de uma campanha (`CampaignRepository.LeaveAsync`) bloqueia a linha da campanha antes de verificar quem gere. Assim, um sucessor nunca sai depois de ter recebido a gestão: ou sai antes, e a escolha é recusada, ou a gestão chega primeiro, e sair dá 409.

### Permissões (servidor)

| Operação | Quem |
| --- | --- |
| Remover membros, concluir, abandonar, apagar | Só quem gere (os outros recebem 403). Concluir e abandonar só a partir de «Ativa» (caso contrário, 409). |
| Convidar, editar nome e notas | Membros aceites |
| Retirar uma partida da campanha | Quem a jogou ou quem gere |
| Sair | Membros, exceto quem gere (409) |
| Remover quem gere ou o autor original | Ninguém (409) |

**Notificações:** nenhuma mostra marcadores técnicos. Os textos de convite, convite aceite e remoção usam «uma campanha» quando o nome é neutro. Além disso, `NotificationService` aplica `User.WithoutDeletedMarkers` como última proteção.

### Testes

- **`tests/Campaigns.Tests` (7 testes isolados):**
  - permissões por papel;
  - estados de concluir e abandonar;
  - retirar partidas;
  - sair;
  - textos sem marcadores.
- **`tests/Accounts.Tests` (9 testes):** inclui o aviso ao sucessor.
- **`verify-own-account-deletion.cjs` (SQL real):**
  - sucessor escolhido;
  - sucessor automático por antiguidade e em empate, determinístico entre leituras;
  - campanha gerida por quem não a escreveu (o nome e a autoria ficam);
  - campanha sem sucessores, que é apagada;
  - a mesma regra pelo apoio;
  - permissões depois da sucessão;
  - aviso ao sucessor sem marcadores;
  - sucessor a sair enquanto quem gere elimina a conta, com as duas ordens observadas.

  A limpeza no fim é só do que a execução criou (`DELETE /device-test/account-deletion/fixture?people=…`).

**Dados de teste corrigidos:** `POST /device-test/account-deletion/repair-legacy-campaigns`. Só toca em campanhas cujos membros são todos contas do fixture. Corrigiu as duas campanhas da validação no iPhone:

- a que ficou sem gestor passou para o único membro ativo;
- a transferida devolveu a autoria à conta eliminada.

As contas e as sessões do roteiro não mudaram.
