# Organização do código — API

Convenção em vigor desde 2026-10-17, a do projeto original, aplicada também a Experiences, GameDurations e Statistics. A convenção da app está em `MeepleBoardApp/docs/organizacao-codigo.md`.

| Pasta | O que contém | Regra |
| --- | --- | --- |
| `MeepleBoardApi/Controllers` | Pedidos HTTP, autorização (`[Authorize]`, perfis) e respostas | Delegam a lógica num serviço através da interface. Os pequenos records de pedido podem ficar no controlador. |
| `MeepleBoard.Services/Implementations` | Serviços que coordenam operações | Um serviço por ficheiro, sempre com a interface correspondente. Não usam o `DbContext`: leem e gravam através dos repositórios. |
| `MeepleBoard.Services/Interfaces` | Interfaces dos serviços | Uma por serviço. |
| `MeepleBoard.Services/Calculators` | Calculadoras e auxiliares puros: `GameDurationCalculator`, `StatisticsCalculator`, `CompanyCalculator`, `JournalPhotoReference` | Estáticos, sem base de dados nem chamadas externas. Não têm interface. |
| `MeepleBoard.Services/Mapping/Dtos` | DTOs, um ficheiro por área (pode ter vários tipos) | Namespace `MeepleBoard.Services.DTOs` nos ficheiros novos. Os namespaces antigos `MeepleBoard.Services.Mapping.Dtos`, `…DTOs.Campaign` e `…DTOs.MatchJournal` mantêm-se. |
| `MeepleBoard.Services/Mapping/AutoMapper` | Perfis AutoMapper | Os tipos auto-referenciados levam `MaxDepth`; ver a análise do NU1903. |
| `MeepleBoard.Services/{ExternalServices,Job,Validator,Settings}` | Integrações externas, jobs, validadores e configurações | Mantêm-se como estavam. |
| `MeepleBoard.Domain/Entities` | Entidades e regras próprias do domínio, como `MatchOutcomeRules`, a avaliação fechada e a anonimização | Uma entidade por ficheiro. |
| `MeepleBoard.Domain/Interfaces` | Contratos dos repositórios | As projeções de leitura (`*Projection`, `StatisticsRow`, `DurationRow`) ficam no ficheiro da interface que as devolve. |
| `MeepleBoard.Infra.Data/Repositories` | Consultas e persistência | As operações que gravam várias tabelas usam uma transação, dentro da estratégia de execução do EF (ver `UserRepository.AnonymizeAsync`). |
| `MeepleBoard.Infra.Data/{Context,Migrations}` | `DbContext`, configuração e migrações | Mantêm-se no sítio. |

**Ferramentas:**

- `tools/DeviceTestApi`: anfitrião e verificadores DeviceTests.
- `tools/StatisticsChecks`: verificações das calculadoras, que agora referenciam o projeto de serviços.

**Avaliação de regras para o Domain:** as calculadoras classificam linhas de leitura (modo e resultado nas estatísticas, mínimos de privacidade nas durações) e não regras de entidades, por isso ficam em `Calculators`. As regras de entidade já estão no Domain.
