using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NexoLoL.Core;

namespace NexoLoL.Desktop;

public partial class MainWindow
{
    void AddRegionalComparison(StackPanel page, AccountMatch match, Champion? champion)
    {
        var platform = LeaguePlatform.Find(match.Platform);
        var region = platform?.SiteCode.ToUpperInvariant();
        var content = new StackPanel();
        var header = new StackPanel {Orientation=Orientation.Horizontal};
        if (champion != null && platform != null)
        {
            var url = $"https://www.leagueofgraphs.com/rankings/summoners/{champion.Slug}/{platform.SiteCode}";
            var link = Button("Ranking no LeagueOfGraphs ↗", () => OpenUrl(url));
            link.HorizontalAlignment=HorizontalAlignment.Left;
            content.Children.Add(link);
        }
        header.Children.Add(Text($"Comparativo da partida com {champion?.Name ?? "o campeão"}" +
            (region == null ? "" : $" · {region}")+"     ", 14, true));
        double kda = ((double)match.Kills + match.Assists) / Math.Max(1, match.Deaths);
        var line = Text($"Seu KDA nesta partida   {kda.ToString("0.00", Pt)}", 15, true, Accent);
        header.Children.Add(line);
        var info = Text("", 12, false, Muted);
        content.Children.Add(info);
        page.Children.Add(new Expander {Header=header,Content=content,Foreground=Theme("papiro"),
            Padding=new Thickness(8,4,8,4),Background=Brush("#171C25"),Margin=new Thickness(0,3,0,0)});
        if (platform == null || match.TeamId is not (100 or 200) || champion == null)
        {
            info.Text = "O histórico desta partida não trouxe os dados necessários para o comparativo regional.";
            return;
        }
        var retry = new Button {Content="Atualizar comparação"};
        retry.Visibility = Visibility.Collapsed;
        retry.HorizontalAlignment = HorizontalAlignment.Left;
        content.Children.Add(retry);
        async Task Load()
        {
            retry.Visibility = Visibility.Collapsed;
            info.Text = "Buscando a média da região…";
            try
            {
                var uri = BardoApiUri($"/v1/comparisons/{platform.Id}/{match.Id}/{match.TeamId}/{champion.Id}");
                var data = await remote.BytesAsync(uri.AbsoluteUri, 8192, lifetime.Token);
                var result = JsonSerializer.Deserialize<RegionalComparison>(data, JsonSerializerOptions.Web)
                    ?? throw new InvalidDataException();
                if (match.QueueId > 0 && match.QueueId != result.Queue) throw new InvalidDataException();
                var queue = result.Queue switch {
                    420 => "Solo/Duo", 440 => "Flex", 450 => "ARAM", 2400 => "ARAM: Desordem",
                    400 => "Normal alternada", 430 => "Normal às cegas", 490 => "Normal rápida", _ => $"Fila {result.Queue}" };
                var description = new List<string> { $"{queue} · últimos {RegionalComparison.WindowDays} dias · partidas acompanhadas pelo Bardola" };
                if (result.Average is double average && result.TopPercent is double top)
                    line.Text = $"KDA {result.Kda.ToString("0.00", Pt)}   ·   Média {average.ToString("0.00", Pt)}   ·   Top {Math.Ceiling(top).ToString("0", Pt)}% das atuações";
                if (result.PlayerRank is int position) line.Text += $"   ·   {position}º/{result.RankedPlayers} por KDA médio";
                description.Add(result.Average.HasValue ? $"{result.Matches:N0} atuações de outros jogadores na comparação." :
                    $"Amostra regional em formação: {result.Matches}/{RegionalComparison.MinimumMatches} atuações para comparar a partida.");
                description.Add($"Seu KDA médio: {result.PlayerAverage.ToString("0.00", Pt)} em {result.PlayerGames} partida(s). " +
                    (result.PlayerRank is int rank ? $"Posição por KDA médio: {rank}º de {result.RankedPlayers} jogadores." :
                    $"Posição disponível a partir de {RegionalComparison.MinimumPlayerGames} jogos por jogador e {RegionalComparison.MinimumPlayers} jogadores elegíveis ({result.RankedPlayers} agora)."));
                info.Text = string.Join(Environment.NewLine, description);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception e) when (e is HttpRequestException or InvalidDataException or IOException or JsonException or TimeoutException)
            {
                info.Text = e switch {
                    HttpRequestException {StatusCode: HttpStatusCode.NotFound} => "A partida ainda não foi indexada ou está fora do recorte da comparação.",
                    HttpRequestException {StatusCode: HttpStatusCode.TooManyRequests} => "Limite de consultas atingido. Tente novamente em instantes.",
                    _ => "API regional indisponível. Você pode consultar a classificação do campeão no LeagueOfGraphs." };
                retry.Visibility = Visibility.Visible;
            }
        }
        retry.Click += async (_, _) => await Load();
        _ = Load();
    }
}
