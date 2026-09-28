# Changelog

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
