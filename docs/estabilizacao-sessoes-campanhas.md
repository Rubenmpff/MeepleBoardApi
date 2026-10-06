# Estabilização — etapa 1: login e leitura

Verificação em 6 de outubro de 2026. API de testes ativa na porta 5099, Expo de testes na 8082; existe também Expo habitual na 8081. Nenhum serviço habitual foi parado. A aplicação de testes usa `http://192.168.1.83:5099/MeepleBoard`, com SQL LocalDB `MeepleBoardDeviceTests` / `MeepleBoard_DeviceTests`. Uma consulta de metadados exclusivamente nesta base confirmou `Matches.CreatorId`.

## Resultado

Não foi reproduzida uma falha de login ou leitura que justifique alterar código de produção nesta etapa. Foi acrescentada uma verificação repetível no backend: `node tools/DeviceTestApi/verify-session-campaign-reading.cjs`, com a API descartável ativa. Usa o pedido exato de login do frontend, valida os campos de resposta necessários e o identificador JWT usado pelo Redux, sem imprimir credenciais ou tokens.

Com SQL real, as quatro contas fictícias autenticaram. Foram consultados última partida, pendências, listagens e todos os detalhes existentes de sessões e campanhas. Autor, participante e membro obtiveram quatro sessões e quatro campanhas; a conta alheia recebeu listas vazias e acesso negado aos detalhes do autor. Visitantes não podem consultar listagens/detalhes. As respostas dos contentores não expõem as notas pessoais usadas nas fixtures. Ausência de última partida devolve 404 e pendências vazias 204, ambos já tratados pelo frontend.

Esta verificação confirma HTTP e persistência existente, não gestos/navegação nem experiência visual no dispositivo. Não cria sessões/campanhas nem testa novos registos nesta etapa. As falhas UPD01 (atualizar partida guarda mas devolve 404) e JRN01 (editar diário guarda mas pode devolver 500) permanecem abertas para a próxima etapa, após confirmação do percurso no iPhone. Não foram removidas permissões ou ocultados erros.

## Confirmação no iPhone antes de avançar

1. Abrir `exp://192.168.1.83:8082` no Expo Go, evitando a instância habitual 8081.
2. Terminar uma sessão antiga se necessário. Consultar a palavra-passe gerada em `C:\Users\ruben\Desktop\ProjectoJogos\MeepleBoardApi\.device-tests\accounts.json`; entrar como `autor@meepleboard.test`.
3. Confirmar login → Início. Abrir Mais → Sessões e abrir uma «Sessão fictícia SQL». Voltar a Mais → Campanhas e abrir uma «Campanha fictícia SQL».
4. Confirmar ausência de erros, listas/detalhes carregados e retorno correto. Se falhar, registar ecrã, ação e mensagem exata, sem enviar palavras-passe/tokens.

Depois desta confirmação, avançar para criação, convites, registo em sessão/encontro, pontuações, resultados e diário, incluindo correção e regressão de UPD01/JRN01. [Arranque do ambiente e limitações](ambiente-teste-iphone.md).
