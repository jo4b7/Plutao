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

## Compilação e execução

O fluxo normal usa GitHub Actions: faça **Commit + Push** e aguarde `Build Plutao Release` ficar verde.
Depois baixe `Plutao.exe` em **Releases → Plutao - versão mais recente**.

`build-release.bat` permanece apenas como alternativa local de emergência.

## Interface compacta

A partir da v0.3.4, a tela principal mostra somente os controles usados com frequência.
Use **Opções avançadas** para temporários, cookies, política de arquivos existentes e opções de metadados/compatibilidade.
O **Log** também pode ser recolhido para economizar espaço e reaparece automaticamente quando ocorre um erro.

A interface usa DPI Per-Monitor V2, layout responsivo e rolagem de segurança para funcionar melhor ao mover a janela entre monitores QHD/2K e HD.

Na v0.3.5 o espaçamento vertical também se adapta ao monitor: em QHD/2K a interface fica mais confortável e, em HD, volta automaticamente para medidas compactas.

## Destinos

Vídeos e Áudios não têm mais um disco fixo. Na primeira execução, escolha as pastas manualmente; o Plutao salva as escolhas em `%LOCALAPPDATA%\Plutao\settings.json`.
## Visualização de perfil

Ao usar **VER / ANALISAR PERFIL**, o Plutao mostra as informações e a seleção de vídeos **dentro da própria janela principal**, sem abrir outra janela:

- foto do perfil;
- nome e @usuário;
- seguidores, seguindo e quantidade de publicações;
- número de vídeos encontrados;
- biografia e indicadores de conta pública/privada/verificada;
- miniaturas dos vídeos;
- data, curtidas, visualizações, resolução e duração quando a plataforma fornece esses metadados.

As miniaturas são opcionais: se a CDN da plataforma bloquear uma imagem, a seleção e o download continuam funcionando normalmente.



### Desempenho das miniaturas

Na v0.4.1, as primeiras miniaturas da lista são priorizadas e o restante é carregado em paralelo em segundo plano. As imagens ficam em cache por até 7 dias em `%LOCALAPPDATA%\Plutao\thumb-cache`, então reabrir um perfil já analisado tende a ser quase instantâneo. As imagens também são redimensionadas antes de permanecerem na memória para reduzir consumo de RAM em perfis com centenas de vídeos.

## Componentes permanentes e início mais rápido

Na v0.4.4, `yt-dlp`, FFmpeg/ffprobe, Deno e gallery-dl ficam em `%LOCALAPPDATA%\Plutao\tools`. Trocar o `Plutao.exe` não obriga mais a baixar esses componentes novamente. Se houver uma pasta `tools` antiga ao lado do executável, os componentes existentes são copiados automaticamente para o novo local.

Links de mídia individual são detectados automaticamente e usam modo sem playlist, mesmo que a opção de páginas/contas esteja habilitada. O log também mede o tempo gasto em análise, download/processamento e total, facilitando identificar gargalos por plataforma.
