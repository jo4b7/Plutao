# Plutao

Plutao é um downloader universal para Windows baseado em **yt-dlp + FFmpeg**, com suporte a vídeo, áudio, páginas, canais, perfis e playlists.

## v0.5.2 — validação final obrigatória

A tela principal foi reorganizada para que o fluxo normal seja simples: **colar o link → escolher Vídeo/Áudio → qualidade/formato → BAIXAR**.

A v0.5.2 corrige um caso em que o yt-dlp podia concluir o processo sem o Plutao conseguir capturar o caminho final do arquivo. Agora o aplicativo procura o arquivo final pelo sufixo/ID/data de gravação e **não marca o download como concluído** enquanto não conseguir localizar e validar o MP4. Isso impede que AV1/VP9 + Opus escape sem a conversão automática para H.264/AAC ou HEVC/AAC.


- prévia automática de links individuais com título, criador, duração, data de publicação, visualizações e curtidas quando a plataforma fornece;
- contas/canais/perfis continuam sendo analisados e selecionados dentro da própria janela;
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
