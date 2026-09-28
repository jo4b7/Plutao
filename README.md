# Plutao

Baixador universal de vídeo e áudio para Windows baseado em **yt-dlp + FFmpeg**.

## v0.3.1 — analisar conta e selecionar vídeos

Agora o modo de página/conta/canal/perfil pode ser analisado antes do download.

Fluxo:

1. Cole um único link de conta/página/perfil/canal.
2. Deixe **Baixar página/conta/canal/perfil completo** ativado.
3. Escolha o limite de análise (Todos, 10, 25, 50, 100 ou 200).
4. Clique em **ANALISAR CONTA/PÁGINA**.
5. O Plutao mostra quantos itens encontrou e abre uma lista para marcar/desmarcar individualmente.
6. Clique em **USAR SELECIONADOS** e depois em **BAIXAR**.

A seleção é aplicada ao link original usando os índices da coleção, então não é necessário transformar a conta em dezenas de links manualmente.

> Algumas plataformas podem listar publicações que não são vídeos ou podem exigir cookies da sua própria sessão. A quantidade mostrada corresponde aos itens que o extrator conseguiu enumerar dentro do limite escolhido.

## Recursos principais

- Links individuais ou múltiplos links em fila.
- Um link de perfil/conta/canal pode expandir para vários itens.
- Análise prévia da conta/página com contagem.
- Seleção individual dos itens antes de baixar.
- Marcar todos / desmarcar todos.
- Progresso por mídia dentro de contas/perfis.
- Vídeo: Melhor, 2160p, 1440p, 1080p, 720p, 480p e 360p.
- MP4 compatível H.264/AAC para YouTube.
- MP4, MKV e WebM.
- Áudio: MP3, M4A, AAC, FLAC, WAV e Opus.
- Cookies de Edge, Chrome ou Firefox.
- Organização opcional por canal/criador.
- Controle de arquivos existentes e histórico anti-repetição.
- yt-dlp, FFmpeg e Deno gerenciados pelo aplicativo.
- Botões para abrir o último arquivo e sua pasta.

## Compilar

Requer Windows 10/11 e .NET 8 SDK x64. Execute `build-release.bat`.
O resultado fica em `release/Plutao.exe`.


## Análise de perfis do Instagram

O yt-dlp está com o extrator de perfis do Instagram marcado como quebrado em versões atuais. O Plutao 0.3.2 usa automaticamente o **gallery-dl** como analisador alternativo para listar posts/reels de perfis e depois envia os vídeos selecionados ao yt-dlp por URL individual. O componente é baixado sob demanda. Em alguns perfis o Instagram pode exigir cookies de uma sessão válida; nesse caso selecione Edge, Chrome ou Firefox no campo **Cookies**.
