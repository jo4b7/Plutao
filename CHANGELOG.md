# Changelog

## 0.2.5

- Corrige o erro `Requested format is not available` em links do Instagram e outros sites.
- A preferência H.264/AAC estrita agora é aplicada ao YouTube, onde AV1 em MP4 é comum.
- Instagram/TikTok/outros passam a priorizar MP4 progressivo com fallback flexível.
- Mantém limite de qualidade quando a plataforma fornece essa informação, com fallback para o melhor formato disponível.

## 0.2.4

- Adiciona política para arquivos existentes: **Manter existente**, **Substituir** e **Manter os dois**.
- Vídeos agora incluem a qualidade real no nome do arquivo.
- **Evitar repetir o mesmo link (histórico)** passa a vir desativado por padrão e com texto mais claro.
- Adiciona **Abrir arquivo** e **Abrir pasta** para o último download concluído.
- Mantém MP4 H.264/AAC como opção de compatibilidade padrão.

## 0.2.3

- Adiciona MP4 compatível com prioridade para H.264/AVC + AAC.

## 0.2.2

- Progresso dos componentes yt-dlp, FFmpeg e Deno.
- Remove limite padrão de 100 segundos no download dos componentes.
