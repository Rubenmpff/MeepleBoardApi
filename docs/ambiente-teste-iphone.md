# Ambiente descartável para testar no iPhone

**Atualização macOS:** SQL Server local por emulação no M2 e scripts shell foram validados; ver [ambiente macOS](testes-macos-docker.md). Os comandos Windows abaixo permanecem como referência desse ambiente. UPD01/JRN01 foram corrigidas em etapas posteriores, descritas em `estabilizacao-escrita-sessoes.md`.

## Isolamento e esquema

A API de testes usa exclusivamente a instância LocalDB `MeepleBoardDeviceTests` e a base `MeepleBoard_DeviceTests`. A ligação é fixa no anfitrião de testes; este não lê a configuração, segredos ou ligação da API habitual. Uma marca de propriedade impede aplicar migrações a uma base preexistente desconhecida. Não foi consultada nem alterada a base habitual.

Foram aplicadas 23 migrações à base descartável, incluindo `AddMatchCreator`. O backend atualizado não suporta os percursos completos com o esquema habitual sem `Matches.CreatorId`; testar esta versão exige o ambiente descartável ou uma futura atualização de esquema autorizada.

A divergência SC01 foi resolvida no snapshot e numa migração exclusivamente de criação do índice filtrado `IX_GameSearchCatalog_RatingsCount_BggRank_AverageRating_Name_BggId`. A migração foi colocada depois de `AddMatchCreator`. A comparação modelo/snapshot não apresenta diferenças. O esquema habitual permanece por verificar/atualizar; não foi aplicado nada nele. A proteção das fotografias públicas antigas (PR02) permanece pendente, sem alterações ao Cloudinary.

O anfitrião usa os controladores, serviços, Identity e repositórios reais. Substitui apenas integrações externas: emails ficam em `.device-tests/outbox`, fotografias em `.device-tests/photos`, catálogo em SQL com três jogos fictícios; notificações e trabalhos em segundo plano estão desativados. Não existem envios reais nem operações no armazenamento habitual de fotografias.

## Arranque

As portas são separadas: API de testes **5099**, Expo de testes **8082**. A API e o Expo habituais podem continuar a executar. Os scripts não alteram `.env`, `appsettings` ou perfis de execução habituais.

1. Num terminal PowerShell, iniciar a API:

```powershell
cd C:\Users\ruben\Desktop\ProjectoJogos\MeepleBoardApi
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-device-test-api.ps1
```

2. Noutro terminal, iniciar o Expo:

```powershell
cd C:\Users\ruben\MeepleBoardApp
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-device-test-expo.ps1 -ApiHost 192.168.1.83
```

O script Expo confirma primeiro a identidade da API e define variáveis apenas para o processo. Sem `-ApiHost`, deteta o IPv4 da interface com gateway. Se o endereço do PC mudar, usar o novo endereço também no iPhone e nas regras de firewall.

3. No iPhone, na mesma rede, abrir `http://192.168.1.83:5099/device-test/health` no Safari: deve apresentar `DeviceTests`. **Esta ligação foi confirmada pelo utilizador no iPhone.** Abrir o QR do terminal no Expo Go ou usar `exp://192.168.1.83:8082`. A aplicação aponta para `http://192.168.1.83:5099/MeepleBoard`.

4. Consultar localmente `C:\Users\ruben\Desktop\ProjectoJogos\MeepleBoardApi\.device-tests\accounts.json`. Contém as palavras-passe geradas das contas `autor@meepleboard.test`, `participante@meepleboard.test`, `alheio@meepleboard.test` e `membro@meepleboard.test`. Não partilhar nem versionar esse ficheiro. Terminar a sessão anterior e entrar com uma conta de teste. Os tokens são exclusivos deste processo; depois de reiniciar a API, voltar a entrar. Não usar contas habituais.

Se os serviços já estiverem ativos, usar o endereço existente; não iniciar outro Expo na mesma porta. Para parar exclusivamente os processos deste ambiente, preservando dados fictícios e os serviços habituais:

```powershell
cd C:\Users\ruben\MeepleBoardApp
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\stop-device-tests.ps1
```

As regras de firewall já foram preparadas para as portas 5099 e 8082, limitadas à interface/endereço do PC, programas DeviceTestApi/node e rede local. Se necessário, executar em PowerShell como administrador:

```powershell
cd C:\Users\ruben\MeepleBoardApp
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\enable-device-test-firewall.ps1 -ApiHost 192.168.1.83
```

Não existe eliminação automática da base. Arranques e verificações preservam/adicionam apenas dados fictícios nesta base.

## Verificações repetíveis

Com a API de testes parada, no repositório backend:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-device-test-api.ps1 -AuditOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-device-test-api.ps1 -Verify
```

`-AuditOnly` compara modelo/migrações sem abrir SQL. `-Verify` migra apenas a base identificada de testes, arranca HTTP, verifica persistência com SQL real e termina. As verificações deixam exemplos fictícios para inspeção. Para catálogo e fotografias, com a API de testes ativa:

```powershell
node tools/DeviceTestApi/verify-local-photos.cjs
```

Resultados desta etapa: esquema completo validado; 20 cenários SQL/HTTP verificados, **18 com comportamento correto e dois reproduzindo falhas funcionais**, descritas abaixo; cinco cenários de catálogo/fotografias aprovados; 58 testes HTTP isolados e uma comparação offline de modelo aprovados; TypeScript e 147 testes frontend aprovados. Os testes não substituem a validação visual no dispositivo.

## Falhas confirmadas com SQL real — não corrigidas nesta etapa

- **UPD01:** o criador atualiza uma partida, a alteração persiste, mas recebe 404. `MatchRepository.UpdateAsync` já guarda e `MatchService.UpdateAsync` volta a guardar, obtendo zero; `MatchController` interpreta-o como inexistência. Rever quem controla `SaveChanges` e testar atualização autorizada, recarga e negação a outros utilizadores.
- **JRN01:** editar a própria contribuição no diário persiste e depois devolve 500 ao recalcular avaliações. `CampaignService.UpsertJournalEntryAsync`, `GameRepository.UpdateAsync` e `CampaignRepository` participam num conflito de tracking de `MatchJournalEntry`. A contribuição de outro autor permanece intacta. Rever tracking e atomicidade; verificar notas/avaliações/agregados após sucesso e falha. Os testes registam a falha, não a consideram corrigida.

Não repetir automaticamente pedidos destas operações perante erro: a primeira gravação pode já ter ocorrido. Nenhuma destas verificações usou dados pessoais.

## Roteiro no iPhone

- Entrar com a conta autor; testar Mais → Pesquisar jogos e Biblioteca → Adicionar jogo, usando os jogos «Meeple Teste». Distinguir pesquisa do catálogo e pesquisa da coleção.
- Guardar preço vazio, `35,50`, `0`, editar e apagar; sair e recarregar a entrada. Confirmar total conforme os estados existentes. Remover da Biblioteca deve preservar jogo e partidas.
- Registar partida com participante e pontuação zero; voltar ao histórico/detalhe. Consultar com autor e participante: notas privadas do próprio, avaliações/tags/fotografias partilhadas; alheio e membro de sessão/campanha sem participação não acedem à partida.
- Testar fotografia fictícia/local, teclado, textos ampliados, português/inglês, retorno/cancelamento e mensagens de erro. A atualização de partida e edição do diário têm as limitações UPD01/JRN01 acima.
- Testar entrada, saída e novo login após reiniciar a API. A ligação de rede está confirmada; a experiência visual completa e todos os percursos no iPhone continuam pendentes.
