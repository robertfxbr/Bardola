using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NexoLoL.Core;

namespace NexoLoL.Desktop;

public partial class MainWindow
{
    int? profileChampion;
    string? profileOwner, profileQueue;

    void OpenChampionProfile(int id)
    {
        profileChampion = id;
        profileQueue = null;
        RenderAccount();
        AccountPage.ScrollToTop();
    }

    static string QueueName(AccountMatch m) => m.QueueId switch
    {
        400 => "Normal alternada", 420 => "Solo/Duo", 430 => "Normal às cegas", 440 => "Flex",
        450 => "ARAM", 490 => "Normal rápida", 2400 => "ARAM: Desordem",
        > 0 => $"Fila {m.QueueId}", _ => ModeLabel(m.Mode) + " · fila não informada"
    };

    bool AddChampionProfileNavigation(GameAccount profile)
    {
        string owner = profile.Region + "/" + profile.RiotId;
        if (owner != profileOwner) { profileOwner = owner; profileChampion = null; profileQueue = null; }
        if (profile.Matches.Count == 0) return false;
        var played = profile.Matches.DistinctBy(m => (m.Platform, m.Id))
            .GroupBy(m => m.ChampionId).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Max(m => m.At)).ToList();
        var nav = new WrapPanel { Margin = new Thickness(0, 8, 0, 10) };
        nav.Children.Add(Button("Visão geral", () => { profileChampion = null; RenderAccount(); }));
        nav.Children.Add(Button("Campeões", () => OpenChampionProfile(profileChampion ?? played[0].Key)));
        AccountDetails.Children.Add(nav);
        if (profileChampion == null) return false;
        var chosen = played.FirstOrDefault(g => g.Key == profileChampion) ?? played[0];
        var champ = catalog.Champions.FirstOrDefault(c => c.Id == chosen.Key);
        string Name(int id) => catalog.Champions.FirstOrDefault(c => c.Id == id)?.Name ?? $"Campeão {id}";
        var panel = new StackPanel();
        var controls = new WrapPanel();
        var champions = new ComboBox { MinWidth = 165, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var group in played)
            champions.Items.Add(new ComboBoxItem { Tag = group.Key, Content = $"{Name(group.Key)} ({group.Count()})" });
        champions.SelectedIndex = played.IndexOf(chosen);
        champions.SelectionChanged += (_, _) => {
            if (champions.SelectedItem is ComboBoxItem { Tag: int id }) OpenChampionProfile(id);
        };
        controls.Children.Add(champions);
        var queues = new ComboBox { MinWidth = 140 };
        queues.Items.Add("Todas as filas");
        foreach (string name in chosen.Select(QueueName).Distinct().Order()) queues.Items.Add(name);
        queues.SelectedItem = profileQueue != null && queues.Items.Contains(profileQueue) ? profileQueue : queues.Items[0];
        queues.SelectionChanged += (_, _) => { profileQueue = queues.SelectedIndex == 0 ? null : (string)queues.SelectedItem; RenderAccount(); };
        controls.Children.Add(queues);
        panel.Children.Add(controls);
        var matches = chosen.Where(m => queues.SelectedIndex == 0 || QueueName(m) == (string)queues.SelectedItem)
            .OrderByDescending(m => m.At).ThenByDescending(m => m.Id).ToList();
        var header = new DockPanel { Margin = new Thickness(0, 12, 0, 8) };
        if (champ != null && LeaguePlatform.Find(profile.Region) is {} region &&
            RiotId.TrySplit(profile.RiotId, out var namePart, out var tag))
        {
            var url = $"https://www.leagueofgraphs.com/summoner/champions/{champ.Slug}/{region.SiteCode}/{Uri.EscapeDataString(namePart)}-{Uri.EscapeDataString(tag)}";
            var more = Button("Perfil no LeagueOfGraphs ↗", () => OpenUrl(url));
            DockPanel.SetDock(more, Dock.Right);
            header.Children.Add(more);
        }
        if (champ != null)
        {
            var icon = new Image { Width = 38, Height = 38, Margin = new Thickness(0, 0, 9, 0) };
            LoadIcon(icon, $"https://ddragon.leagueoflegends.com/cdn/{catalog.Version}/img/champion/{champ.Key}.png", lifetime.Token);
            DockPanel.SetDock(icon, Dock.Left);
            header.Children.Add(icon);
        }
        header.Children.Add(Text(Name(chosen.Key), 24, true));
        panel.Children.Add(header);
        int wins = matches.Count(m => m.Win);
        var metrics = new WrapPanel();
        void Metric(string label, string value)
        {
            var box = new StackPanel { Margin = new Thickness(0, 0, 26, 5) };
            box.Children.Add(Text(label, 11, false, Muted));
            box.Children.Add(Text(value, 20, true));
            metrics.Children.Add(box);
        }
        string Avg(Func<AccountMatch,double> metric) => matches.Average(metric).ToString("0.0", Pt);
        Metric("Partidas", $"{matches.Count}");
        Metric("Vitórias", $"{(100d * wins / matches.Count).ToString("0.#", Pt)}% · {wins}V {matches.Count - wins}D");
        Metric("Abates / mortes / assistências", $"{Avg(m => m.Kills)} / {Avg(m => m.Deaths)} / {Avg(m => m.Assists)}");
        var timed = matches.Where(m => m.Duration > 0).ToList();
        if (timed.Count > 0) Metric("CS/min", timed.Average(m => m.Cs * 60d / m.Duration).ToString("0.0", Pt));
        var damage = timed.Where(m => m.ChampionDamage.HasValue).ToList();
        if (damage.Count > 0) Metric($"Dano/min · {damage.Count} jogo(s)", damage.Average(m => m.ChampionDamage!.Value * 60d / m.Duration).ToString("N0", Pt));
        var gold = timed.Where(m => m.Gold.HasValue).ToList();
        if (gold.Count > 0) Metric("Ouro/min", gold.Average(m => m.Gold!.Value * 60d / m.Duration).ToString("N0", Pt));
        var vision = timed.Where(m => m.Vision.HasValue).ToList();
        if (vision.Count > 0) Metric("Visão/min", vision.Average(m => m.Vision!.Value * 60d / m.Duration).ToString("0.00", Pt));
        panel.Children.Add(metrics);
        panel.Children.Add(Text($"Calculado a partir do histórico disponível de {profile.RiotId} ({profile.Matches.Count} partidas consultadas).", 12, false, Muted));
        var banner = new Grid {ClipToBounds=true,Background=Theme("noite-painel")};
        if (champ != null)
        {
            var illustration = new Image();
            // A background brush fills the panel without changing its measured height.
            var art = new ImageBrush {Stretch=Stretch.UniformToFill,AlignmentY=AlignmentY.Center};
            System.Windows.Data.BindingOperations.SetBinding(art,ImageBrush.ImageSourceProperty,
                new System.Windows.Data.Binding("Source"){Source=illustration});
            banner.Children.Add(new Border {Width=465,HorizontalAlignment=HorizontalAlignment.Right,
                Background=art,OpacityMask=new LinearGradientBrush(Colors.Transparent,Color.FromArgb(135,255,255,255),0)});
            LoadIcon(illustration,$"https://ddragon.leagueoflegends.com/cdn/img/champion/splash/{champ.Key}_0.jpg",lifetime.Token);
        }
        panel.Margin=new Thickness(16,12,16,14);
        banner.Children.Add(panel);
        AccountDetails.Children.Add(banner);
        AddRegionalComparison(AccountDetails, matches[0], champ);

        var games = new StackPanel();
        games.Children.Add(Text("KDA por partida", 16, true));
        double Ratio(AccountMatch m) => ((double)m.Kills + m.Assists) / Math.Max(1, m.Deaths);
        double max = Math.Max(1, matches.Max(Ratio));
        foreach (var m in matches)
        {
            var row = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            foreach (int width in new[] { 165, 160, 100, 0, 70 })
                row.ColumnDefinitions.Add(new() { Width = width == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
            void Cell(UIElement element, int column) { Grid.SetColumn(element, column); row.Children.Add(element); }
            var color = Brush(m.Win ? "#72DDC4" : "#EDA29D");
            Cell(Text((m.Win ? "Vitória" : "Derrota") + (m.At > DateTimeOffset.MinValue ? $" · {m.At.ToLocalTime():dd/MM HH:mm}" : ""), 13, true, color), 0);
            Cell(Text(QueueName(m), 12, false, Muted), 1);
            Cell(Text($"{m.Kills} / {m.Deaths} / {m.Assists}", 13), 2);
            Cell(new ProgressBar { Minimum = 0, Maximum = max, Value = Ratio(m), Height = 5, Foreground = color,
                Background = Brush("#303846"), BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 12, 0) }, 3);
            Cell(Text(Ratio(m).ToString("0.00", Pt), 14, true, color), 4);
            games.Children.Add(row);
        }
        AccountDetails.Children.Add(Card(games));
        return true;
    }
}
