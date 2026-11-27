# Bardo.gg · pedido de produção para League of Legends

## Texto para o formulário da Riot

### Product name

Bardo.gg

### Product description and user flow

Bardo.gg is an independent, Portuguese-language Windows companion for League of Legends. Players can look up a Riot ID on the Brazil platform and see the account level, ranked Solo/Duo and Flex standings, and recent supported public matches, even while the League client is closed. Each match summary shows the champion, result, K/D/A, CS and duration. A lookup does not verify account ownership. Players who connect their active League client can also view the last synchronized personal profile on that PC and remove it using Disconnect.

### API access requested

We request production access for League of Legends to ACCOUNT-V1 (resolve a Riot ID to its PUUID), SUMMONER-V4 (level and profile icon), LEAGUE-V4 (ranked entries by PUUID), and MATCH-V5 (recent match IDs and match details). The prototype uses AMERICAS routing for accounts and matches, and BR1 for summoner and ranked data. It examines the five most recent match IDs, returns the queried participant’s statistics from supported matchmade queues, and omits custom and unsupported queues. The API key is held by the backend. The desktop app receives a profile summary. Successful searches are cached for three minutes. The service honors Riot’s Retry-After response.

### Desktop features and sources for review

The desktop companion uses the local League Client API to identify the active player, read ranked data, mastery and match history, and show the post-game scoreboard and timeline. During champion select, it can suggest multiple picks based on role, mastery, aggregate ranked statistics and publicly confirmed enemy champion picks. Ban suggestions exclude allies’ declared champion intents. Players make their own picks and bans. Optional pre-game imports can update rune pages, item sets and summoner spells in the local client. The app uses the local Live Client Data API for the player’s in-game dashboard. Optional on-screen features read visible card titles and scoreboard timers with local OCR. ARAM Mayhem card grades come from a third-party champion/patch tier list. Augment win rates are not displayed. We request review of these features as part of the complete product. The ranked draft statistics come from LoLalytics, separately from the Riot Web API.

### Riot Sign On follow-up

After approval of our production application, we would like to request an RSO client for League of Legends. Our intended flow opens Riot authorization in the system browser, returns to an HTTPS callback we control, and uses ACCOUNT-V1 accounts/me to identify the player who authorized the app. Our use case is verified ownership of a Bardo.gg profile. The prototype currently supports public lookup and local-client connection. RSO credentials, a live callback and the signed-in flow are pending.

## Informações do responsável antes de enviar

| Campo | Preenchimento |
| --- | --- |
| Site funcional e download | Inserir a URL HTTPS em um domínio controlado pelo responsável. A propriedade de bardo.gg ainda precisa ser confirmada. |
| Contato do produto | Inserir o e-mail que o responsável usa para suporte e privacidade. |
| Termos e política de privacidade | Inserir as URLs públicas vigentes. Descrever o cache do servidor, o perfil salvo no PC e a remoção ao desconectar. Conferir também a retenção de logs do provedor escolhido. |
| Demonstração | Mostrar uma consulta por Riot ID com o cliente fechado, as fontes no draft e o pós-jogo. Usar dados de demonstração identificados ou uma conta autorizada. |
| Código de apoio | https://github.com/robertfxbr/Bardo.gg |
| Retorno RSO | Definir o endereço HTTPS real após escolher o domínio e o ambiente. |
| Verificação do domínio | Após o cadastro, usar o conteúdo e o endereço fornecidos pela Riot no portal. |

A solicitação inicial é para a chave de produção. A Riot exige site verificável e acesso aos termos e à política de privacidade. O convite para solicitar o cliente RSO vem após a aprovação da aplicação. [FAQ oficial sobre produção e RSO](https://developer.riotgames.com/docs/faqs) · [Portal de registro](https://developer.riotgames.com/) · [Políticas para League of Legends](https://developer.riotgames.com/docs/lol)
