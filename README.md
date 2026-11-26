# Bardo.gg

Companheiro independente para **League of Legends**, em português, sem Overwolf e sem anúncios.

- Builds por campeão, modo, função, elo e patch, com fonte e amostra visíveis.
- Importação automática de itens, runas e feitiços na seleção de campeão; Flash no D ou F e backup da página de runas reutilizada.
- Perfil da conta ativa no cliente, ranques e histórico recente, sem pedir a senha do LoL.
- Catálogo e imagens oficiais, favoritos, cache validado e exportação de conjuntos de itens.
- Painel opcional de estatísticas próprias, build e prioridade de habilidades para modo sem bordas/segundo monitor.
- Cronômetros automáticos no minimapa usando o modo avançado do próprio LoL; contadores de cura do ARAM sincronizados pelo placar.
- Atualizações assinadas em segundo plano, usadas na próxima abertura pelo mesmo atalho.

## Instalar

Windows 10 (2004 ou posterior) ou Windows 11 x64 com **[.NET Desktop Runtime 10](https://dotnet.microsoft.com/download/dotnet/10.0)**. Baixe `Instalar-Bardo.gg.exe` em [Releases](https://github.com/robertfxbr/Bardo.gg/releases/latest), execute uma vez e use o atalho **Bardo.gg**. Não são necessários direitos de administrador. O app não substitui seu cliente do League.

O instalador contém o aplicativo inicial e funciona sem internet para instalar. Builds e imagens precisam de internet na primeira consulta. Depois da instalação, novas versões publicadas são baixadas automaticamente; a aplicação ocorre ao fechar e abrir o Bardo.gg, preservando preferências e favoritos.

## Estado do projeto

Versão inicial em desenvolvimento. Importação validada com testes de protocolo; validação em seleção real de campeão e do painel durante uma partida ainda pendente. Não há garantia de compatibilidade permanente com interfaces locais do LoL ou páginas de builds, que podem mudar. Não é um produto aprovado pela Riot Games.

Os cronômetros da selva são os nativos do LoL. A opção **Curas do ARAM no minimapa** lê os tempos visíveis no Tab por OCR local e conta sobre as relíquias em modo sem bordas. Calibre em Ajustes na primeira partida e segure Tab para sincronizar. A rota inicial da jungle usa a recomendação nativa do League para o campeão. Ultimates aliados, comparação de inventários e análise de outros jogadores na tela de carregamento **ainda não estão implementados**. As configurações indicam essas limitações.

## Compilar e testar

Com Windows e .NET SDK 10, na raiz do repositório:

```powershell
dotnet build Desktop/NexoLoL.Desktop.csproj
dotnet run --project Tests/NexoLoL.Tests.csproj
dotnet run --project ImportTests/NexoLoL.ImportTests.csproj
dotnet run --project UiTests/NexoLoL.UiTests.csproj
dotnet run --project UpdateTests/NexoLoL.UpdateTests.csproj
```

Os nomes internos `NexoLoL` são legados. Os testes WPF renderizam em memória, sem abrir janelas. Os testes de importação usam um cliente simulado; não escrevem em uma conta real. As opções `--live`/`--account-live` dos testes principais fazem consultas às fontes e ao cliente, sem escritas no jogo. `--native-timers-live` no projeto ImportTests é uma verificação intencional de escrita: ativa o modo avançado de cronômetros no cliente real. Não use essa opção para executar apenas testes simulados.

## Publicar atualizações

```powershell
./Publish-Update.ps1 -Version 0.4.1
# Após conferir os arquivos gerados:
./Publish-Update.ps1 -Version 0.4.1 -Publish
```

O script compila o app e instalador, assina o manifesto e pode publicar via GitHub CLI. A chave privada deve corresponder à chave pública fixada em `UpdateCore/ReleaseTrust.cs`; ela fica em `%LOCALAPPDATA%/Bardo.gg-Publisher/release-key.pem`, fora deste repositório. Sem essa chave é possível compilar e testar o código, mas não assinar atualizações aceitas pelas instalações existentes. Faça backup seguro da chave. Não gere outra chave para uma atualização da mesma instalação.

Quem criar um fork deve configurar seu próprio repositório e par de chaves antes da primeira distribuição. Uma mesma versão não deve ser republicada com conteúdo diferente. Detalhes do atualizador estão em [docs/bardo-release.md](docs/bardo-release.md); feitiços e cronômetros em [docs/feitiços-e-cronometros.md](docs/feitiços-e-cronometros.md).

## Privacidade e licença

O app usa a sessão local do cliente; não pede senha, não salva a credencial da sessão e não envia dados da conta a um servidor do Bardo.gg. Preferências, cache, sessões e backups ficam em `%LOCALAPPDATA%/Bardo.gg`. Consultas de builds e catálogo são feitas diretamente às fontes indicadas. Não lê memória de processo nem injeta código no jogo.

Código original sob **GPL-3.0**, conforme [LICENSE](LICENSE). Marcas, imagens e dados de terceiros seguem seus próprios termos: veja [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Bardo.gg is not endorsed by Riot Games and does not reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games and all associated properties are trademarks or registered trademarks of Riot Games, Inc.
