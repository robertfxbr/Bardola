# API de perfis do Bardo.gg

A primeira versão atende BR1. Em **Meu perfil**, use **Consultar Riot ID · BR** com nome e tag. A consulta pública funciona com o jogo fechado quando o servidor está configurado. O botão **Conectar conta do LoL** lê a sessão local e salva o último resumo em preferências no PC. **Desconectar** remove essa cópia. Um perfil consultado pelo nome não é vinculado à sessão local.

## Executar no Windows

Instale o SDK .NET 10. Para um teste privado, entre no Developer Portal e obtenha uma chave de desenvolvimento. No PowerShell 7, execute na raiz do projeto:

```powershell
$env:RIOT_API_KEY = Read-Host 'Chave de desenvolvimento da Riot' -MaskInput
dotnet run --project Api
```

O servidor escuta em `http://127.0.0.1:5219`. O app desktop usa esse endereço por padrão. Para um servidor já implantado, configure `BARDO_API_URL` no ambiente do desktop antes de abrir o aplicativo. Use HTTPS fora da máquina local. Mantenha a chave somente no ambiente do processo servidor. Uma chave de desenvolvimento expira em 24 horas e serve para testes privados.

## Consultas

No pacote compilado para Windows, extraia o ZIP e execute `dotnet Bardo.Api.dll` na pasta extraída, com o ASP.NET Core Runtime 10 instalado e a variável `RIOT_API_KEY` definida na mesma sessão.

```powershell
Invoke-RestMethod 'http://127.0.0.1:5219/health'
$id = [Uri]::EscapeDataString('Nome#TAG')
Invoke-RestMethod "http://127.0.0.1:5219/v1/br1/profile?riotId=$id"
```

O serviço resolve a identidade em ACCOUNT-V1, consulta perfil e ranque em BR1 e procura partidas em AMERICAS. Entre as cinco partidas mais recentes, inclui as de BR1 nas filas 400, 420, 430, 440, 450, 490 e 2400, desde que o tipo seja MATCHED_GAME e o PUUID consultado esteja entre os participantes. A resposta contém dados do jogador consultado. Campos de identificação de outros participantes e o PUUID não são enviados ao desktop.

Respostas bem-sucedidas ficam três minutos em memória. A consulta pode devolver 400 para um Riot ID inválido, 404 para conta ou perfil ausente no Brasil, 429 para limite de consultas, 503 para chave indisponível ou recusada e 502/504 para falha ou demora da fonte. `/health` indica que o processo está ativo. O limite inicial do protótipo é de oito consultas por minuto por instância. Dimensionar esse limite e a hospedagem para o tráfego aprovado antes de abrir ao público.

## Verificação

`dotnet run --project ApiTests -c Release` inicia o servidor real em uma porta temporária e substitui apenas o transporte HTTP de saída por respostas simuladas. `dotnet run --project UiTests -c Release` exercita a interface WPF. A consulta autenticada aos servidores da Riot requer chave válida. [Referência de APIs](https://developer.riotgames.com/apis) · [Solicitação de produção](riot-production-request.md)
