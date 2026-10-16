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
| Nome, email, telefone, palavra-passe, fotografia, token de push, 2FA, acessos externos, tokens, claims e perfis (a conta fica bloqueada para sempre) | A linha da conta, com o nome `Conta eliminada xxxxxxxx` e `DeletedAt` |
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
- que o jogador anónimo continua na partida;
- que a denúncia continua a contar;
- que as médias voltam ao valor inicial no fim.

## Limitações conhecidas

- **Token de acesso já emitido.** Continua válido até expirar (`Jwt:ExpiryHours`, 24 h). A conta não tem dados nem sessão nova, mas, com esse token, ainda pode chamar a API até lá. A correção proposta é recusar o token em `JwtBearerEvents.OnTokenValidated` quando `DeletedAt` está preenchido. Custa uma consulta por chave primária em cada pedido autenticado, por isso fica para decisão.
- **Nome da conta eliminada.** Está guardado em português (`Conta eliminada …`). Em inglês aparece igual.
- **Pedido de eliminação pela própria pessoa.** A app ainda não o tem. Hoje só um administrador elimina contas.
