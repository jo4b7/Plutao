# Plutao v0.4.3

## Pastas de Vídeo e Áudio

O Plutao não define mais um disco/pasta padrão para os downloads.

Na primeira execução:
1. Clique em **Procurar** ao lado de Vídeos.
2. Escolha a pasta desejada.
3. Faça o mesmo para Áudios.

Esses caminhos ficam salvos e são restaurados automaticamente quando o Plutao abrir novamente.

Arquivo de preferências:

`%LOCALAPPDATA%\Plutao\settings.json`

## Não precisa mais executar build-release.bat

A pasta `.github/workflows` contém a compilação automática.

Depois de copiar esta atualização:
1. Faça Commit no GitHub Desktop.
2. Faça **Push origin**.
3. O GitHub Actions compila o Plutao sozinho.
4. No GitHub, abra **Releases** e entre em **Plutao - versão mais recente**.
5. Baixe `Plutao.exe` ou `Plutao-win-x64.zip`.

A partir daí, para abrir o programa basta executar `Plutao.exe`.

O `build-release.bat` pode continuar existindo apenas como alternativa caso você queira compilar localmente.
