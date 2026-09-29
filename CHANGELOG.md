# Plutao v0.4.3

## Destinos persistentes
- Removidos os caminhos fixos de Vídeos e Áudios.
- Em uma instalação nova, os dois campos começam vazios.
- O usuário escolhe manualmente as pastas uma vez.
- Os destinos escolhidos ficam salvos em `%LOCALAPPDATA%\Plutao\settings.json`.
- Os caminhos são restaurados automaticamente nas próximas aberturas.
- Também salva caminhos digitados/colados manualmente.

## Compilação automática
- Adicionado GitHub Actions para compilar `win-x64` automaticamente a cada push na `main`.
- O workflow gera `Plutao.exe` self-contained e `Plutao-win-x64.zip`.
- A compilação também atualiza automaticamente a release `latest`.
- `build-release.bat` continua no repositório apenas como opção de emergência; não é mais necessário no fluxo normal.
