using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NexoLoL.Desktop;

public partial class MainWindow
{
    static Brush Theme(string key) => (Brush)Application.Current.FindResource(key);

    static BitmapFrame BrandImage(string file)
    {
        var uri = new Uri($"pack://application:,,,/BardoApp;component/Assets/{file}");
        var frame = BitmapFrame.Create(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    static readonly BitmapFrame NormalIcon = BrandImage("bardola.ico");
    static readonly BitmapFrame GhostIcon = BrandImage("bardola-ghost.ico");
    static readonly BitmapFrame NormalSymbol = BrandImage("bardola-simbolo-64.png");
    static readonly BitmapFrame GhostSymbol = BrandImage("bardola-simbolo-ghost-64.png");

    static void ApplyBrandMode(bool enabled)
    {
        var resources = Application.Current.Resources;
        resources["BrandWindowIcon"] = enabled ? GhostIcon : NormalIcon;
        resources["BrandSymbol"] = enabled ? GhostSymbol : NormalSymbol;
        resources["OverlayBrandText"] = enabled ? "GHOST" : "BARDOLA";
        resources["OverlayBrandInk"] = enabled ? Theme("patina") : Theme("latao");
        resources["GhostBadgeVisibility"] = enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    static StackPanel BrandBadge()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var symbol = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 5, 0) };
        RenderOptions.SetBitmapScalingMode(symbol, BitmapScalingMode.HighQuality);
        symbol.SetResourceReference(Image.SourceProperty, "BrandSymbol");
        var label = Text("", 11, true);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.SetResourceReference(TextBlock.TextProperty, "OverlayBrandText");
        label.SetResourceReference(TextBlock.ForegroundProperty, "OverlayBrandInk");
        row.Children.Add(symbol);
        row.Children.Add(label);
        return row;
    }

    static void AddGhostBadge(Canvas canvas)
    {
        var badge = new Border
        {
            Background = Theme("noite-overlay"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Child = BrandBadge()
        };
        badge.SetResourceReference(VisibilityProperty, "GhostBadgeVisibility");
        Canvas.SetLeft(badge, 8);
        Canvas.SetTop(badge, 8);
        canvas.Children.Add(badge);
    }
}
