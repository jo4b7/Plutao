# Changelog

## 0.2.2

- Corrige cancelamento falso ao baixar FFmpeg em conexões onde o download leva mais de 100 segundos.
- Barra de progresso agora mostra também o download dos componentes (yt-dlp, FFmpeg e Deno).
- Exibe MB baixados, velocidade e ETA durante a preparação de componentes.
- O botão PARAR também cancela a atualização de componentes.

## 0.2.1

- Adicionada opção **Organizar por canal/criador**, que pode ser ativada ou desativada.
- Pastas separadas para vídeos e áudios.
- Quando a organização está ativada, cada criador/canal recebe sua própria subpasta.
- Quando está desativada, os arquivos ficam diretamente na pasta de Vídeos ou Áudios.
- Corrigido o layout das caixas de Links e Log, que estavam ficando comprimidas.
- Mantido o tema preto e o progresso detalhado da v0.2.0.

## 0.2.0

- Tema preto.
- Barra de progresso detalhada.
- Integração de Deno para melhorar suporte ao YouTube.
- Pasta de temporários configurável.
