# Plutao

Downloader de vídeo/áudio para Windows baseado em yt-dlp + FFmpeg, com análise de contas/perfis e seleção individual de mídia.

## MP4 compatível

Com **MP4 compatível (garantir H.264/AAC)** ativado, o Plutao:

1. tenta baixar H.264/AAC diretamente quando a plataforma oferece esses formatos;
2. verifica os codecs reais do arquivo final com `ffprobe`;
3. se o vídeo vier em VP9/AV1 (ou o áudio em codec não compatível), converte automaticamente para H.264/AAC;
4. se o arquivo já estiver em H.264/AAC, não recodifica.

Isso evita depender de extensões AV1/VP9 do Windows para reproduzir MP4s baixados de Instagram, TikTok e outras plataformas.

## Contas e perfis

Perfis do Instagram são analisados com gallery-dl como fallback e os vídeos selecionados são baixados individualmente pelo yt-dlp. YouTube, TikTok e outras coleções usam os extratores disponíveis conforme a plataforma.

## Compilar

Execute `build-release.bat`. O executável final será criado em `release/Plutao.exe`.
