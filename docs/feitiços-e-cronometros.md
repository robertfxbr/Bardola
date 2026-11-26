# Feitiços e cronômetros — 0.4.0

Pedido: importar feitiços automaticamente como itens/runas e oferecer cronômetros como nas referências. A implementação usa a recomendação `summary.pick/win.sums.ids` efetivamente observada na página LoLalytics, com validação do campeão, modo e função já existente. Flash pode ficar no D ou F; pares sem Flash não recebem Flash artificialmente. Escrita apenas por PATCH da própria seleção, com revalidação de conta/fase/campeão e leitura de confirmação. A ausência de recomendação validada não autoriza adivinhar o par.

O cliente instalado expõe `HUD.MinimapEnableAllTimers` (confirmado em leitura) e a operação `PatchLolGameSettingsV1GameSettings` no `/help`. O Bardo.gg usa essa opção para os cronômetros automáticos de todos os acampamentos, com a implementação nativa e os tempos do próprio LoL. Alterações preservam as outras preferências e são conferidas por leitura. O app espera sair de uma partida para aplicar alterações.

As interfaces públicas Live Client Data documentam eventos de objetivos/inibidores, mas não eventos de coleta de relíquias de cura do ARAM. Um painel de contagens por eventos deve identificar sua fonte e não inventar disponibilidade de um objetivo. Cronômetros manuais de cura, se oferecidos, precisam ser identificados como manuais; não equivalem ao recurso automático das referências. Não acessar memória de processo ou timers de habilidades inimigas.

Fontes: https://developer.riotgames.com/docs/lol ; https://www.leagueoflegends.com/en-us/news/game-updates/patch-25-17-notes/ ; página pública https://lolalytics.com/lol/zilean/build/?tier=emerald_plus&lane=support ; endpoints locais indicados acima. As observações são por requisições de leitura e inspeção de código; não foi executado um motor REA nem houve decompilação do Blitz.

Validação: parser de feitiços, posição do Flash, modo incompatível, cancelamento/troca de conta/fase, confirmação de escrita, preservação de configurações e uso do atualizador existente. Sem abrir UI ou iniciar partidas automaticamente.
