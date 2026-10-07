# Dependências — análise de segurança separada

Registo de 6 de outubro de 2026, durante a continuação no Mac M2.

- Backend: `MeepleBoard.Services/MeepleBoard.Services.csproj` declara **AutoMapper 14.0.0**. O aviso de segurança **NU1903** consta da documentação de estabilização e foi reportado pelo utilizador. Compilação com .NET SDK **9.0.318** confirmada pelo utilizador; versão do SDK também confirmada nesta sessão. Falta analisar o advisory, os usos afetados e uma correção compatível.
- Frontend: o utilizador reportou **67 vulnerabilidades** após `npm ci`, com Node **22.14.0**/npm **10.9.2**, e TypeScript aprovado. Falta recolher o relatório e distinguir gravidade, dependências diretas/transitivas e exposição em runtime/build. A contagem não foi reproduzida nesta sessão.

Trabalho separado do arranque dos testes. Nenhuma dependência foi atualizada; não executar correções automáticas com `--force`. Uma futura proposta deve documentar advisories, compatibilidade e validação, preservando a base habitual.

## Atualização 07/10/2026 — partilha de cartões

A instalação dirigida de expo-sharing/react-native-view-shot e remoção de uma dependência direta desnecessária reportou **69 vulnerabilidades npm: 17 moderadas, 51 altas e 1 crítica**, com Node 22.14.0/npm 10.9.2. Manifests/lockfile alterados apenas para esta funcionalidade, usando versões mapeadas pelo SDK Expo local. Esta contagem global não atribui automaticamente as falhas aos novos pacotes nem substitui análise de exposição. Não executados npm audit fix, --force ou atualização automática generalizada. NU1903/AutoMapper 14.0.0 permanece para análise separada. O registo inicial de 67 fica como evidência histórica.
