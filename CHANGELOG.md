# Changelog

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
