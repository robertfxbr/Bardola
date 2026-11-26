# Bardo.gg 0.3.0

Aplicativo independente WPF para Windows x64 e League of Legends, em português. Prioridade: builds por contexto, importação de itens/runas, conexão com o próprio cliente e atualizações sem trocar de atalho.

## Referências visuais

Barra superior compacta, fundo grafite, destaques dourados e verde, importações em linhas com interruptores e recursos de partida em cartões. As capturas fornecidas pelo usuário servem de referência de interação e organização; nenhuma imagem ou marca do Blitz é incluída.

## Atualização

O instalador contém uma versão assinada e instala em `%LOCALAPPDATA%/Bardo.gg`. O atalho aponta sempre para `Bardo.gg.exe`; o aplicativo fica em `app-versions/<versão>/BardoApp.exe`. O app verifica GitHub Releases ao iniciar e a cada 15 minutos. Uma nova versão é baixada, validada e preparada; só é usada ao abrir o app novamente. A atualização não reinicia o aplicativo durante o jogo.

Manifesto e pacote são verificados com ECDSA P-256 e SHA-256. A chave pública está no código. A chave privada de publicação fica fora do repositório e dos instaladores. Extração tem limites e recusa caminhos fora da instalação, links simbólicos e entradas duplicadas. O ponteiro de versão é trocado por escrita atômica e a versão anterior é preservada. A reinstalação verifica e repara o executável; há rollback quando a versão anterior está disponível e íntegra. Falhas de rede mantêm o app atual.

O launcher não atualiza seu próprio protocolo automaticamente. Mudanças futuras incompatíveis no launcher/runtime podem exigir instalador novo. O pacote atual exige .NET Desktop Runtime 10; esse runtime não é instalado pelo Bardo.gg.

## Painel de partida

Janela do Windows opcional, destinada a modo sem bordas ou segundo monitor. Usa dados do próprio jogador pela Live Client Data API. Não injeta código, lê memória de processo ou captura entradas. Estatísticas, itens da build e prioridade de habilidades têm controles separados, desligados por padrão. Uma build só aparece se campeão e modo coincidirem com a partida. A meta de CS/min é escolhida pelo usuário e não é uma comparação com uma população inventada.

Cronômetros de objetivos/ARAM, feitiços automáticos, ultimates aliados, rotas da selva, análises de carregamento e comparação de inventários não foram implementados. Recursos adicionais devem ser verificados com dados disponíveis e políticas vigentes, incluindo https://developer.riotgames.com/docs/lol#game-integrity.

## Validação

Testes de parser/cache/conta, importação com cliente simulado, WPF sem mostrar janelas e atualização em diretório temporário. Leituras locais do cliente já foram verificadas; importação real em seleção de campeão e painel visível durante partida permanecem pendentes de validação de uso.
