# Dependências — análise de segurança separada

Registo de 6 de outubro de 2026, durante a continuação no Mac M2.

- Backend: `MeepleBoard.Services/MeepleBoard.Services.csproj` declara **AutoMapper 14.0.0**. O aviso de segurança **NU1903** consta da documentação de estabilização e foi reportado pelo utilizador. Compilação com .NET SDK **9.0.318** confirmada pelo utilizador; versão do SDK também confirmada nesta sessão. Falta analisar o advisory, os usos afetados e uma correção compatível.
- Frontend: o utilizador reportou **67 vulnerabilidades** após `npm ci`, com Node **22.14.0**/npm **10.9.2**, e TypeScript aprovado. Falta recolher o relatório e distinguir gravidade, dependências diretas/transitivas e exposição em runtime/build. A contagem não foi reproduzida nesta sessão.

Trabalho separado do arranque dos testes. Nenhuma dependência foi atualizada; não executar correções automáticas com `--force`. Uma futura proposta deve documentar advisories, compatibilidade e validação, preservando a base habitual.
