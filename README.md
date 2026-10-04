# Plutao

Plutao é um downloader universal para Windows baseado em **yt-dlp + FFmpeg**, com suporte a vídeo, áudio, páginas, canais, perfis e playlists.

## v0.5.7 — miniaturas, resultados e transições suaves

A tela principal mantém o fluxo simples: **colar o link → escolher Vídeo/Áudio → qualidade/formato → BAIXAR**.

Perfis, contas e canais compatíveis agora são analisados automaticamente depois que o link é colado. O botão de análise deixa de ser uma etapa obrigatória e aparece como **TENTAR NOVAMENTE** apenas quando a análise automática falha.

O **Diagnóstico** não fica mais no fim da página: ele abre ao lado da área de perfil/vídeos somente quando solicitado. Ao fechá-lo, a lista recupera toda a largura. A coluna **Informações** prioriza a duração e quebra métricas longas em linhas para evitar texto cortado.

As **Configurações** ficam em uma gaveta lateral que pode ser aberta mesmo durante downloads ou análises. Opções que podem ser alteradas continuam acessíveis; mudanças feitas durante uma execução entram na próxima tarefa, enquanto a atualização dos binários permanece bloqueada até a execução terminar.

A v0.5.7 mantém as correções de estabilidade da v0.5.6 e melhora a experiência visual: a lista de resultados volta a aparecer corretamente após a análise de canais, miniaturas individuais do YouTube usam fallback JPG confiável e a interface ganha transições suaves para Configurações, Diagnóstico e resultados.

A v0.5.7 mantém as melhorias anteriores em cache, validação final, progresso e geometria: qualidades reais por vídeo, canais do YouTube separados em Vídeos/Shorts/Lives, metadados complementares do Instagram e o analisador alternativo de perfis do TikTok quando o yt-dlp falha ao obter o `secUid`.

- prévia automática para links individuais;
- análise automática para perfis/canais/páginas;
- duração, visualizações, curtidas, data e resolução quando disponíveis;
- seleção pela linha inteira nas listas;
- barra de progresso mantida durante todo o fluxo;
- destinos lembrados entre execuções.

## Resolução e codec automáticos

Em **MP4**, a compatibilidade é automática e não exige configuração:

- até **1080p** → H.264 + AAC;
- acima de **1080p** → HEVC/H.265 + AAC;
- para 1440p/2160p, a conversão HEVC tenta primeiro a **GPU AMD via AMF**; se não estiver disponível, usa `libx265` na CPU automaticamente;
- a resolução escolhida é priorizada antes do codec. Se 2160p estiver disponível apenas em VP9/AV1, o Plutao baixa 2160p e converte depois, sem cair silenciosamente para 1080p;
- o arquivo final é verificado novamente com `ffprobe`; o Plutao não considera concluído um MP4 que permaneça em AV1/VP9/Opus quando a regra exigir H.264/HEVC + AAC;
- em **Melhor disponível**, a regra é decidida pela resolução real do arquivo baixado.

O `ffprobe` verifica codec e altura do arquivo antes de qualquer conversão. Arquivos que já estão no codec correto não são recodificados.

## Cache inteligente

O Plutao usa `%LOCALAPPDATA%\Plutao` para componentes e cache:

```text
Plutao\
├── tools\             yt-dlp, FFmpeg, ffprobe, Deno, gallery-dl
├── cache\
│   ├── thumbs\        miniaturas limitadas automaticamente
│   └── data\          prévias e índice de reutilização
└── settings.json
```

- miniaturas têm limite automático de aproximadamente **400 MB**; as menos usadas são removidas primeiro;
- dados de prévia são pequenos e expiram automaticamente;
- ao baixar novamente o **mesmo link com as mesmas opções**, o Plutao pode reaproveitar o arquivo local pronto. No modo **Manter os dois**, cria uma nova cópia sem repetir análise, download ou conversão;
- o cache próprio do yt-dlp também é persistente.

## Perfis e datas

A análise de perfil mostra, quando a plataforma disponibiliza:

- foto, nome e @usuário;
- seguidores, seguindo e publicações;
- biografia e conta pública/privada/verificada;
- **data de criação da conta**, somente quando o extrator fornece um campo explícito de criação/entrada;
- vídeos com miniatura, **data de publicação**, duração, resolução, curtidas e visualizações.

O Plutao não inventa a data de criação quando a plataforma não a expõe.

## Tempos de diagnóstico

O diagnóstico separa, quando o yt-dlp expõe as etapas:

- conexão/extração;
- seleção de formato;
- download/merge;
- validação/conversão de codec;
- total do item.

## Compilação

O fluxo normal é pelo GitHub Actions:

1. extraia o projeto completo sobre o repositório local;
2. **Commit to main** no GitHub Desktop;
3. **Push origin**;
4. aguarde **Build Plutao Release** ficar verde;
5. baixe `Plutao.exe` em **Releases → Plutao - versão mais recente**;
6. substitua o executável usado em `C:\PROJETOS\Plutao-\Aplicativo\Plutao.exe`.

`build-release.bat` permanece apenas como alternativa local de emergência.
