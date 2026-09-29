# Plutao

Plutao é um downloader universal para Windows baseado em **yt-dlp + FFmpeg**, com suporte a vídeo, áudio, páginas, canais, perfis e playlists.

## v0.5.0 — experiência de aplicativo

A tela principal foi reorganizada para que o fluxo normal seja simples: **colar o link → escolher Vídeo/Áudio → qualidade/formato → BAIXAR**.

- prévia automática de links individuais com título, criador, duração, data de publicação, visualizações e curtidas quando a plataforma fornece;
- contas/canais/perfis continuam sendo analisados e selecionados dentro da própria janela;
- destinos ficam resumidos na tela principal e são lembrados entre execuções;
- configurações técnicas e diagnóstico ficam recolhidos;
- na primeira utilização, se ainda não houver pasta de destino, o Plutao pergunta onde salvar ao clicar em **BAIXAR**.

## Resolução e codec automáticos

Com **Compatibilidade automática** ativada em MP4:

- até **1080p** → H.264 + AAC;
- acima de **1080p** → HEVC/H.265 + AAC;
- a resolução escolhida é priorizada antes do codec. Se 2160p estiver disponível apenas em VP9/AV1, o Plutao baixa 2160p e converte depois, sem cair silenciosamente para 1080p;
- em **Melhor**, a regra é decidida pela resolução real do arquivo baixado.

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
