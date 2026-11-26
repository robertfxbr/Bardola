using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NexoLoL.Core;

public static class BuildParsers
{
    public static bool SamePatch(string a, string b)
    {
        static string Normalize(string p) {
            var parts=p.Split('.');
            if(parts.Length<2 || !int.TryParse(parts[0],out var major)) return p;
            if(major is >=20 and <30) major-=10; // Patch de marketing 26.x corresponde ao Data Dragon 16.x.
            return $"{major}.{parts[1]}";
        }
        return Normalize(a)==Normalize(b);
    }

    public static string Url(BuildQuery q) => q.Mode == GameMode.Mayhem
        ? $"https://lazyelo.com/aram-mayhem/builds/{MayhemSlug(q.Slug)}"
        : $"https://lolalytics.com/lol/{q.Slug}/{(q.Mode==GameMode.Aram?"aram/":"")}build/?tier={q.Tier}{(q.Mode==GameMode.Rift && q.Role!="default"?$"&lane={q.Role}":"")}";

    public static string MayhemSlug(string slug) => slug switch { "monkeyking"=>"wukong", "nunu"=>"nunu-willump", "drmundo"=>"dr-mundo", "jarvaniv"=>"jarvan-iv", "aurelionsol"=>"aurelion-sol", "missfortune"=>"miss-fortune", "masteryi"=>"master-yi", "twistedfate"=>"twisted-fate", "tahmkench"=>"tahm-kench", "xinzhao"=>"xin-zhao", "leesin"=>"lee-sin", "reksai"=>"rek-sai", "kogmaw"=>"kog-maw", "chogath"=>"cho-gath", "velkoz"=>"vel-koz", "khazix"=>"kha-zix", "belveth"=>"bel-veth", "renata" or "renataglasc"=>"renata-glasc", "kaisa"=>"kai-sa", "ksante"=>"k-sante", _=>slug };

