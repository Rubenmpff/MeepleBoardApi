# Testes no Mac M2 — SQL Server local por emulação

Atualização: DeviceTests tem 25 migrações, incluindo AddExplicitMatchOutcomes (quatro colunas anuláveis, sem preencher registos antigos). Ver [resultados explícitos](resultados-explicitos.md). Base habitual intacta.

Verificado em 6 de outubro de 2026. O utilizador escolheu explicitamente experimentar SQL Server x86-64 em Docker no M2, para desenvolvimento com dados fictícios, aceitando a ausência de suporte Microsoft para emulação. Não foi escolhido outro servidor nem usada a base habitual.

## Continuação — avaliação e participantes

Aplicada apenas na base marcada de testes a 24.ª migração, `PreserveJournalHalfRatings`, que preserva avaliações em meios pontos no diário. Competitivo exige dois participantes distintos e avaliação própria é obrigatória, com zero válido. Ver [contrato, evidência e limitações](avaliacao-participantes.md). As contas/credenciais continuam exclusivamente locais. A revisão visual no iPhone permanece pendente.

## Ambiente validado

- Docker Desktop instalado; CLI 28.3.0, motor Linux aarch64, cerca de 8 GB disponíveis. Foi necessário abrir Docker Desktop.
- SQL Server 2022 Developer, versão 16.0.4295.3. Imagem `mcr.microsoft.com/mssql/server@sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090`, plataforma `linux/amd64`, limite de memória 4 GB.
- Contentor `meepleboard-device-tests-sql`; volume `meepleboard-device-tests-data`; etiqueta `meepleboard.device-tests=true`.
- Porta SQL **127.0.0.1:14339 → 1433**: não exposta à LAN. API no Mac **5099**, Expo **8082**. IPv4 observado: **192.168.1.116**, sujeito a mudança.
- `meeple_sql` e `meepleboardapi_meeple_sql_data` não foram usados nem alterados; contentor habitual observado parado antes/depois. O Compose habitual não foi executado.
- Frontend executado com **Node 22.14.0/npm 10.9.2**, explicitamente pelo PATH do processo. O default da shell permanece diferente. SDK .NET **9.0.318**.

Credenciais aleatórias exclusivas do contentor ficam em `.device-tests/docker.env` e `.device-tests/sql-connection.txt`; imagem fixada em `.device-tests/sql-image.txt`. Diretório 700, ficheiros restritos e ignorados pelo Git. Não apresentar estes ficheiros nem usar `docker inspect` completo: o ambiente do contentor contém a palavra-passe. Não reutilizar segredos/configuração habituais. Os tokens e as passwords das contas fictícias também ficam exclusivamente no diretório ignorado.

## Configuração e proteções

`DeviceTestSql` lê apenas o ficheiro indicado por `MEEPLE_DEVICE_TEST_SQL_FILE`, com endpoint aprovado separado em `MEEPLE_DEVICE_TEST_SQL_SERVER`. Exige base exatamente `MeepleBoard_DeviceTests`, autenticação SQL, encriptação, sem `AttachDbFilename`, `UserInstance` ou persistência de segredos. O endpoint do launcher macOS é exclusivamente `127.0.0.1,14339`. Aceita o certificado autoassinado apenas nesse endpoint local; ligações remotas exigem validação do certificado.

As três ligações do anfitrião derivam da mesma configuração validada: EF, master e base de testes. Mantém-se a auditoria offline, `MigrateAsync` e a marca `MeepleBoardDeviceTestsOwner = MeepleBoardDeviceTestApi-v1`. Uma base preexistente sem a marca correta continua recusada antes das migrações. Não foi enfraquecida a proteção nem usada `EnsureCreated`.

Windows sem configuração externa mantém a ligação LocalDB original. O launcher PowerShell só chama SqlLocalDB quando não há configuração externa explícita. Este percurso foi preservado por revisão de código, mas não foi executado no PC Windows nesta sessão. No Mac sem configuração externa, o arranque é recusado; `--audit-only` pode correr sem SQL.

## Continuação — formulário e pontuações com sinal

Validada a nova criação com pontuação completa, zero e negativos, revisão partilhada e leitura de registos antigos incompletos. Ver [contrato e evidência](pontuacoes-inteiros-com-sinal.md) e [formulário no frontend](../../MeepleBoardApp/docs/formulario-registo-partidas.md). Solo, cooperativo e empate permanecem pendentes para a próxima correção funcional. O ambiente voltou a ficar ativo em API 5099/Expo 8082; voltar a iniciar sessão após reiniciar a API.

## Comandos para reutilizar

No backend, com o Docker Desktop aberto e os ficheiros locais já preparados:

