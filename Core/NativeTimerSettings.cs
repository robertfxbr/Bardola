using System.Text.Json.Nodes;

namespace NexoLoL.Core;
public static class NativeTimerSettings
{
    const string Path="/lol-game-settings/v1/game-settings";
    public static async Task ConfigureAsync(ILeagueApi api,bool enabled,CancellationToken ct=default){
        string owner=(await api.GetAsync("/lol-summoner/v1/current-summoner",ct))?["puuid"]?.ToString()??"";
        if(owner=="")throw new InvalidDataException("Conecte o cliente do LoL para configurar os cronômetros.");
        await Guard(api,owner,ct);
        var settings=await api.GetAsync(Path,ct);
        if(settings?["HUD"]?["MinimapEnableAllTimers"] is not JsonValue value||!value.TryGetValue<bool>(out bool current))throw new InvalidDataException("Esta versão do LoL não expôs a opção de cronômetros do minimapa.");
        if(current==enabled)return;
        await Guard(api,owner,ct);ct.ThrowIfCancellationRequested();
        await api.SendAsync(HttpMethod.Patch,Path,new JsonObject{["HUD"]=new JsonObject{["MinimapEnableAllTimers"]=enabled}},ct);
        var confirmation=await api.GetAsync(Path,ct);
        if(confirmation?["HUD"]?["MinimapEnableAllTimers"] is not JsonValue actual||!actual.TryGetValue<bool>(out bool result)||result!=enabled)throw new InvalidDataException("O LoL não confirmou a configuração dos cronômetros.");
    }
    static async Task Guard(ILeagueApi api,string owner,CancellationToken ct){
        string phase=(await api.GetAsync("/lol-gameflow/v1/gameflow-phase",ct))?.ToString()??"";
        if(phase is not ("None" or "Lobby" or "ChampSelect" or "EndOfGame" or "PreEndOfGame"))throw new InvalidDataException("Aguardando o saguão para configurar os cronômetros.");
        if((await api.GetAsync("/lol-summoner/v1/current-summoner",ct))?["puuid"]?.ToString()!=owner)throw new InvalidDataException("A conta mudou; configuração adiada.");
    }
}
