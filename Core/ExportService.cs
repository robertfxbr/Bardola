using System.Text.Json;

namespace NexoLoL.Core;

public static class ExportService
{
    public static string CreateItemSet(BuildDocument doc,BuildVariant variant)
    {
        object Block(string title,List<ItemChoice> items)=>new {type=title,items=items.Select(i=>new {id=i.Id.ToString(),count=i.Count}).ToArray()};
        if(variant.Core.Count==0 || variant.Start.Concat(variant.Core).Concat(variant.Alternatives).Any(i=>i.Id<=0 || i.Count<=0))throw new InvalidDataException("Build contém itens inválidos.");
        return JsonSerializer.Serialize(new {
            title=$"Bardo.gg · {doc.Query.Slug} · {doc.Query.Mode} · {doc.Patch}",
            type="custom",map=doc.Query.Mode==GameMode.Rift?"SR":"HA",mode="any",priority=false,
            sortrank=0,associatedMaps=new[]{doc.Query.Mode==GameMode.Rift?11:12},associatedChampions=new[]{doc.Query.ChampionId},preferredItemSlots=Array.Empty<object>(),
            blocks=new[]{Block("Início",variant.Start),Block("Itens principais",variant.Core),Block("Escolhas de botas",variant.Boots??[]),Block("Alternativas situacionais",variant.Alternatives)}
        },new JsonSerializerOptions{WriteIndented=true});
    }
}