```bash
./scripts/start-device-test-sql.sh
./scripts/start-device-test-api.sh --sql-probe
./scripts/start-device-test-api.sh --audit-only
./scripts/start-device-test-api.sh --verify
./scripts/start-device-test-api.sh --verify-sql=schema
./scripts/start-device-test-api.sh
```

`--verify` arranca temporariamente HTTP, executa cenários integrados com SQL e termina; não o executar enquanto a API ocupa 5099. O último comando mantém a API em primeiro plano. Os scripts .NET usam `--no-restore`; num checkout novo é necessário restaurar primeiro o projeto. O launcher SQL reutiliza apenas o contentor identificado, verifica etiqueta/volume/porta e recusa destinos inesperados; não recria credenciais ou volumes automaticamente.

Noutro terminal, com API ativa, `./scripts/verify-device-tests.sh` executa os verificadores Node e confirma as fixtures via SQL multiplataforma. Acrescenta dados fictícios. Os modos SQL também podem ser chamados individualmente:

```bash
./scripts/start-device-test-api.sh --verify-sql=writes
./scripts/start-device-test-api.sh --verify-sql=dates
./scripts/start-device-test-api.sh --verify-sql=rule
./scripts/start-device-test-api.sh --verify-sql=invites
```

Estes modos apenas leem SQL, validam primeiro a marca e usam parâmetros para os IDs das fixtures. Preservam as verificações de escrita, agregados, datas e convites dos scripts Windows. A opção histórica de verificar uma fixture cancelada permanece no PowerShell; nenhum teste de cancelamento foi executado nesta passagem.

No frontend, noutro terminal, indicar o IPv4 atual do Mac:

```bash
./scripts/start-device-test-expo.sh 192.168.1.116
```

O script exige Node 22.14.0/npm 10.9.2, valida health na LAN e define variáveis só no processo. Não altera `.env`. `MEEPLE_TEST_NODE_DIR` permite indicar outra instalação dessas mesmas versões. Para terminar API/Expo iniciados em terminais próprios: Ctrl+C em cada terminal. Para parar só SQL de testes: `docker stop meepleboard-device-tests-sql`. Também é possível parar API/Expo com `./scripts/stop-device-tests.sh` no frontend, incluindo os processos iniciados pelo agente; `--check` valida sem parar. Este script confirma porta, comando e diretório de trabalho antes de enviar SIGINT, e recusa processos inesperados. Não remover o volume nem parar processos globalmente.

## Evidência desta sessão

- Arranque do contentor e ligação autenticada dentro do contentor e via Microsoft.Data.SqlClient no Mac aprovados; zero reinícios observados.
- Auditoria do modelo/snapshot aprovada; SQL confirmou marca, **23 migrações**, `Matches.CreatorId` e índice filtrado do catálogo.
- **20 cenários SQL/HTTP integrados** aprovados: gravação/releitura, pontuação zero, edição de partida/diário, preços e permissões reais.
- Verificadores HTTP de catálogo, fotografias locais, escrita de sessões, datas, amigo obrigatório e convites aprovados; verificadores SQL de writes/dates/rule/invites aprovados.
- Leitura de login/sessões/campanhas aprovada. O verificador antigo exigia zero sessões para a conta alheio, incompatível com o teste posterior que a convida legitimamente. Foi ajustado para confirmar participação real em cada sessão visível e negar acesso à fixture privada, conservando a ausência de campanhas dessa conta.
- Seis recusas de configuração verificadas sem abrir SQL: ausência de configuração no Mac, base errada, endpoint não aprovado, base anexada, sem encriptação e bypass de certificado remoto.
- TypeScript e **178 testes frontend** aprovados com Node 22.14.0. Sintaxe shell e `git diff --check` aprovados. Aviso AutoMapper/NU1903 continua registado separadamente; nenhuma atualização de dependências.
- Health HTTP pelo IPv4 do Mac aprovado; Metro em 8082 confirmou `packager-status:running`. API/Expo ficaram ativos nesta sessão. O utilizador confirmou posteriormente no iPhone: health DeviceTests / MeepleBoard_DeviceTests / externalDelivery false, Expo pela 8082, login e abertura de Início e Sessões sem erros 401. O registo de partidas numa sessão ativa é o próximo percurso manual, ainda não validado no dispositivo.

Ligação reutilizável no iPhone: na mesma rede Wi-Fi, abrir `http://192.168.1.116:5099/device-test/health` no Safari e confirmar DeviceTests. Só depois abrir Expo Go com `exp://192.168.1.116:8082`. Após reiniciar a API, voltar a fazer login com uma conta fictícia; não transportar tokens do Windows.

Referência Microsoft: [contentores SQL Server e limitações de emulação](https://learn.microsoft.com/en-us/sql/linux/containers/deploy?view=sql-server-ver16). Este resultado confirma este ensaio, não suporte oficial nem estabilidade prolongada.
