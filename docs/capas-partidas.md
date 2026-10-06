# Capas nas respostas de partidas

6 de outubro de 2026. Match → MatchDto passa a preencher GameImageUrl a partir de Game.ImageUrl, usando a relação já carregada nos detalhes individuais e de sessão. Sem imagem, conserva ausência/string vazia para o placeholder do frontend. Não são alteradas permissões, filtros de participação, resultados, escrita, esquema ou migrações.

Validação: compilação; 59 testes HTTP de autorização + 1 auditoria offline, incluindo capa presente e ausente em partida/sessão; 20 cenários HTTP/SQL; verify-session-match-details.cjs compara também gameImageUrl com o GET individual; fotografias locais e verificador SQL de escrita aprovados. Exclusivamente base marcada MeepleBoard_DeviceTests. Base habitual e armazenamento externo intactos. Avisos de dependências continuam em análise separada.

A confirmação visual no frontend usa a partida devolvida pela API. Repetir fotografias utiliza o ID existente, sem criar outra partida. Resultado não definido permanece distinto de nome indisponível; limitações solo/cooperativo/empate não foram corrigidas. Validação visual no iPhone pendente.
