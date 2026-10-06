# Sessões: formulário e convites múltiplos

## Organização e componentes

`CreateSessionScreen` tem três secções: **Sobre a sessão** (nome/local), **Quando** (data/hora e prazo automático/personalizado) e **Quem vem** (organizador e amigos). Reutiliza o logótipo original e o sistema visual, com cartões, cores suaves, texto contrastado e áreas de toque amplas. Mantém as datas UTC, o spinner iOS claro, validações, campos após erros e confirmação de saída. Abrir o seletor de data/hora fecha o teclado, sem alterar os valores.

`FriendSelector` é partilhado pela criação e por `InviteSessionFriendsScreen`, através da rota `/games/sessions/invite?sessionId=...`. Pesquisa local nos amigos aceites existentes, sem chamadas ao catálogo ou pesquisa global de utilizadores. Ignora acentos e maiúsculas, conserva a seleção ao pesquisar e apresenta nomes removíveis no resumo. Distingue carregamento, erro/repetição, ausência de amigos e pesquisa sem correspondências. Amigos com vínculo à sessão continuam identificados pelo estado, mas não podem ser selecionados; quando todos já têm convite, há explicação e acesso à área Amigos. Não existe reenvio a quem recusou. Ao voltar da área Amigos, a lista é atualizada sem limpar os campos. Uma seleção cujo amigo desapareceu continua removível, identificada como indisponível. Novas alterações reativam a proteção de saída.

O detalhe apresenta **Convidar amigos** junto a **Quem vem**, apenas para o organizador de uma sessão futura. A lista de participantes continua real, com os estados existentes. Todos recusados mantém a mensagem correspondente. Ao voltar da subpágina, o detalhe consulta novamente a sessão; o carregamento inicial e o retorno foram verificados. Há proteção de saída com seleção por enviar e retorno ao contexto anterior, incluindo fallback para o detalhe se a subpágina for aberta por link direto.

`SessionAttendance` é usado na lista e no detalhe. Conta os vínculos Accepted **incluindo o organizador**, com Pending e Declined separados. Exemplo: organizador e um convidado aceites → **2 confirmados · 0 pendentes · 0 recusados**, com «Inclui o organizador» no detalhe. Deixa de apresentar a fração ambígua 1/1. As mensagens curtas têm PT/EN e plurais, incluindo zero. Os contadores são derivados dos participantes existentes; `AcceptedGuestCount` do backend mantém o significado e contrato anteriores.

## Envio múltiplo e falhas

`sendSessionInvitations` reutiliza o endpoint individual, sequencialmente; **não é uma transação de lote**. Consulta a sessão antes do envio, confirma organizador/estado e não faz POST para quem já possui vínculo. Cada amigo recebe resultado Sent, AlreadyInvited ou Failed; os nomes e resultados ficam visíveis. Apenas os que falharam continuam selecionados após o lote. Os resultados anteriores bem-sucedidos são conservados durante a repetição.

Depois do envio, consulta novamente os participantes. Se um POST falhar na comunicação mas o vínculo existir, identifica «Já tem convite» e não o repete. Se a leitura falhar, mostra um aviso explícito, preserva os resultados confirmados e as seleções que falharam. A tentativa seguinte exige uma nova leitura antes de qualquer POST; não repete às cegas. Um bloqueio síncrono e botões/campos desativados durante o envio impedem duplo toque. Os erros de amizade e indisponibilidade têm mensagens PT/EN por amigo; não são convertidos em sucesso sem vínculo confirmado.

No backend, `GameSessionService.InvitePlayerAsync` exige `IFriendshipRepository.ExistsAcceptedAsync` antes de adicionar o vínculo, tal como a criação. Preserva autorização do organizador, validação do utilizador e rejeição de vínculos existentes, inclusive Declined. A amizade inexistente/não aceite devolve 400; duplicado devolve 409 pelas respostas existentes. O índice único `(SessionId, UserId)` continua a proteger a persistência. Não houve mudança de esquema, migração ou regra de cancelamento.

## Verificações