    public static BuildDocument ParseLolalytics(string html, BuildQuery query)
    {
        if(query.Mode==GameMode.Mayhem) throw new InvalidDataException("Desordem exige fonte específica.");
        var m=Regex.Match(html,"<script\\b[^>]*type=[\"']qwik/json[\"'][^>]*>(.*?)</script>",RegexOptions.Singleline);
        if(!m.Success) throw new InvalidDataException("A estrutura da fonte mudou. Abra a página original ou tente atualizar.");
        try {
            using var doc=JsonDocument.Parse(m.Groups[1].Value);
            var pool=doc.RootElement.GetProperty("objs");
            JsonNode? Resolve(JsonElement node,int depth=0) {
                if(depth>40) throw new InvalidDataException("Referência circular na fonte.");
                JsonNode? Ref(JsonElement value) {
                    if(value.ValueKind!=JsonValueKind.String) return Resolve(value,depth+1);
                    string text=value.GetString()!;
                    int index=0;
                    foreach(char c in text) {int digit=c is >= '0' and <= '9'?c-'0':c is >= 'a' and <= 'z'?c-'a'+10:-1;
                        if(digit<0 || index>(pool.GetArrayLength()+1)/36) throw new InvalidDataException("Referência inválida na fonte."); index=index*36+digit;}
                    if(index>=pool.GetArrayLength()) throw new InvalidDataException("Referência ausente na fonte.");
                    return Resolve(pool[index],depth+1);
                }
                if(node.ValueKind==JsonValueKind.Object) {var o=new JsonObject();foreach(var p in node.EnumerateObject())o[p.Name]=Ref(p.Value);return o;}
                if(node.ValueKind==JsonValueKind.Array) {var a=new JsonArray();foreach(var e in node.EnumerateArray())a.Add(Ref(e));return a;}
                return JsonNode.Parse(node.GetRawText()); // Strings no pool são valores literais, nunca novas referências.
            }
            var candidates=pool.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.Object && x.TryGetProperty("header",out _) && x.TryGetProperty("summary",out _)).ToList();
            if(candidates.Count!=1) throw new InvalidDataException("Não foi possível identificar uma build única na fonte.");
            // Resolver apenas header/summary: não percorrer grafo de runtime da página.
            var selected=new JsonObject();
            foreach(var p in candidates[0].EnumerateObject().Where(p=>p.Name is "header" or "summary")) {
                int idx=Base36(p.Value.GetString()!);selected[p.Name]=Resolve(pool[idx]);
            }
            var header=selected["header"]!;string patch=S(header["patch"]),role=S(header["lane"]);
            var contexts=pool.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.Object && new[]{"path","cid","tier","mode","patch","champion","lane"}.All(k=>x.TryGetProperty(k,out _))).ToList();
            if(contexts.Count!=1)throw new InvalidDataException("Não foi possível validar os filtros da consulta na fonte.");
            string Context(string key)=>S(Resolve(pool[Base36(contexts[0].GetProperty(key).GetString()!)]));
            if(Context("champion")!=query.Slug || Context("cid")!=query.ChampionId.ToString())throw new InvalidDataException("Contexto de campeão divergente na fonte.");
            if(Context("tier")!=query.Tier)throw new InvalidDataException("A fonte devolveu outra faixa de rank.");
            if(query.Patch!="" && !SamePatch(Context("patch"),query.Patch))throw new InvalidDataException("Contexto de patch divergente na fonte.");
            if(Context("mode")!=(query.Mode==GameMode.Rift?"ranked":"aram"))throw new InvalidDataException("Contexto de modo divergente na fonte.");
            if(I(header["cid"])!=query.ChampionId) throw new InvalidDataException("A fonte devolveu outro campeão.");
            if(I(header["queue"])!=(query.Mode==GameMode.Rift?420:450)) throw new InvalidDataException("A fonte devolveu outro modo de jogo.");
            if(query.Patch!="" && !SamePatch(patch,query.Patch)) throw new InvalidDataException($"A fonte ainda está no patch {patch}; catálogo {query.Patch}.");
            if(query.Mode==GameMode.Rift && query.Role!="default" && role!=query.Role) throw new InvalidDataException("A fonte não tem dados para a função escolhida.");
            var variants=new List<BuildVariant>();
            foreach(var key in new[]{"pick","win"}) {
                var v=selected["summary"]?[key]; if(v is null) continue;
                var items=v["items"];var start=Ids(items?["start"]?["set"]).GroupBy(x=>x).Select(g=>new ItemChoice(g.Key,g.Count())).ToList();
                var core=Ids(items?["core"]?["set"]).Select(x=>new ItemChoice(x)).ToList();
                if(core.Count==0) throw new InvalidDataException("A fonte não retornou os itens principais.");
                var alternatives=new List<ItemChoice>();
                foreach(var slot in new[]{"item4","item5","item6"})
                    if(items?[slot] is JsonArray a) foreach(var it in a) {int id=I(it?["id"]);if(id>0 && !core.Any(c=>c.Id==id) && !alternatives.Any(c=>c.Id==id)) alternatives.Add(new(id,1,D(it?["wr"]),I(it?["n"])));}
                var runes=v["runes"]?["set"];
                var primary=Ids(runes?["pri"]);var secondary=Ids(runes?["sec"]);var mods=Ids(runes?["mod"]);
                RunePage? page=primary.Count>0 && secondary.Count>0 ? new(Style(primary[0]),Style(secondary[0]),[..primary,..secondary,..mods]) : null;
                string sequence=S(v["skillorder"]?["id"]);
                sequence=new string(sequence.Select(c=>c switch {'1'=>'Q','2'=>'W','3'=>'E','4'=>'R',_=>' '}).Where(c=>c!=' ').ToArray());
                variants.Add(new(key=="pick"?"Mais jogada":"Alternativa por vitórias",start,core,alternatives,S(v["skillpriority"]?["id"]),sequence,page,I(items?["core"]?["n"]),D(items?["core"]?["wr"]),SummonerSpells:Ids(v["sums"]?["ids"])));
            }
            if(variants.Count==0) throw new InvalidDataException("Sem recomendações disponíveis para este contexto.");
            return new(query,patch,"LoLalytics",Url(query),DateTimeOffset.UtcNow,variants,D(header["wr"]),I(header["n"]),role,Ids(header["counters"]?["strong"]),Ids(header["counters"]?["weak"]),
                Note:"Estatísticas agregadas da fonte. Vitórias de itens tardios sofrem viés de partidas longas; compare também a amostra.");
        } catch(InvalidDataException) {throw;} catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentOutOfRangeException) {
            throw new InvalidDataException("Não foi possível validar os dados da página. Nenhuma build foi substituída.",e);
        }
    }

    public static BuildDocument ParseMayhem(string html, BuildQuery query)
    {
        if(query.Mode!=GameMode.Mayhem) throw new InvalidDataException("Esta fonte é exclusiva de Desordem.");
        string expected=$"https://lazyelo.com/aram-mayhem/builds/{MayhemSlug(query.Slug)}";
        if(!Regex.IsMatch(html,$"<link\\b[^>]*href=[\"']{Regex.Escape(expected)}[\"']",RegexOptions.IgnoreCase)) throw new InvalidDataException("A fonte devolveu outra página de campeão.");
        // Apenas HTML visível. Não executar scripts nem consultar APIs privadas.
        string visible=Regex.Replace(html,"<script\\b[^>]*>.*?</script>","",RegexOptions.Singleline|RegexOptions.IgnoreCase);
        string patch=Regex.Match(visible,@"(?:Patch|patch)\s*(?:<!--.*?-->\s*)*(\d{2}\.\d{1,2})",RegexOptions.Singleline).Groups[1].Value;
        if(patch=="" || (query.Patch!="" && !SamePatch(patch,query.Patch))) throw new InvalidDataException($"Patch da fonte não validado ({patch}).");
        string Section(string title,bool onlyList=true) {
            var m=Regex.Match(visible,$"<h4\\b[^>]*>{title}</h4>(.*?)(?=<h[234]\\b|$)",RegexOptions.Singleline|RegexOptions.IgnoreCase);
            if(!m.Success)return "";
            if(!onlyList)return m.Groups[1].Value;
            var list=Regex.Match(m.Groups[1].Value,"<ol\\b[^>]*>(.*?)</ol>",RegexOptions.Singleline|RegexOptions.IgnoreCase);
            return list.Success?list.Groups[1].Value:"";
        }
        List<ItemChoice> ItemImages(string text) => Regex.Matches(text,@"(?:/img/item/|/img/items/)(\d+)\.(?:png|webp)",RegexOptions.IgnoreCase).Select(m=>int.Parse(m.Groups[1].Value)).Distinct().Select(id=>new ItemChoice(id)).ToList();
        var start=ItemImages(Section("Start"));var core=ItemImages(Section("Core items, in buying order"));
        var boots=ItemImages(Regex.Match(Section("Core items, in buying order",false),"<table\\b.*?</table>",RegexOptions.Singleline|RegexOptions.IgnoreCase).Value);
        var alternatives=ItemImages(Section("Situational, after the core")).Where(x=>!core.Any(c=>c.Id==x.Id)).ToList();
        if(core.Count<2 || start.Count==0) throw new InvalidDataException("Não foi possível validar os itens desta build de Desordem.");
        string clean=Clean(visible);
        string priority=Regex.Match(clean,@"Max\s+([QWE])\s*\([^)]*\)\s*first,\s*then\s*([QWE]),\s*then\s*([QWE])",RegexOptions.IgnoreCase) is {Success:true} sk ? string.Concat(sk.Groups[1].Value,sk.Groups[2].Value,sk.Groups[3].Value) : "";
        var cards=new List<string>();
        var cardsSection=Regex.Match(visible,"Best augments for.*?(?=<h2|$)",RegexOptions.Singleline|RegexOptions.IgnoreCase).Value;
        foreach(Match m in Regex.Matches(cardsSection,@"href=""/aram-mayhem/augments/[^""/]+""[^>]*>(.*?)</a>",RegexOptions.Singleline)) {
            string name=Clean(m.Groups[1].Value);if(name!="" && !cards.Contains(name))cards.Add(name);if(cards.Count==9)break;
        }
        var spellSection=Regex.Match(visible,@"<h3\b[^>]*>Summoner spells</h3>(.*?)(?=<h[234]\b|$)",RegexOptions.Singleline|RegexOptions.IgnoreCase).Groups[1].Value;
        var firstPair=Regex.Match(spellSection,@"<li\b[^>]*>(.*?)</li>",RegexOptions.Singleline|RegexOptions.IgnoreCase).Groups[1].Value;
        var spells=Regex.Matches(firstPair,@"/img/spell/(Summoner[A-Za-z]+)\.png",RegexOptions.IgnoreCase).Select(m=>SpellRules.Id(m.Groups[1].Value)).ToList();
        string? date=Regex.Match(html,"\"dateModified\":\"([^\"]+)\"") is {Success:true} dt?dt.Groups[1].Value:null;
        return new(query,patch,"LazyElo / ARAMKit",expected,DateTimeOffset.UtcNow,[new("Caminho principal de Desordem",start,core,alternatives,priority,Boots:boots,SummonerSpells:spells)],
            Augments:cards,Note:"Desordem desativa runas. Fonte específica deste modo; aumentos exibidos sem taxas de vitória.",SourceDate:date);
    }

    static int Base36(string text) {int n=0;foreach(char c in text)n=checked(n*36+(c<='9'?c-'0':c-'a'+10));return n;}
    public static string Clean(string html) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html,"<[^>]+>"," ")),@"\s+"," ").Trim();
    static string S(JsonNode? n)=>n?.ToString()??"";
    static int I(JsonNode? n)=>int.TryParse(S(n),out int v)?v:0;
    static double? D(JsonNode? n)=>double.TryParse(S(n),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double v)?v:null;
    static List<int> Ids(JsonNode? n)=>n is JsonArray a?a.Select(I).Where(x=>x>0).ToList():[];
    static int Style(int perk)=>(perk/100) switch {81=>8100,82=>8200,83=>8300,84=>8400,_=>8000};
}
