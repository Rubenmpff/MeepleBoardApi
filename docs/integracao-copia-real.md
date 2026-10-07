# Integração principal e cópia isolada — preparação, 07/10/2026

## Estado confirmado

- Branch `fix/session-invite-accepted-friends`. API ativa em 5099: `DeviceTests`, base `MeepleBoard_DeviceTests`, `externalDelivery=false` (health consultado nesta revisão).
- O anfitrião DeviceTests carrega os controllers da API principal e reutiliza `AddInfrastructure`, Identity, EF, serviços e repositórios reais. As Estatísticas (`/MeepleBoard/statistics/me` e `/matches`) estão na API principal e registadas no IoC habitual; não dependem de endpoints privados de DeviceTests.
- Health, preparação/semente de dados fictícios, JWT efémero e substitutos externos pertencem ao anfitrião de testes. Email é guardado em outbox local, push não envia, fotografias são locais, BGG usa dados locais e não há processamento Hangfire.
- `Program.cs` habitual ativa Hangfire, regista tarefas de limpeza de utilizadores/sessões/partidas e sincronização BGG. Development/QA criam utilizadores de teste; roles são criadas em todos os ambientes. O programa não aplica `Database.Migrate` no arranque.
- NÃO apontar DeviceTests à cópia: exige o nome/marcador da base fictícia e executa sementes. NÃO apontar já o arranque habitual à cópia: os workers podem consumir tarefas restauradas e executar limpezas/entregas.
- A configuração habitual local foi inspecionada sem mostrar valores sensíveis: User Secrets da API no Mac apontam para SQL em `localhost:1433`, base `MeepleBoardDb`, e contêm JWT. Não contêm as chaves planas externas verificadas (Brevo/BGG/Cloudinary/dashboard). Isto descreve o destino configurado neste Mac, não prova onde estão os dados reais no Windows. O Git não contém a ligação habitual do PC. LocalDB aparece no fallback do anfitrião de testes Windows, não na ligação habitual encontrada no Mac.
- O utilizador confirmou que trouxe código pelo GitHub, sem transferir a base. A transferência fica para quando tiver acesso ao PC Windows. No PC, identificar DefaultConnection efetiva (incluindo User Secrets/variáveis) sem divulgar credenciais; exportar backup consistente dessa base, não da DeviceTests.
- Nenhuma restauração, migração ou ligação à base original foi feita. Não foi encontrado backup `.bak`, `.bacpac`, `.mdf` ou `.ldf` neste workspace. Isto não prova ausência noutros locais do Mac.

## Configuração e serviços a preparar

| Área | Contrato atual | Preparação necessária fora de DeviceTests |
|---|---|---|
| SQL | `ConnectionStrings:DefaultConnection` (vazia no appsettings versionado) | Servidor acessível no Mac; ligação exclusiva à cópia, sem LocalDB Windows; identidade SQL sem acesso à original; nome/volume/porta separados do ambiente fictício. Não escolher/restaurar antes de conhecer origem e compatibilidade do backup. |
| JWT | `JWT_KEY` mínimo 32 caracteres; `Jwt:Issuer`, `Jwt:Audience` | Chave própria persistente só local e issuer/audience explícitos: o emissor tem defaults, mas Identity valida valores vindos da configuração. Separar sessões/tokens dos restantes ambientes. |
| Identity | tokens de confirmação/recuperação e Data Protection | Diretório de chaves privado persistente, próprio do ambiente. Não reutilizar JWT/refresh tokens originais. Verificar login com a conta copiada sem redefinir a palavra-passe original. |
| Email | `Brevo:ApiKey`, `Email:SenderEmail`, `Email:SenderName` | Credenciais e remetente válidos para a integração completa. Na cópia, capturar emails ou encaminhar exclusivamente para destinatários de teste; nunca enviar para os emails restaurados. |
| Fotografias | `CLOUDINARY_CLOUD_NAME`, `CLOUDINARY_API_KEY`, `CLOUDINARY_API_SECRET` | O serviço lê estas chaves planas: a secção `Cloudinary:*` do appsettings não as substitui. Fotos antigas podem referenciar recursos do cloud original; copiar a base não copia imagens. Preparar leitura e armazenamento de teste, impedindo apagar/alterar recursos originais. Falta de credenciais só é detetada ao usar o serviço. |
| Catálogo | `Bgg:BaseUrl`, `Bgg:Token`, `Bgg:CatalogPageUrl` | Configurar e verificar consultas reais, erros/retries e importação separadamente. Estatísticas e histórico usam o catálogo SQL, sem exigir chamadas BGG para cada cálculo. |
| Push | serviço HTTP Expo, tokens guardados no utilizador | Bloquear entregas na cópia; posteriormente validar tokens/dispositivos de teste, permissões e build adequado. Tokens restaurados não podem receber notificações. |
| OAuth | `Authentication:Google:*`, `Authentication:Apple:*` | Providers só são registados se as chaves estiverem preenchidas. Confirmar quais fluxos são utilizados, aplicações/redirects/deep links próprios e credenciais locais; não declarar OAuth validado pelo login com palavra-passe. |
| Hangfire | SQL storage + workers + dashboard | Perfil seguro para a cópia ainda por implementar: sem workers, sem consumo de jobs restaurados, sem sementes e sem entregas externas. Só ativar tarefas num ensaio posterior controlado. Development/QA exigem credenciais de dashboard com password >=16 caracteres. |
| App / rede | `EXPO_PUBLIC_API_MODE`, `EXPO_PUBLIC_API_PORT`, `EXPO_PUBLIC_API_BASEPATH`; URLs tunnel/QA/production | Apontar uma sessão Expo separada à API da cópia e terminar a sessão anterior. Não mudar silenciosamente a app 8082 que usa dados fictícios. No iPhone, localhost é o telefone: usar host LAN ou URL HTTPS adequado. |
| Links de autenticação | `meepleboard://confirm-email`, `meepleboard://reset-password`; URLs web `meepleboard.com` fixos em AuthService | Tornar destinos configuráveis e validar retorno no build/ambiente escolhido. Não assumir que o esquema nativo se abre no Expo Go nem que o site web serve as rotas. |
| Pipeline principal | CORS, rate limiting, auto-refresh e LastActive | Não presentes da mesma forma em DeviceTests. Validar no perfil principal (incluindo comportamento de 401/refresh e escrita LastActive). O rate limiting está ativo no código apesar do comentário dizer desativado. |

