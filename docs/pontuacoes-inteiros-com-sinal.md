# Pontuações competitivas — inteiros com sinal

Implementado e validado em 6 de outubro de 2026 apenas no ambiente **DeviceTests / MeepleBoard_DeviceTests**, SQL Server local em Docker por emulação no Mac M2. Base/contentor habitual intactos. Sem alterações de dependências ou migração nova.

## Contrato e validação

`Score` continua `int?` em .NET e SQL `int` anulável: intervalo `-2147483648..2147483647`. Foram removidas as recusas de negativos do serviço, entidade e anotação do DTO de leitura. O parser JSON de `int?` continua a rejeitar decimais e overflow. Zero não equivale a ausência de valor.

`CreateMatchDto.ScoresEnabled` é anulável, para compatibilidade com clientes que omitem a opção:

- `true`: todos os participantes finais precisam de uma entrada de pontuação não nula, incluindo o autor acrescentado pelo percurso rápido quando aplicável.
- `false`: o pedido não pode conter entradas de pontuação.
- omitido: pedidos sem valores continuam válidos; se existem valores numéricos, exige cobertura completa. Listas antigas sem qualquer valor numérico preservam o comportamento sem pontuação.

Continuam recusados participantes inexistentes na lista, IDs vazios, entradas nulas e pontuações duplicadas para o mesmo participante. A validação de cobertura precede lookup/import do jogo e gravações. Vencedor não é deduzido pelas pontuações. Mantidas participação obrigatória, convites aceites, sessão ativa e autorização de leitura/diário/fotografias.

Só a criação aplica a regra de completude. Partidas antigas incompletas continuam a ser lidas sem alterar valores ou preencher null com zero; não foi executado qualquer backfill. Não existe uma coluna nova para a opção: ela valida o pedido; ausência de pontuações mantém os valores null.

## Limitações — RESULT01, próxima etapa funcional

Solo ainda perde `winnerId` no mapper frontend; cooperativo não persiste modo/resultado de equipa e a API continua a exigir vencedor não solo. Marcação de equipa pode ser reduzida ao primeiro jogador marcado pelo mapper atual. Empates não têm representação explícita. Esta alteração não corrige essas regras nem interpreta resultados em falta. Ver [formulário e pendências no frontend](../../MeepleBoardApp/docs/formulario-registo-partidas.md).

## Evidência

- 34 testes isolados de serviço/contrato: negativos e limites, completude, ator obrigatório, vencedor com pontuação inferior e leitura de registo incompleto.
- 59 testes HTTP de autorização + uma auditoria offline aprovados. A fixture de criação passou a enviar pontuação para ambos os jogadores, mantendo verificações de notas privadas/ator JWT.
- Auditoria modelo/snapshot sem diferenças; SQL confirma 23 migrações, CreatorId e índice filtrado. Não é necessária migração para aceitar negativos em `int`.
- 26 cenários HTTP/SQL reais aprovados em base marcada: rápido/sessão com -17 e zero, vencedor explícito com pontuação inferior, extremos int32, sem pontuação e resultado manual, leitura incompleta, sete pedidos inválidos recusados sem aumentar o número de partidas e privacidade.
- Verificador de detalhes da sessão aprovado; cinco cenários de catálogo/fotografias locais aprovados, incluindo leitura protegida e remoção só pelo autor.
- Frontend: TypeScript, 200 testes em Node 22.14.0, bundle iOS em Expo 8082. API reiniciada na porta 5099; tokens por processo exigem novo login.
- Contentor habitual `meeple_sql` observado parado; SQL de testes usa apenas `meepleboard-device-tests-data`. Credenciais, fotografias e fixtures continuam locais/ignoradas.

Avisos de AutoMapper/npm mantêm-se na análise separada, sem atualizações automáticas.
