# Plutao

Baixador universal de vídeo e áudio para Windows, com interface gráfica e suporte baseado no **yt-dlp + FFmpeg**.

> Use apenas para conteúdo que você tem autorização para baixar e respeite os termos/direitos aplicáveis de cada serviço.

## Recursos

- URLs individuais ou várias URLs em fila.
- Vídeo: Melhor, 2160p, 1440p, 1080p, 720p, 480p e 360p.
- Contêiner de vídeo: MP4, MKV e WebM.
- Áudio: MP3, M4A, AAC, FLAC, WAV e Opus.
- Playlists, canais e perfis quando habilitado e suportado pelo yt-dlp.
- Cookies do Chrome, Edge ou Firefox para conteúdo que exija sua própria sessão.
- Miniatura, metadados e JSON opcionais.
- Arquivo de histórico para não baixar o mesmo item duas vezes.
- Atualização automática/manual do yt-dlp.
- Download automático/manual do FFmpeg.
- Botão para interromper downloads e log em tempo real.

## Plataformas

A compatibilidade acompanha os extratores do yt-dlp. Isso inclui YouTube, TikTok e muitas outras plataformas públicas/suportadas. Como os sites mudam com frequência, mantenha o yt-dlp atualizado.

## Como compilar

Requer Windows 10/11 e .NET 8 SDK.

Abra `build-release.bat`. O resultado ficará em:

`release/Plutao.exe`

A publicação é `win-x64`, self-contained e single-file, então o usuário final não precisa instalar o .NET.

## Estrutura

- `src/Plutao/` — código do aplicativo.
- `tools/` — criado automaticamente ao executar; armazena yt-dlp.exe, ffmpeg.exe e ffprobe.exe.
- `release/` — saída do build.