Os appsettings versionados não provam quais segredos existem no Mac/PC/serviço remoto. Não foram apresentados nem extraídos segredos. Chaves, backup, logs com dados pessoais e credenciais devem ficar apenas em armazenamento local ignorado, com acesso restrito.

## Migrações

Há 25 migrações no repositório. O conjunto necessário na cópia só pode ser conhecido comparando `__EFMigrationsHistory` do backup com o código.

Entre as recentes: `AddMatchCreator`, `AddCatalogPendingDetailsIndex`, `PreserveJournalHalfRatings` (inteiros nullable → float nullable) e `AddExplicitMatchOutcomes` (colunas nullable, sem reclassificação dos registos antigos). Estatísticas não acrescentaram outra migração.

Não executar `database update` contra a ligação habitual. Rever SQL das migrações pendentes e os custos de índices/catalogação na cópia. Os Downs de avaliações/resultados recusam conversões destrutivas; recuperação planeada por nova restauração da cópia, não por downgrade automático.

## Sequência concreta quando houver backup

1. Identificar origem, formato, versão SQL, histórico de migrações, tamanho, encriptação e disponibilidade dos recursos externos. Receber uma cópia consistente, sem alterar a original. Confirmar compatibilidade de restauro antes de escolher o destino; não assumir que um `.bak` é intercambiável entre versões SQL.
2. Criar destino isolado para `MeepleBoard_RealCopy`, com volume/porta/credenciais próprios e sem acesso à original. Não reutilizar `MeepleBoard_DeviceTests`. Preservar o backup como entrada só de leitura e registar hash/tamanho localmente.
3. Restaurar **sem `WITH REPLACE`**, redirecionando os ficheiros da base para caminhos exclusivos. Não arrancar API nem Hangfire ainda. Auditoria inicial só de leitura com `scripts/audit-real-copy.sql`, executada com esta base selecionada; recusa outro nome. Sem listar utilizadores, passwords, tokens ou notas.
4. Comparar e rever migrações pendentes; guardar contagens anteriores. Aplicar só à cópia, auditar novamente e confirmar preservação dos resultados legados, zeros/meios pontos, referências e privacidade. Não carregar fixtures fictícias ou executar seeders.
5. Implementar/validar o perfil isolado baseado no programa principal, com guardas de base e bloqueio de jobs/email/push/alterações ao Cloudinary original. Este perfil ainda não está implementado; o runbook não é autorização para usar os arranques existentes com uma ligação diferente.
6. Testar login/refresh, Início, Biblioteca, sessões, campanhas, detalhe/histórico e Estatísticas. Comparar cálculos com partidas conhecidas da cópia. Testar gravação/releitura apenas na cópia, com resultados novos identificados como ensaio; verificar que o backup original permanece igual.
7. Ensaiar serviços externos com recursos e destinatários de teste. Registar separadamente funções só locais, leitura de recursos antigos e integrações externas efetivamente verificadas. Só depois preparar ligação habitual numa decisão explícita; nunca promover esta cópia misturada com ensaios como base original.

## Bloqueios atuais / informação necessária

- Caminho e disponibilidade de uma cópia/backup; formato e versão SQL de origem (o acesso ao PC Windows esteve indisponível anteriormente).
- Local do ambiente habitual e migrações que efetivamente tem aplicadas.
- Quais serviços externos estão já configurados e se existem contas/recursos separados para ensaio. Basta identificar disponibilidade: não enviar segredos pela conversa.
- Perfil principal seguro para a cópia, links configuráveis e validação das fotografias existentes: trabalho identificado, ainda não concluído.
- Sem backup não é possível confirmar compatibilidade, executar migrações da cópia, validar dados históricos reais ou afirmar integração completa. A preparação e a validação fictícia não substituem esse ensaio.

## Execução futura no Windows

As funcionalidades são partilhadas no backend principal, não é necessário portar os controllers de DeviceTests. Fazer pull das branches de trabalho sem merge implícito, confirmar SDK 9.0.318/Node 22.14.0 e identificar a configuração efetiva do PC. LocalDB é viável no Windows se já for a origem habitual; Docker é apenas outra configuração SQL, não uma dependência das Estatísticas.

Primeiro criar backup e cópia isolada no Windows ou transferir backup para destino Mac compatível. Comparar o histórico de migrações, gerar/rever o SQL pendente e aplicar à cópia. O principal não aplica migrações automaticamente. Manter desativados jobs, sementes e entregas na cópia antes de ensaiar o arranque principal. Validar as mesmas rotas `/MeepleBoard/statistics/me` e `/matches`, autenticação, privacidade e histórico antigo. Só depois planear atualização habitual com backup e decisão explícita; esta etapa não a executa.
