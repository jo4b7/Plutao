# Plutao

Baixador universal de vídeo e áudio para Windows, com interface gráfica e suporte baseado em **yt-dlp + FFmpeg + Deno**.

> Use apenas para conteúdo que você tem autorização para baixar e respeite os termos/direitos aplicáveis de cada serviço.

## Recursos

- URLs individuais ou várias URLs em fila.
- Vídeo: Melhor, 2160p, 1440p, 1080p, 720p, 480p e 360p.
- Contêiner de vídeo: MP4, MKV e WebM.
- MP4 compatível: H.264/AVC + AAC por padrão.
- Áudio: MP3, M4A, AAC, FLAC, WAV e Opus.
- Pastas separadas para **Vídeos** e **Áudios**.
- Opção **Organizar por canal/criador**.
- Nome dos vídeos inclui a qualidade real, por exemplo: `Título [id] [1080p].mp4`.
- Política configurável quando um arquivo já existe: **Manter existente**, **Substituir** ou **Manter os dois**.
- Em **Manter os dois**, o Plutao acrescenta um identificador de data/hora ao novo arquivo para nunca sobrescrever o anterior.
- **Evitar repetir o mesmo link (histórico)** é uma opção separada e vem desativada por padrão. Quando ligada, o mesmo ID pode ser ignorado mesmo se você mudar qualidade ou formato.
- Botões **Abrir arquivo** e **Abrir pasta** para o último download concluído.
- Playlists, canais e perfis quando habilitado e suportado pelo yt-dlp.
- Cookies do Edge, Chrome ou Firefox para conteúdo que exija sua própria sessão.
- Miniatura, metadados e JSON opcionais.
- Atualização automática/manual de yt-dlp, FFmpeg e Deno.
- Barra de progresso com porcentagem, velocidade, ETA, item atual e etapa.
- Tema preto.

## Arquivos existentes

- **Manter existente**: se já existir exatamente aquele arquivo/qualidade, o yt-dlp não baixa novamente.
- **Substituir**: força o download e sobrescreve o arquivo com o mesmo nome.
- **Manter os dois (novo nome)**: preserva o anterior e salva o novo com um sufixo de data/hora.

Arquivos em qualidades diferentes possuem nomes diferentes, por exemplo:

- `Vídeo [abc123] [1080p].mp4`
- `Vídeo [abc123] [720p].mp4`

## Como compilar

Requer Windows 10/11 e .NET 8 SDK x64.

Execute `build-release.bat`. O resultado ficará em:

`release/Plutao.exe`

A publicação é `win-x64`, self-contained e single-file.


## Compatibilidade de formatos

A opção **MP4 compatível (H.264/AAC)** evita AV1 no YouTube. Em Instagram, TikTok e outras plataformas, o Plutao usa um seletor MP4 mais flexível para não rejeitar formatos progressivos disponíveis.
