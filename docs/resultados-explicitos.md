# Modos e resultados explícitos — regras aprovadas

## Estado atual da validação no iPhone — 7 de outubro de 2026

A pedido explícito do utilizador, ficam **pendentes** os testes de Solo com empate, Solo com resultado não definido e **todos os resultados cooperativos** (vitória, derrota, empate e não definido). Esta marcação substitui as confirmações breves anteriormente interpretadas como validação desses percursos. Os testes automatizados não substituem esta confirmação.

Confirmados de forma explícita: empate competitivo parcial, vitória partilhada competitiva, Solo com vitória, Solo com derrota, pontuação zero e negativa, avaliação zero e 7,5/meios pontos, estrelas douradas legíveis e releitura dos percursos confirmados. Não generalizar estas confirmações a todos os ecrãs/estados nativos.

Etapa atual: **duas propostas visuais de autenticação para escolher antes de implementar**. Sem alteração dos ecrãs, regras, API ou base. Estatísticas e retrospetiva «O teu ano à mesa» continuam como etapas seguintes de análise/proposta, não autorizadas para implementação nesta etapa.


Implementação funcional em DeviceTests. Validação física no iPhone pendente. A base habitual não foi alterada. A avaliação zero e o dourado das estrelas continuam à espera de confirmação visual.

| Modo | Participantes | Resultado | Seleção |
|---|---|---|---|
| Competitivo | Pelo menos dois distintos | Vitória, empate ou não definido | Vitória: um ou vários vencedores; vários exigem confirmar que as regras permitem vitória partilhada. Empate: selecionar pelo menos dois empatados no primeiro lugar; os restantes têm derrota. |
| Solo | Exatamente um, fora das sessões | Vitória, derrota, empate ou não definido | Resultado do jogador, sem participante fictício «jogo». |
| Cooperativo | Pelo menos dois distintos | Vitória, derrota, empate ou não definido da equipa | Aplica-se a todos; sem vencedor individual. |

O modo não muda automaticamente pelo número de jogadores. A pontuação nunca decide o resultado. Vitória partilhada é confirmada pelo autor segundo as regras do jogo; o catálogo não verifica essa autorização. Equipas adversárias e semi-cooperativo continuam fora deste contrato.

## Contrato e histórico

`gameMode`: COMPETITIVE/SOLO/COOPERATIVE. `result`: Win/Loss/Draw/Undefined. `resultPlayerIds` identifica vencedores ou jogadores empatados no primeiro lugar, apenas no competitivo. `sharedVictoryAllowed` confirma vitória partilhada. Cada participante tem `outcome`; a leitura devolve `winnerIds` e `resultSource` (Explicit/Legacy).

`winnerId` serve apenas de compatibilidade quando há exatamente um vencedor individual; não escolhe o primeiro vencedor partilhado nem um membro da equipa. Atualizar um registo explícito com um pedido antigo sem resultado conserva o resultado existente. Alterações de resultado continuam exclusivas do autor.

A migração adiciona quatro colunas anuláveis, sem valores por defeito ou preenchimento histórico. Conserva WinnerId, IsWinner e IsSoloGame antigos; não deduz modo pelo catálogo nem derrota/empate pela ausência de vencedor. Os leitores distinguem resultado não definido, resultado antigo insuficiente e nome do vencedor indisponível. Um vencedor antigo registado pode ser mostrado, mas não transforma todo o registo numa classificação explícita.

## Formulário, detalhes e cálculos existentes

Jogo → Jogadores → Resultado → Rever mantém modo visível, seleções compactas, revisão por jogador e correção por secção. Conserva inteiros com sinal, zero, avaliação pessoal obrigatória de 0–10 em meios pontos, datas e rascunhos, saída protegida e o percurso confirmação → detalhes → sessão. Sem pontuação, os valores são omitidos. Não cria avaliações para outros jogadores.

Detalhes, lista da sessão, histórico do jogo, última partida e histórico com amigos leem resultados explícitos. Vitória partilhada identifica todos os vencedores; empate parcial distingue empates e derrotas. Os filtros de privacidade continuam no servidor.

Os cálculos existentes contam Win/Loss/Draw por participante explícito. Taxa de vitória = vitórias / resultados conhecidos, incluindo empates no denominador. Undefined e registos antigos sem resultados explícitos ficam excluídos, com contagens separadas; sem resultados conhecidos, a taxa é ausente. Uma vitória da equipa conta uma vez por pessoa. O histórico por jogo com amigos indica o tamanho da amostra carregada, limitada a 200 registos. Maior pontuação registada não significa melhor pontuação: o sentido da pontuação continua por definir.

A nova área de Estatísticas, retrospetiva «O teu ano à mesa», propostas visuais de autenticação e restantes redesigns permanecem nas pendências e não foram implementados. Nessas etapas, duração ausente não deve valer zero; rankings de taxas devem indicar a amostra e respeitar privacidade.

## Verificação e percurso no iPhone

Verificações concluídas: TypeScript, 228 testes frontend, 72 testes de contrato/serviço, 59 testes HTTP de autorização e uma auditoria offline, 46 cenários reais SQL/HTTP, seis verificações de esquema e cinco cenários de fotografias locais. Node 22.14.0; API 5099 e Expo 8082 em DeviceTests. Estes testes cobrem os quatro resultados Solo/cooperativo, vitória/empate/não definido competitivo, vitória partilhada, pontuações negativas/zero, avaliação zero, autoria e leituras privadas. A migração é a 25.ª deste ambiente; não autoriza migração da base habitual.

Começar por uma sessão com três jogadores: selecionar empate, indicar dois empatados no primeiro lugar, rever e guardar. Na confirmação e nos detalhes devem aparecer dois empates e uma derrota, independentemente das pontuações. Sair e reabrir. Depois testar vitória partilhada; no registo rápido, Solo; por último cooperativo e resultado não definido, um percurso de cada vez. Não considerar concluída a validação visual antes da confirmação no iPhone.
