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
