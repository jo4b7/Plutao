# Plutao v0.4.4

## O que mudou

### Componentes permanentes

Os componentes agora ficam em:

`%LOCALAPPDATA%\Plutao\tools`

Isso inclui `yt-dlp.exe`, `ffmpeg.exe`, `ffprobe.exe`, `deno.exe` e `gallery-dl.exe`. Atualizar somente o `Plutao.exe` não força um novo download do FFmpeg.

Na primeira abertura desta versão, o Plutao tenta copiar automaticamente os componentes da antiga pasta `tools` ao lado do executável para o novo local.

### Links individuais mais rápidos

O Plutao reconhece automaticamente links individuais comuns de YouTube/Shorts, Instagram/Reels, TikTok, X/Twitter e Facebook. Nesses links ele força o modo sem playlist, mesmo quando **Permitir página/conta/canal/perfil completo** está ativado.

Deno é preparado apenas quando há link do YouTube.

### Diagnóstico de tempo

Durante o processo, o status pode mostrar:

- Conectando à plataforma
- Obtendo informações
- Lendo formatos disponíveis
- Selecionando formato
- Iniciando download
- Baixando
- Convertendo/finalizando

O log registra o tempo gasto em componentes, preparação/análise, download/processamento e total.

## Atualizar manualmente

1. Extraia este pacote completo por cima de `C:\PROJETOS\Plutao-\Plutao`.
2. Abra o GitHub Desktop.
3. Confira **Changes**.
4. Commit sugerido: `Plutao v0.4.4 - cache permanente e analise mais rapida`.
5. Faça **Push origin**.
6. Aguarde **Actions → Build Plutao Release** ficar verde.
7. Baixe o novo `Plutao.exe` em **Releases → Plutao - versão mais recente**.
