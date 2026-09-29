# Plutao v0.4.3

Esta versão parte da v0.4.2 completa e mantém todos os arquivos do projeto.

## Destinos persistentes

- Vídeos e Áudios não recebem mais um caminho fixo em C:, E: ou outro disco.
- Em uma instalação nova, os dois campos começam vazios.
- Escolha uma pasta com o botão **Procurar**.
- O Plutao salva essa escolha e restaura automaticamente na próxima abertura.
- Se você trocar a pasta depois, a nova escolha passa a ser lembrada.

As preferências ficam em:

`%LOCALAPPDATA%\Plutao\settings.json`

## Compilação automática

Não é mais necessário executar `build-release.bat` no fluxo normal.

Depois de copiar esta versão para o repositório:

1. Abra o GitHub Desktop.
2. Confira os arquivos em **Changes**.
3. Faça **Commit to main**.
4. Faça **Push origin**.
5. Abra a aba **Actions** no GitHub e aguarde `Build Plutao Release` ficar verde.
6. Abra **Releases** e entre em **Plutao - versão mais recente**.
7. Baixe `Plutao.exe` ou `Plutao-win-x64.zip`.

O `build-release.bat` continua no projeto apenas como alternativa local de emergência.
