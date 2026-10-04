# Changelog

## 0.5.11

- Corrige a lista de vídeos que podia ficar com apenas uma linha visível mesmo em tela maximizada.
- A altura do painel de resultados deixa de depender da altura atual do controle interno com `Dock=Fill`, eliminando o ciclo de encolhimento.
- O painel de resultados e o Diagnóstico passam a usar uma altura-alvo própria calculada pelo espaço vertical realmente livre da janela.
- Maximizar/restaurar recalcula a altura disponível sem perder largura nem deixar um grande espaço preto desnecessário.
- Em monitores grandes, a lista aproveita mais espaço vertical; em janelas menores, mantém altura mínima e usa rolagem normalmente.
- Versão e User-Agent internos atualizados para 0.5.11.

## 0.5.10

- Miniaturas fora da área próxima da tela são liberadas para reduzir uso de memória em canais com milhares de vídeos.
- Filtros por categoria e ações de selecionar/limpar milhares de itens usam atualização em lote para evitar cintilação e travamentos.
- O DataGridView mantém double buffering, mas preserva o desenho nativo para evitar regressões visuais.
- A lista de URLs de miniaturas com falha passa a ter limite e limpeza automática.
- Downloads verificam se as pastas de destino e temporários são realmente graváveis antes de iniciar.
- Downloads de componentes validam o tamanho esperado quando o servidor informa Content-Length.
- yt-dlp, Deno e gallery-dl executam `--version` antes de substituir a instalação anterior.
- FFmpeg e ffprobe executam `-version` antes de substituir a instalação anterior.
- Versão e User-Agent internos atualizados para 0.5.10.

## 0.5.9

- Revisão ampla de estabilidade e desempenho sobre a v0.5.8.
- Redimensionamento da janela passa a ser agrupado, evitando relayout pesado a cada pixel.
- Miniaturas são pausadas durante resize/transições e retomadas somente depois do layout estabilizar.
- A área de resultados usa geometria mais previsível ao alternar entre janela normal e maximizada.
- Análises de canal/perfil em andamento são canceladas imediatamente quando link, cookies, limite ou modo mudam.
- URLs equivalentes passam a ser normalizadas ao reutilizar a seleção analisada.
- O progresso inicial usa a quantidade real de URLs selecionadas.
- Miniaturas do YouTube preservam a URL fornecida pelo yt-dlp antes dos fallbacks JPG.
- Miniaturas com falha usam backoff para evitar milhares de requisições repetidas ao rolar listas grandes.
- O carregamento de miniaturas reduz concorrência e atualizações de texto para aliviar a UI.
- Prévia individual tenta várias miniaturas automaticamente antes de ocultar a imagem.
- Atualização de yt-dlp, Deno e gallery-dl funciona com pasta temporária em outro disco.
- Downloads ignorados pelo histórico deixam de ser reportados falsamente como arquivo final ausente.
- Saída assíncrona do yt-dlp é drenada antes de validar o caminho final.
- Conversão MP4 mantém backup do original e restaura automaticamente se a validação final falhar.
- O diagnóstico passa a ter limite de tamanho para não degradar a interface em filas muito longas.
- Atualizações de progresso são limitadas para reduzir flood na thread da interface.
- Cache de coleções foi versionado novamente para não reaproveitar metadados/miniaturas antigos.
- Processos de yt-dlp, gallery-dl e FFmpeg passam a liberar seus handles ao terminar ou cancelar.
- Análise, download e atualização de componentes executam o trabalho pesado fora da thread da interface.
- A lista de resultados recebe mais espaço vertical em janelas grandes.
- Transições de listas enormes animam somente o contêiner leve, evitando redesenhar milhares de linhas por quadro.
- Ao mudar um link durante download/atualização, a análise automática do novo link é retomada ao final da tarefa.
- Cancelamento da prévia deixa de descartar o CancellationTokenSource enquanto a tarefa ainda está encerrando.
- Callbacks de log, progresso, avatar e miniaturas são protegidos contra o fechamento da janela.
- Abas ausentes do YouTube continuam sendo tratadas como normais, mas falhas reais de uma aba agora são reportadas.
- Validação automatizada inclui smoke test do EXE e integração real com os canais usados para reproduzir os bugs.
- User-Agent interno e versão do aplicativo atualizados para 0.5.9.

## 0.5.8

