# Plutao

Baixador universal de vídeo e áudio para Windows, com interface gráfica e suporte baseado em **yt-dlp + FFmpeg + Deno**.

> Use apenas para conteúdo que você tem autorização para baixar e respeite os termos/direitos aplicáveis de cada serviço.

## Recursos

- URLs individuais ou várias URLs em fila.
- Vídeo: Melhor, 2160p, 1440p, 1080p, 720p, 480p e 360p.
- Contêiner de vídeo: MP4, MKV e WebM.
- Áudio: MP3, M4A, AAC, FLAC, WAV e Opus.
- Pastas separadas para **Vídeos** e **Áudios**.
- Opção **Organizar por canal/criador** ativável/desativável.
- Playlists, canais e perfis quando habilitado e suportado pelo yt-dlp.
- Cookies do Edge, Chrome ou Firefox para conteúdo que exija sua própria sessão.
- Miniatura, metadados e JSON opcionais.
- Histórico para evitar downloads repetidos.
- Atualização automática/manual de yt-dlp, FFmpeg e Deno.
- Barra de progresso com porcentagem, velocidade, ETA, item atual e etapa.
- Tema preto.

## Organização dos arquivos

Com **Organizar por canal/criador** desativado:

`Videos/Nome do vídeo [id].mp4`

Com a opção ativada:

`Videos/Nome do criador/Nome do vídeo [id].mp4`

O mesmo comportamento vale para a pasta de áudios.

## Como compilar

Requer Windows 10/11 e .NET 8 SDK x64.

Execute `build-release.bat`. O resultado ficará em:

`release/Plutao.exe`

A publicação é `win-x64`, self-contained e single-file.
