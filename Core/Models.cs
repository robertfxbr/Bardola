namespace NexoLoL.Core;

public enum GameMode { Rift, Aram, Mayhem }
public record BuildQuery(int ChampionId, string Slug, GameMode Mode, string Role = "default", string Tier = "emerald_plus", string Patch = "")
{
    public string CacheKey => $"{ChampionId}_{Mode}_{Role}_{Tier}_{Patch}";
}
public record ItemChoice(int Id, int Count = 1, double? WinRate = null, int Games = 0);
public record RunePage(int PrimaryStyle, int SecondaryStyle, List<int> Perks);
public record BuildVariant(string Name, List<ItemChoice> Start, List<ItemChoice> Core, List<ItemChoice> Alternatives,
    string SkillPriority = "", string SkillOrder = "", RunePage? Runes = null, int Games = 0, double? WinRate = null, List<ItemChoice>? Boots = null, List<int>? SummonerSpells = null);
public record BuildDocument(BuildQuery Query, string Patch, string Source, string SourceUrl, DateTimeOffset FetchedAt,
    List<BuildVariant> Variants, double? WinRate = null, int Games = 0, string Role = "", List<int>? StrongAgainst = null,
    List<int>? WeakAgainst = null, List<string>? Augments = null, string Note = "", string? SourceDate = null);
public record BuildResult(BuildDocument Document, bool FromCache, bool Stale, string Message);
public record Champion(int Id, string Key, string Name, string Title)
{
    public string Slug => Key.ToLowerInvariant();
    public override string ToString() => Name;
}
public record ItemInfo(int Id, string Name, string Description, int Gold, string Icon);
public record RuneInfo(int Id, string Name, string Description, string Icon, int Style = 0);
public record SpellInfo(int Id,string Name,string Description,string Icon);
public sealed class Catalog
{
    public string Version { get; set; } = "";
    public string Patch => string.Join('.', Version.Split('.').Take(2));
    public List<Champion> Champions { get; set; } = [];
    public Dictionary<int, ItemInfo> Items { get; set; } = [];
    public Dictionary<int, RuneInfo> Runes { get; set; } = [];
    public Dictionary<int, SpellInfo> Spells { get; set; } = [];
}
public record GameSnapshot(bool Connected, string Phase, int ChampionId = 0, string Mode = "", double GameTime = 0,
    int Level = 0, double Gold = 0, int Kills = 0, int Deaths = 0, int Assists = 0, int Cs = 0, double Vision = 0,
    List<int>? Items = null, string Role = "default", int Map = 0, string Message = "");