- Corrige o layout ao alternar entre janela normal e maximizada.
- A área de resultados volta a ocupar toda a largura disponível quando o Diagnóstico está fechado.
- Redimensionamentos são agrupados para evitar recalcular a interface a cada pixel durante maximize/restore.
- Canais com milhares de vídeos deixam de redimensionar todas as linhas da grade durante mudanças de janela.
- Transições de Configurações, Diagnóstico e resultados ficam mais curtas e leves.
- Em coleções grandes, a interface evita animações quadro a quadro sobre o DataGridView para não engasgar.
- Miniaturas de Shorts tentam múltiplos fallbacks JPG do YouTube antes de desistir.
- Falhas temporárias de miniatura deixam de ser marcadas como concluídas, permitindo nova tentativa ao voltar à área visível.
- Mensagens de canal sem aba de Shorts/Lives deixam de aparecer como ERROR no Diagnóstico.
- User-Agent interno e versão do aplicativo atualizados para 0.5.8.

## 0.5.7

- Corrige a área de resultados que podia permanecer oculta mesmo depois de um canal ser analisado com sucesso.
- Miniaturas individuais do YouTube usam `hqdefault.jpg` por ID como fallback estável.
- Miniaturas da lista de canais do YouTube também priorizam JPG compatível com WinForms.
- Falhas ao carregar uma miniatura deixam de exibir o ícone de imagem quebrada.
- A gaveta de **Configurações** passa a abrir e fechar com transição lateral suave.
- **Diagnóstico** e **resultados** ganham transições de expansão/recolhimento.
- Painéis animados usam double buffering para reduzir cintilação e aparência serrilhada.
- Cliques repetidos durante uma animação são ignorados até a transição terminar.
- User-Agent interno e versão do aplicativo atualizados para 0.5.7.

## 0.5.6

- Corrige colisão de cache entre playlists diferentes do YouTube.
- O modo **Substituir** deixa de reaproveitar silenciosamente um arquivo do cache.
- Arquivos reutilizados passam por verificação básica com `ffprobe`; cache inválido é descartado.
- **ABRIR ARQUIVO** só é liberado depois da validação final do arquivo.
- Conversões MP4 validam o temporário antes de substituir o original.
- Vídeos verticais como 1080x1920 passam a ser tratados como 1080p; 1440p/4K continuam usando HEVC.
- Coleções MP4 validam os caminhos finais capturados, em vez de validar apenas o último arquivo.
- Erros internos de coleções deixam de ser mascarados pelo `--ignore-errors`.
- O limite de canal do YouTube passa a valer para o total de Vídeos + Shorts + Lives.
- Instagram ganha categorias **Reels** e **Posts**.
- Comentários são exibidos quando a plataforma fornece a contagem.
- Corrige `@` duplicado e melhora a escolha do identificador de canais.
- Miniaturas do YouTube ganham fallback por ID; caches de imagem corrompidos são descartados.
- Cache de miniaturas em RAM passa a ter limite.
- Corrige o progresso em filas que misturam vários links e coleções.
- Durante uma tarefa, opções que alterariam a execução atual ficam bloqueadas.
- Análise cancelada ou vazia passa a mostrar estado correto e **TENTAR NOVAMENTE**.
- Resultados ocultos podem ser mostrados novamente.
- Gaveta de configurações, prévia e colunas da lista recebem ajustes de geometria.
- **Atualizar componentes** também instala/atualiza `gallery-dl`.
- Executáveis de componentes truncados passam a ser detectados.
- Remove o caminho temporário especial de desenvolvimento e usa `%LOCALAPPDATA%\Plutao\Temporarios`.
- User-Agent interno e versão do aplicativo atualizados para 0.5.6.

## 0.5.5

- Links de perfil/canal compatíveis passam a ser analisados automaticamente após o usuário colar o link; não é mais obrigatório clicar em **ANALISAR PERFIL / CANAL**.
- Em caso de falha na análise automática, o botão aparece como **TENTAR NOVAMENTE**; o diagnóstico continua fechado até o usuário ativá-lo.
- **Configurações** deixam de aparecer no fim da página e passam para uma gaveta lateral sobre a janela principal.
- A gaveta de configurações pode ser aberta durante download, análise de perfil e complementação de metadados; alterações feitas durante uma execução valem para a próxima tarefa.
- O botão **Diagnóstico** fica disponível no cabeçalho e o painel de log aparece ao lado da área de perfil/vídeos somente quando ativado.
- Ao fechar o diagnóstico, a lista de vídeos recupera automaticamente toda a largura disponível.
- A coluna **Informações** foi reorganizada para manter a duração visível, com quebra de linha para métricas longas como visualizações, curtidas, comentários e resolução.
- A lista de vídeos ganhou alturas de linha e larguras responsivas melhores quando o diagnóstico está aberto.
- Mantidas as correções da v0.5.4 para metadados do Instagram e análise alternativa de perfis do TikTok.

## 0.5.4

