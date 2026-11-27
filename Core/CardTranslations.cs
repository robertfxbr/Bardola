using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexoLoL.Core;

public static class CardTranslations
{
    sealed record Entry(int Id,[property:JsonPropertyName("nameTRA")] string? Title);

    public static Dictionary<string,string> Pair(string english,string portuguese)
    {
        var options=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
        var originals=(JsonSerializer.Deserialize<Entry[]>(english,options)??[])
            .Where(e=>e.Id>0 && !string.IsNullOrWhiteSpace(e.Title)).ToLookup(e=>e.Id,e=>e.Title!);
        var translated=JsonSerializer.Deserialize<Entry[]>(portuguese,options)??[];
        var pairs=from card in translated
                  where !string.IsNullOrWhiteSpace(card.Title)
                  from original in originals[card.Id]
                  group original by card.Title! into titles
                  let names=titles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                  where names.Length==1
                  select (Portuguese:titles.Key,English:names[0]);
        return pairs.ToDictionary(p=>p.Portuguese,p=>p.English);
    }
}
