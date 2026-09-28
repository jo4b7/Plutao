# Changelog

## 0.3.2

- Corrige o erro interno ao analisar respostas `null` do yt-dlp.
- Perfis do Instagram usam fallback automático com gallery-dl quando necessário.
- O fallback lista vídeos/reels e devolve URLs individuais para a seleção.
- Itens selecionados de perfis do Instagram são baixados individualmente pelo yt-dlp.
- gallery-dl é baixado automaticamente somente quando o analisador de perfil precisar dele.
- Mensagens de erro de perfil do Instagram ficaram mais claras, especialmente quando cookies são necessários.
- `build-release.bat` detecta e fecha `Plutao.exe` antes de recompilar.
- Mensagem de falha da compilação não culpa mais o .NET em qualquer erro.


## 0.3.1

- Novo botão **ANALISAR CONTA/PÁGINA**.
- Mostra quantos itens foram encontrados antes do download.
- Nova janela com seleção individual por checkbox.
- Todos os itens vêm marcados por padrão.
- Botões **Marcar todos** e **Desmarcar todos**.
- Mostra quantidade encontrada e quantidade selecionada.
- Download dos itens escolhidos usando `--playlist-items` no link original.
- A seleção é invalidada automaticamente se o link, cookies ou limite forem alterados.
- Mantém a opção de baixar a conta inteira sem analisar previamente.

## 0.3.0

- Modo de página/conta/canal/perfil completo ativado por padrão.
- Um único link de perfil pode expandir para todos os vídeos enumerados pelo extrator.
- Limite opcional de 10, 25, 50, 100, 200 ou Todos.
- Barra de progresso mostra mídia atual dentro de playlists/perfis quando o site fornece índice/total.