- Perfis do TikTok passam a usar um analisador alternativo com `gallery-dl` quando o yt-dlp falha ao obter o `secondary user ID`/`secUid`; os links individuais continuam sendo baixados pelo yt-dlp.
- A lista de perfil do TikTok tenta trazer duração, visualizações, curtidas, comentários, resolução, data e miniatura quando a plataforma fornece esses dados.
- Perfis do Instagram continuam sendo listados pelo `gallery-dl`, mas agora o Plutao completa os metadados de cada vídeo com o `yt-dlp` sem baixar a mídia.
- A lista de vídeos do Instagram passa a mostrar **duração** e **visualizações** quando o Instagram/yt-dlp disponibiliza esses dados.
- Quando a plataforma não entrega uma dessas informações, a interface mostra `—` em vez de simplesmente deixar o campo sumir.
- A complementação de metadados é paralela com limite de processos para não deixar a análise excessivamente lenta nem disputar cookies do navegador.
- Curtidas, resolução, data e miniatura obtidas na primeira etapa são preservadas quando a segunda fonte não fornece o mesmo campo.
- O cache de análise de perfis foi renovado para evitar reutilizar listas antigas do Instagram sem duração/visualizações.
- A coluna de informações passa a identificar explicitamente a duração (`Duração 0:18`, por exemplo).

## 0.5.3

- Links base de canais do YouTube, como `youtube.com/@usuario`, passam a abrir automaticamente as categorias reais de **Vídeos**, **Shorts** e **Lives**.
- Links diretos `/videos`, `/shorts` e `/streams` continuam funcionando e abrem apenas a categoria correspondente.
- As seções do canal deixam de aparecer como se fossem vídeos individuais; o Plutao consulta cada aba e junta as mídias reais, removendo duplicados.
- A seleção de perfis/canais ganhou filtros por categoria com contagem de itens.
- A linha inteira da lista agora pode ser clicada para selecionar ou desmarcar uma mídia; o checkbox deixa de ser o único alvo de clique.
- Botões de seleção passam a agir sobre a categoria visível quando um filtro está aberto.
- A prévia de vídeo passa a ler as resoluções realmente disponíveis no yt-dlp e o seletor de qualidade é atualizado automaticamente.
- Se a maior resolução disponível for 1440p, por exemplo, 2160p deixa de aparecer para aquele vídeo.
- O seletor mostra a resolução máxima disponível e mantém **Melhor disponível** como comportamento padrão quando ainda não há uma análise individual.
- Botões **ABRIR ARQUIVO** e **ABRIR PASTA** ganharam largura mínima para não ter o texto cortado.
- O layout responsivo agora usa o tamanho real da janela, diferenciando melhor janela normal de modo maximizado em telas QHD.
- A barra de progresso continua visível durante todo o fluxo e permanece em 100% após uma conclusão bem-sucedida.

## 0.5.2

- Corrige a captura do caminho do arquivo final após o yt-dlp.
- Se o `after_move` não informar o caminho, o Plutao procura o arquivo pelo sufixo único, ID da mídia e data de modificação.
- MP4 não é mais marcado como concluído quando o arquivo final não pôde ser localizado e validado.
- A validação/conversão H.264/HEVC + AAC passa a ser obrigatória antes do sucesso do item.
- Cache de download da v0.5.1 é invalidado para não reaproveitar arquivos AV1/VP9/Opus não convertidos.
- Mantém HEVC por AMD AMF em 1440p/2160p com fallback automático para CPU.

## 0.5.1

- Conversão HEVC de 1440p/2160p tenta usar a GPU AMD via AMF primeiro; em GPUs compatíveis, como a RX 5700 XT, a recodificação fica muito mais rápida.
- Se AMF não estiver disponível ou falhar, o Plutao troca automaticamente para `libx265` na CPU sem exigir nenhuma opção do usuário.
- O log informa se o HEVC foi codificado pela GPU AMD ou pelo fallback de CPU.
- MP4 passa a aplicar a regra de compatibilidade automaticamente, sem opção extra na interface.
- Cache de downloads foi versionado novamente para não reaproveitar arquivos incompatíveis da v0.5.0.
- Arquivos vindos do cache são validados antes da reutilização; AV1/VP9/Opus são convertidos quando necessário.
- Após qualquer conversão, o Plutao usa ffprobe novamente e só marca como concluído se o codec final estiver correto.
- Até 1080p o alvo continua H.264 + AAC; acima de 1080p, HEVC/H.265 + AAC.
- Workflow agora move a tag `latest` para o commit realmente compilado, corrigindo o aviso de commits posteriores na página de Release.
- READMEs antigos por versão deixaram de fazer parte do pacote completo; o histórico fica no CHANGELOG.md.

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
