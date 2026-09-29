# Plutao v0.5.0

Esta versão transforma a tela principal em um fluxo mais próximo de um aplicativo final e adiciona otimizações automáticas sem criar novas opções desnecessárias.

Principais mudanças:

- nova tela principal focada em colar o link e baixar;
- prévia automática de mídia individual com metadados e data de publicação;
- análise de perfil integrada com data de criação da conta quando realmente fornecida pela plataforma;
- compatibilidade automática H.264 até 1080p e HEVC acima de 1080p;
- resolução escolhida tem prioridade, evitando queda silenciosa de 2160p/1440p para 1080p por causa do codec;
- reutilização local do mesmo download com as mesmas opções;
- cache de miniaturas limitado automaticamente a cerca de 400 MB;
- diagnóstico de tempo mais detalhado;
- componentes continuam persistentes em `%LOCALAPPDATA%\Plutao\tools`.
