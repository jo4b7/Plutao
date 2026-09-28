# Plutao

Downloader de vídeo e áudio para Windows baseado em **yt-dlp + FFmpeg**, com suporte a YouTube, Instagram, TikTok e outras plataformas suportadas pelo yt-dlp.

> Use apenas para conteúdo que você tem autorização para baixar e respeite os termos e direitos aplicáveis de cada serviço.

## v0.2.0

- Tema preto.
- Barra de progresso visível com porcentagem, item atual, velocidade e ETA.
- Status de etapas: preparando, analisando, baixando, convertendo e finalizando.
- Deno instalado automaticamente para suporte completo ao YouTube atual.
- yt-dlp, FFmpeg e Deno podem ser atualizados pelo botão **Atualizar componentes**.
- Pasta de temporários configurável; no PC de desenvolvimento usa por padrão `C:\PROJETOS\Plutao-\Teporarios`.
- Melhor tratamento de MP4 e fallback de formatos.
- Mensagens de ajuda específicas para YouTube e Instagram quando um link falha.
- Correção de DPI/escala para evitar controles cortados.

## Formatos

### Vídeo

- Melhor
- 2160p
- 1440p
- 1080p
- 720p
- 480p
- 360p
- MP4, MKV e WebM

### Áudio

- MP3
- M4A
- AAC
- FLAC
- WAV
- Opus

## Cookies

Para conteúdo público, comece com **Nenhum**. Em plataformas que exijam a sua própria sessão, selecione Edge, Chrome ou Firefox. O Plutao apenas usa cookies da sessão local do navegador selecionado; ele não contorna acesso que a sua conta não possui.

## Como compilar

Requer Windows 10/11 e .NET 8 SDK x64.

Execute:

`build-release.bat`

O executável será criado em:

`release\Plutao.exe`

A publicação é `win-x64`, self-contained e single-file.
