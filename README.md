# Plutao

Plutao é um downloader universal para Windows baseado em **yt-dlp + FFmpeg**, com suporte a vídeo, áudio, páginas, canais, perfis e playlists.

## v0.5.4 — metadados completos do Instagram

A tela principal foi reorganizada para que o fluxo normal seja simples: **colar o link → escolher Vídeo/Áudio → qualidade/formato → BAIXAR**.

A v0.5.4 mantém as qualidades reais e a organização de canais da v0.5.3 e melhora a análise de perfis do Instagram. O `gallery-dl` continua descobrindo rapidamente os posts/reels e o Plutao usa o `yt-dlp` apenas para completar os metadados dos vídeos, sem baixar a mídia durante a análise.

Na lista do Instagram, duração e visualizações aparecem quando a plataforma fornece esses dados. Quando um valor realmente não está disponível, o Plutao mostra `—` para deixar claro que o dado não foi informado. Curtidas, resolução, data e miniatura continuam sendo preservadas.

Em canais do YouTube, basta colar o link base (`youtube.com/@usuario`): o Plutao consulta **Vídeos**, **Shorts** e **Lives**, organiza tudo por categoria e remove duplicados. A linha inteira continua clicável para selecionar ou desmarcar a mídia.

A validação final da v0.5.2 continua ativa: o Plutao localiza o arquivo final e só conclui um MP4 depois de validar H.264/AAC ou HEVC/AAC conforme a resolução.


- prévia automática de links individuais com título, criador, duração, data de publicação, visualizações e curtidas quando a plataforma fornece;
- contas/canais/perfis continuam sendo analisados e selecionados dentro da própria janela, com categorias de YouTube e seleção pela linha inteira;
- destinos ficam resumidos na tela principal e são lembrados entre execuções;
- configurações técnicas e diagnóstico ficam recolhidos;
- na primeira utilização, se ainda não houver pasta de destino, o Plutao pergunta onde salvar ao clicar em **BAIXAR**.

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
- biografia e estado pública/privada/verificada;
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
