using System.Text.Json;

namespace NexoLoL.Core;

// Item sets exported by Blitz have a champion key, but no trustworthy queue or patch.
// The caller binds those two fields to the game in which the set was observed.
public static class LocalArsenals
{
    public static string DirectoryFor(string root, BuildQuery q)
    {
        var digits = q.Patch.Split('.');
        if (digits.Length < 2 || !int.TryParse(digits[0], out int major) || !int.TryParse(digits[1], out int minor))
            throw new InvalidDataException("Patch desconhecido.");
        if (major is >= 20 and < 30) major -= 10;
        return Path.Combine(root, "arsenais", $"{major}.{minor}", q.Mode.ToString(),
            ClientImporter.NormalizeRole(q.Role), q.ChampionId.ToString());
    }

    public static BuildVariant Read(string json, string champion)
    {
        using var data = JsonDocument.Parse(json);
        return Read(data.RootElement,champion);
    }

    static BuildVariant Read(JsonElement root,string champion)
    {
        if (!string.Equals(root.GetProperty("championKey").GetString(), champion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O campeão deste arquivo difere da build aberta.");
        var title = root.GetProperty("title").GetString();
        if (title is null || !title.StartsWith("Blitz: ", StringComparison.Ordinal) || title.Length is < 8 or > 100)
            throw new InvalidDataException("Título de arsenal Blitz inválido.");
        var blocks = new Dictionary<string,List<ItemChoice>>(StringComparer.Ordinal);
        foreach (var block in root.GetProperty("blocks").EnumerateArray())
        {
            string name = block.GetProperty("type").GetString() ?? "";
            var items = new List<ItemChoice>();
            foreach (var item in block.GetProperty("items").EnumerateArray())
            {
                if (!int.TryParse(item.GetProperty("id").ToString(), out int id) || id <= 0 ||
                    !int.TryParse(item.GetProperty("count").ToString(), out int count) || count is < 1 or > 99)
                    throw new InvalidDataException("Item do arsenal inválido.");
                items.Add(new(id, count));
            }
            if (!blocks.TryAdd(name, items)) throw new InvalidDataException("Bloco de itens duplicado.");
        }
        if (!blocks.TryGetValue("Core Final Build", out var core) || core.Count == 0 ||
            !blocks.TryGetValue("Starting Items", out var start) || start.Count == 0 ||
            !blocks.TryGetValue("Core Build Order", out var order) || order.Count == 0)
            throw new InvalidDataException("O arsenal está incompleto.");
        return new(title[7..].Trim(), start, core, blocks.GetValueOrDefault("Situational items that are also good") ?? [],
            Boots:blocks.GetValueOrDefault("Boots Options"), PurchaseOrder:order, ArsenalSource:"Blitz · arsenal local");
    }

    public static BuildResult Include(BuildResult result, string root, IReadOnlyDictionary<int,ItemInfo>? catalog=null)
    {
        var doc = result.Document;
        if (string.IsNullOrEmpty(doc.Query.Patch)) return result;
        var variants = new List<(BuildVariant Build,bool Preferred)>();
        try {
            string folder = DirectoryFor(root, doc.Query);
            if (!Directory.Exists(folder)) return result;
            foreach (string path in Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal).Take(12))
            {
                try {
                    if (new FileInfo(path).Length > 128_000) continue;
                    using var metadata=JsonDocument.Parse(File.ReadAllText(path));
                    var set = Read(metadata.RootElement, doc.Query.Slug);
                    if(catalog is not null && set.Start.Concat(set.Core).Concat(set.Alternatives).Concat(set.PurchaseOrder??[]).Concat(set.Boots??[])
                        .Any(item=>!catalog.ContainsKey(item.Id)))continue;
                    if (variants.Any(v => v.Build.Name == set.Name)) continue;
                    bool preferred=metadata.RootElement.TryGetProperty("priority",out var priority) && priority.ValueKind==JsonValueKind.True;
                    var original = doc.Variants[0];
                    variants.Add((set with { Runes=original.Runes, SummonerSpells=original.SummonerSpells,
                        SkillPriority=original.SkillPriority, SkillOrder=original.SkillOrder },preferred));
                } catch (Exception e) when (e is IOException or JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException) { }
            }
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { return result; }
        return variants.Count == 0 ? result : result with { Document=doc with {
            Variants=[..variants.OrderByDescending(v=>v.Preferred).Select(v=>v.Build),..doc.Variants] } };
    }
}