- TypeScript; **178 testes frontend**, incluindo pesquisa com acentos, resumo/remover (mesmo se o amigo ficou indisponível), bloqueio de já convidados/recusados, secções PT/EN, contadores incluindo organizador, teclado, falhas parciais, repetição só dos falhados, respostas perdidas, falha de leitura e duplo toque. Serviços simulados nos testes de componentes; não equivalem a validação visual.
- Compilação da API e **23 testes isolados de contrato/serviço/mapeamento**, mantendo pontuações/UTC/criação obrigatória e acrescentando amizade/duplicados/recusas/autorização nos convites posteriores.
- **58 testes HTTP de autorização + 1 verificação offline de modelo/migrações**, sem SQL/armazenamento externo.
- `verify-session-invite-friends.cjs` e `verify-session-invite-friends-sql.ps1`: duas sessões fictícias novas, novos convites pendentes, duplicados 409, não amigos 400 sem inserção, recusa seguida de reenvio rejeitado, dados consultados novamente e todos os registos anteriores do autor sem alterações. Não cancelam sessões, não aceitam convites nem criam campanhas/partidas.
- API LAN **192.168.1.83:5099** exclusivamente na base **MeepleBoard_DeviceTests**, com CreatorId e marca de propriedade verificada. Auditoria de modelo/snapshot aprovada. Expo **192.168.1.83:8082** compilou o bundle iOS com a rota, seletor, contadores e API corretos.

As falhas parciais e a perda de resposta são provocadas **nos testes isolados**, sem inventar estados na aplicação. As gravações/rejeições e leitura posterior do endpoint real foram verificadas separadamente com HTTP/SQL. A interface e as condições de rede no dispositivo precisam de confirmação. Não se instalaram dependências; a base habitual e o armazenamento habitual de fotografias não foram usados. NU1903 do AutoMapper permanece preexistente.

Cancelar versus eliminar continua pendente, sem implementação. S04 (prazo personalizado inicial eventualmente passado), proteção das fotografias públicas antigas e ausência de CreatorId no esquema habitual mantêm-se registados. Usar o ambiente descartável nesta validação. Os jobs automáticos continuam desativados nele.

Limitação anterior identificada no código: `GameSessionRepository.GetListAsync` não carrega Matches; o contador de partidas da listagem (`SessionsListScreen`, `GameSessionDto.MatchCount`) deriva da coleção não carregada e pode indicar zero apesar de existirem partidas. Não foi alterado nem considerado resolvido pela correção dos contadores de **pessoas**; confirmar o caso com uma sessão/partida de teste em SQL antes da correção. A limitação de nomes desconhecidos nos DTOs leves também permanece registada.

## Percurso no iPhone

1. Recarregar o projeto **exp://192.168.1.83:8082**, na mesma rede do PC, e voltar a entrar como **autor@meepleboard.test** após o reinício da API.
2. Mais → Sessões → Criar. Nome **Sessão iPhone 04**, local e data/hora futura. Confirmar Sobre a sessão, Quando e Quem vem. Pesquisar **participante**, selecionar **Teste-participante**, limpar a pesquisa e confirmar que a seleção permanece. Guardar e reabrir: **1 confirmado, 1 pendente, 0 recusados**, incluindo o organizador.
3. Junto aos participantes, tocar **Convidar amigos**. Teste-participante já tem convite e fica bloqueado. Selecionar **Teste-membro** e **Teste-alheio**, usando pesquisa e resumo; enviar dois convites. Confirmar resultado por amigo, voltar ao detalhe e reabrir: **1 confirmado, 3 pendentes, 0 recusados**. Ambos os novos convites devem continuar pendentes.
4. Abrir novamente Convidar amigos: os três devem estar bloqueados, com explicação de que já foram convidados. Não cancelar a sessão.
5. Repetir a avaliação visual em inglês e com texto ampliado: títulos, nomes, resultados, botão final e teclado não devem cortar nem cobrir conteúdo.

Se se desejar verificar falha de comunicação no dispositivo, usar uma **nova sessão de teste** com amigos ainda não convidados, carregar a subpágina, selecionar dois e desligar o Wi-Fi antes do envio. Esperar a falha (timeout configurado 15 s), confirmar seleções/aviso e voltar a ligar para repetir. Isto testa falha total; falha parcial controlada já está coberta isoladamente e não deve ser inferida deste percurso. Não modificar dados ou permissões para forçar resultados visuais.
