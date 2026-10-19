# Integração pendente — ramo `feat/game-resources-session-autoclose`

Registado a 2026-10-18. Resolver antes de integrar. A preparação para as lojas **não** está concluída.

## Commits intermédios que não compilam (API)

Verificado em worktrees temporárias com `dotnet build tools/DeviceTestApi`:

| Commit | Compila | Motivo |
| --- | --- | --- |
| `e08f845` | sim | — |
| `4e195dc` | não (2 erros) | Inclui a remoção de `CommunityFixture.cs`, mas `Program.cs` ainda o regista (corrigido só em `e0cbf77`). |
| `921cc99` | não | Igual. Além disso, `community-validation.cjs` precisa de `disposable.cjs`, que só entra em `e0cbf77`. |
| `c5ad970` | não | Igual (só documentação por cima). |
| `e0cbf77` | sim | Fim do ramo: tudo compila e os testes passam. |

**Estado de publicação:** o ramo não tem upstream e nenhum ramo remoto contém estes commits. O histórico não está publicado. Mesmo assim, não se reescreve o ramo nem se faz force push.

## Proposta de entrega

1. **Partir do destino.** Criar um ramo novo de integração a partir do ramo de destino (por exemplo, `integracao/protecoes-comunidade`), sem tocar no ramo atual, que fica como referência até ao fim.
2. **Levar o estado final, não os passos.** Compor poucos commits, cada um completo e a compilar, com `git checkout feat/game-resources-session-autoclose -- <caminhos>`:
   1. **Modelo e migração:** entidades, contexto, migração e auditoria do anfitrião.
   2. **Funcionalidade API:** serviços, controladores, autenticação, filtro e eliminação de conta.
   3. **Testes isolados e ferramentas DeviceTests:** `Community.Tests`, stubs, `DisposableFixture`, verificadores, procedimento do roteiro.
   4. **Documentação.**

   A app segue o mesmo padrão: funcionalidade, testes e pré-visualização, documentação.
3. **Verificar cada commit** antes do seguinte:
   - `dotnet build`;
   - as 11 suites isoladas;
   - `npm test` e `tsc` na app.

   No último commit: os 31 verificadores com dados descartáveis e `roteiro-snapshot.cjs compare` com 0 diferenças.
4. **Entregar por pedido de integração (PR).** Na descrição, juntar:
   - a lista de verificações;
   - as migrações, que só adicionam colunas anuláveis e tabelas novas:
     - `20261018075754_AddBlocksReportsAndSuspension`;
     - `20261018163245_AddPublicationPhotos` (fotografias publicadas, removidas com a publicação).

   Em alternativa, uma integração por squash produz um único commit verificável.
5. **Depois da integração,** apagar o ramo antigo só com a tua autorização.

## Continua pendente

- **Contacto de apoio:** endereço real por indicar. Não se inventa nenhum.
- **Verificadores PowerShell:** os `verify-session-*-sql.ps1` (Windows) não foram convertidos nem corridos.
- **Validação no iPhone:** roteiro em `MeepleBoardApp/docs/validacao-comunidade-iphone.md`.
- **Preparação para as lojas:** não concluída.

## Última verificação completa (2026-10-19, DeviceTests)

Verificado com a API sem o diagnóstico temporário, que foi removido.

- **API:** 11 suites isoladas.
- **Verificadores:** os 31 verificadores, todos com pessoas descartáveis e limpeza confirmada.
  - Correu tudo bem, exceto `verify-roteiro-cleanup`. A expectativa estava desatualizada (faltava a tabela `PublicationPhotos`); foi corrigida e voltou a passar.
- **App:** `npm test` (403) e `tsc`.
- **Roteiro:** `roteiro-snapshot.cjs compare` com 0 diferenças.
- **Conteúdo das contas de validação:** idêntico antes e depois.
