# Changelog

## 0.5.0

- Interface principal redesenhada com fluxo de aplicativo: colar link, visualizar prévia, escolher modo/qualidade/formato e baixar.
- Configurações técnicas e diagnóstico saíram do caminho principal e ficam recolhidos por padrão.
- Prévia automática para links individuais com título, criador, duração, data de publicação, visualizações, curtidas e resolução quando disponíveis.
- Ao clicar em BAIXAR sem destino configurado, o Plutao pede a pasta naquele momento e salva a escolha.
- Perfis continuam na mesma janela e agora podem mostrar a data de criação da conta quando a plataforma fornece um campo explícito confiável.
- Vídeos de perfil continuam mostrando data de publicação e demais metadados disponíveis.
- Compatibilidade MP4 automática: H.264/AAC até 1080p e HEVC/H.265 + AAC acima de 1080p.
- A resolução escolhida agora tem prioridade sobre o codec; 1440p/2160p pode ser baixado em VP9/AV1 e convertido depois, sem cair silenciosamente para 1080p.
- `ffprobe` mede a altura real antes de decidir entre H.264 e HEVC; a opção “Melhor” também segue a regra automaticamente.
- Cache de prévia persistente para evitar repetir consultas recentes do mesmo link.
- Reutilização de download concluído para o mesmo link + mesmas opções; em “Manter os dois”, uma nova cópia local é criada sem nova análise/download/conversão.
- Miniaturas passam a usar `%LOCALAPPDATA%\Plutao\cache\thumbs` com limpeza LRU automática e limite aproximado de 400 MB.
- Cache antigo de miniaturas é migrado automaticamente quando encontrado.
- Diagnóstico de tempo separa conexão/extração, seleção de formato, download/merge, compatibilidade/codec e total quando as etapas são detectáveis.

## 0.4.4

- Componentes (`yt-dlp`, `FFmpeg`, `ffprobe`, `Deno` e `gallery-dl`) passam a usar cache permanente em `%LOCALAPPDATA%\Plutao\tools`, sobrevivendo à troca do `Plutao.exe`.
- Migração automática dos componentes encontrados na antiga pasta `tools` ao lado do executável, evitando novo download depois da atualização.
- Cache permanente do yt-dlp em `%LOCALAPPDATA%\Plutao\cache`.
- Detecção automática de links individuais de YouTube/Shorts, Instagram/Reels, TikTok, X/Twitter e Facebook; links individuais usam `--no-playlist` mesmo quando o modo de conta/página está permitido.
- Deno só é preparado quando o link realmente usa YouTube; outras plataformas deixam de esperar por um componente desnecessário.
- Status da análise ficou mais informativo: conexão, obtenção de informações, leitura de formatos, seleção de formato e início do download.
- Log ganhou tempos de preparação dos componentes, análise do link, download/processamento e tempo total do item.

## 0.4.3

- Removidos os destinos fixos de Vídeos e Áudios. Em uma instalação nova, os campos começam vazios.
- O Plutao agora salva as pastas escolhidas manualmente em `%LOCALAPPDATA%\Plutao\settings.json` e restaura essas escolhas nas próximas aberturas.
- Alterar manualmente os destinos atualiza a preferência salva.
- Adicionado GitHub Actions para gerar `Plutao.exe` e `Plutao-win-x64.zip` automaticamente a cada push relevante na branch `main`.
- A compilação automática publica/atualiza a release `latest`, eliminando a necessidade de usar `build-release.bat` no fluxo normal.
- Mantidos todos os recursos e arquivos da v0.4.2.

## 0.4.1

- A análise de perfil não abre mais uma janela separada: perfil, estatísticas e seleção de vídeos aparecem dentro da janela principal.
- A seleção é aplicada em tempo real; basta marcar/desmarcar vídeos e clicar em **BAIXAR**.
- O painel de resultados pode ser ocultado sem perder a seleção atual.
- Miniaturas agora priorizam os primeiros itens visíveis e depois carregam o restante em segundo plano.
- Aumentada a concorrência de download de miniaturas para acelerar perfis grandes.
- Cache de miniaturas em memória e em `%LOCALAPPDATA%\\Plutao\\thumb-cache` por até 7 dias; reabrir o mesmo perfil fica muito mais rápido.
- Imagens são reduzidas ao tamanho da interface logo após o download para diminuir uso de RAM e travamentos com centenas de vídeos.
- Conexões HTTP de miniaturas são reutilizadas e otimizadas; CDN do Instagram recebe `Referer` apropriado quando necessário.
- Estatísticas com valor zero não são mais exibidas como se fossem dados reais quando a plataforma não informou o valor.

## 0.4.0

- Tela de análise agora mostra um cabeçalho do perfil com foto, nome, @usuário e plataforma.
- Exibe seguidores, seguindo, publicações, quantidade de vídeos encontrada e indicadores de conta pública/privada/verificada quando a plataforma fornece esses dados.
- Biografia do perfil é exibida quando disponível.
- Lista de seleção ganhou miniaturas dos vídeos, data e informações adicionais como curtidas, visualizações e resolução quando disponíveis.
- Instagram usa os metadados do gallery-dl para foto do perfil e miniaturas dos Reels/posts.
- YouTube/TikTok e outras coleções aproveitam os metadados disponibilizados pelo yt-dlp.
- Falha ao carregar uma miniatura não bloqueia análise nem download.
- Botão para abrir o perfil original diretamente da tela de seleção.

## 0.3.5

- Interface ganhou mais espaço vertical em monitores grandes sem voltar a ficar poluída.
- Espaçamento se adapta automaticamente entre monitores HD e QHD/2K.
- Área de links, progresso, botões e log usam alturas diferentes conforme a resolução disponível.
- Campos de pastas ficaram um pouco mais altos para evitar aparência “espremida”.
- Destino padrão de vídeos alterado para `E:\[VIDEOS]`.
- Destino padrão de áudios alterado para `E:\[MUSICAS]`.
- Mantidos Opções avançadas e Log recolhíveis.

## 0.3.4

- Interface principal mais compacta sem mudar o tema visual.
- Opções menos usadas foram movidas para **Opções avançadas**, recolhidas por padrão.
- Log agora pode ser mostrado/ocultado e abre automaticamente quando há erro.
- Controles de formato usam layout responsivo e quebram de linha em vez de ficarem cortados.
- Janela principal agora cabe melhor em monitores HD/1366x768.
- Ajuste automático ao mover entre monitores com DPI/resoluções diferentes.
- Área principal ganhou rolagem apenas quando realmente necessária.
- Tela de seleção de vídeos também recebeu ajustes de DPI e tamanho mínimo.

## 0.3.3

- Corrige MP4 do Instagram/TikTok que podia terminar em VP9/AV1 mesmo com “MP4 compatível” ativado.
- O Plutao agora usa `ffprobe` após o download para verificar os codecs reais do arquivo final.
- Quando necessário, converte automaticamente somente o que estiver incompatível para H.264/AAC com FFmpeg.
- Se o vídeo já for H.264 e o áudio AAC, não há recodificação nem perda de tempo/qualidade.
- O seletor de formatos fora do YouTube agora tenta H.264/AAC antes dos fallbacks flexíveis.
- Captura do caminho final do arquivo ficou mais confiável com `after_move`, inclusive para os botões Abrir arquivo/Abrir pasta.

## 0.3.2

- Fallback com gallery-dl para analisar perfis do Instagram.
- Correção de respostas `null` durante análise de coleções.
