# Metadados das fotografias do diário

Correção autorizada a 2026-10-18 e implementada em DeviceTests. Produção, Azure, segredos e AutoMapper não foram alterados.

## O que mudou

- **Limpeza no servidor:** antes de guardar ou servir uma fotografia nova do diário, o servidor retira os metadados identificativos (`PhotoMetadata`, chamado em `CampaignService.AddJournalPhotoAsync`). Retira:
  - **EXIF:** GPS, marca e modelo da câmara, números de série, datas, autor e miniaturas embutidas;
  - **XMP;**
  - **IPTC e Photoshop;**
  - **comentários;**
  - **blocos de texto PNG** (`tEXt`, `iTXt`, `zTXt`, `tIME`).
- **A imagem não muda:** os píxeis são copiados tal como estão, sem recompressão.
- **Orientação:** fica num bloco EXIF mínimo que só tem essa etiqueta, para a fotografia aparecer direita.
- **Cor:** ficam os dados de cor (perfil ICC e bloco Adobe no JPEG; `iCCP`, `sRGB`, `gAMA` e `cHRM` no PNG).
- **Formatos aceites:** só JPEG, PNG e WebP, o que a app envia.
  - Outros formatos (GIF, HEIC) recebem «Formato de imagem não suportado. Usa JPEG, PNG ou WebP.»
  - Um ficheiro danificado recebe «Não foi possível ler esta imagem.»
- **Abrange tudo:** participantes e, no futuro, a comunidade usam a mesma cópia limpa.

## Testes

- **`tests/Photos.Tests` (5):** um ficheiro de teste por formato, com GPS, câmara, número de série, datas, autor, XMP/IPTC, comentário e orientação 6. Confirma:
  - que os metadados saem;
  - que a orientação fica sozinha;
  - que os dados da imagem ficam intactos;
  - que a operação é idempotente;
  - que formatos e ficheiros danificados são recusados.
- **`tools/DeviceTestApi/verify-photo-metadata.cjs`:** usa SQL e API reais, com pessoas descartáveis. A envia, B (outro participante) recebe. O leitor ImageIO da Apple (`image-metadata.swift`) confirma que a cópia de B:
  - descodifica;
  - tem o mesmo tamanho, a mesma orientação e os **mesmos píxeis** (impressão digital dos píxeis descodificados);
  - não tem nenhuma etiqueta além de `tiff:Orientation`.
- **Fotografia grande real:** verificada uma vez à parte (3000 × 3000 px, JPEG com perfil de cor). Tinha 12 etiquetas e ficou com 0, com os mesmos píxeis.
- **Limitação:** o armazenamento de produção (Cloudinary) não é testado daqui. O que é testado é o conteúdo que lhe é enviado, que agora já vai limpo.

## Inventário das fotografias existentes (DeviceTests, só leitura, 2026-10-18)

`GET /device-test/disposable/photo-inventory` (só em loopback), com os metadados lidos pelo ImageIO:

| Ficheiros | Referenciados por | Metadados identificativos |
| --- | --- | --- |
| 1 | Pessoas de validação (fotografia da Carla) | Nenhum |
| 21 | Ninguém: ficaram de verificadores anteriores a esta correção | 1 com GPS (o ficheiro de teste sintético das 15:47 UTC); os restantes, nenhum |
| 0 | Contas do roteiro do iPhone | — |

- **Ficheiros órfãos:** a limpeza das pessoas descartáveis apagava as linhas, mas não os ficheiros. Isso ficou corrigido para as próximas execuções: agora também apaga os ficheiros das fotografias que remove.
- **Produção:** não inventariada. Não há credenciais de produção neste ambiente, e não se usam.

## Plano para as fotografias existentes (não executado)

1. **DeviceTests, ficheiros órfãos (21): em quarentena desde 2026-10-18 16:07 UTC.**
   - Comando: `node tools/DeviceTestApi/photo-quarantine.cjs move`. Antes de mover, volta a confirmar que nenhuma entrada de diário referencia os ficheiros.
   - Destino: `.device-tests/photos-quarantine/2026-10-18T16-07-17-045Z/`, com `inventory.json` (tamanho e SHA-256 de cada ficheiro).
   - Para repor: `node tools/DeviceTestApi/photo-quarantine.cjs restore 2026-10-18T16-07-17-045Z`.
   - Ainda não foram apagados definitivamente; aguarda decisão.
   - A fotografia de validação (Carla) não foi movida e tem o mesmo SHA-256 de antes.
2. **DeviceTests, ficheiros referenciados:**
   - correr o inventário antes de cada validação;
   - qualquer ficheiro referenciado com metadados é reescrito com `PhotoMetadata`, mantendo a referência.
   - Hoje não há nenhum nesse caso.
3. **Produção:** só com autorização e credenciais adequadas, fora deste ambiente.
   1. **Inventário só de leitura** da pasta `meepleboard/match-journal`, com os metadados de cada imagem, contando GPS e câmara.
   2. **Reprocessamento por lotes:**
      - descarregar;
      - aplicar `PhotoMetadata.Strip`;
      - enviar como imagem protegida nova;
      - atualizar a referência na entrada do diário;
      - apagar a antiga só depois de verificar.
      - Guarda-se um mapa antigo → novo para poder reverter.
   3. **Referências públicas antigas** (fotografias anteriores ao armazenamento protegido, que hoje não são servidas): ficam como estão até decisão própria.
