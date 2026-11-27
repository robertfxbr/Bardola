namespace NexoLoL.Core;

public sealed record RegionalComparison(int Queue, double Kda, int Matches, double? Average,
    double? TopPercent, int PlayerGames, double PlayerAverage, int RankedPlayers, int? PlayerRank)
{
    public const int WindowDays = 30, MinimumMatches = 30, MinimumPlayerGames = 5, MinimumPlayers = 10;
}

public sealed record LeaguePlatform(string Id, string Route, string SiteCode)
{
    public static LeaguePlatform? Find(string? region) => region?.Trim().ToUpperInvariant() switch
    {
        "BR" or "BR1" => new("BR1", "americas", "br"),
        "NA" or "NA1" => new("NA1", "americas", "na"),
        "LAN" or "LA1" => new("LA1", "americas", "lan"),
        "LAS" or "LA2" => new("LA2", "americas", "las"),
        "EUW" or "EUW1" => new("EUW1", "europe", "euw"),
        "EUNE" or "EUN1" => new("EUN1", "europe", "eune"),
        "TR" or "TR1" => new("TR1", "europe", "tr"),
        "RU" => new("RU", "europe", "ru"),
        "ME" or "ME1" => new("ME1", "europe", "me"),
        "KR" => new("KR", "asia", "kr"),
        "JP" or "JP1" => new("JP1", "asia", "jp"),
        "OCE" or "OC1" => new("OC1", "sea", "oce"),
        "SEA" or "SG" or "SG2" => new("SG2", "sea", "sg"),
        "TW" or "TW2" => new("TW2", "sea", "tw"),
        "VN" or "VN2" => new("VN2", "sea", "vn"),
        _ => null
    };
}
