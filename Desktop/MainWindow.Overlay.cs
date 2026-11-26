using System.Windows;
using System.Windows.Controls;
using NexoLoL.Core;

namespace NexoLoL.Desktop;
public partial class MainWindow
{
    Window? overlay;
    StackPanel? overlayContent;
    // A superfície é uma janela do Windows. Não lê memória e não injeta código no jogo.
    void UpdateOverlay(){
        bool wanted=preferences.OverlayStats||preferences.OverlayBuild||preferences.OverlaySkills||preferences.OverlayRelics;
        if(!wanted||snapshot.GameTime<=0){overlay?.Hide();return;}
        if(overlay is null){
            overlayContent=new StackPanel{Margin=new Thickness(14)};
            overlay=new Window{Title="Bardo.gg · Partida",Width=320,SizeToContent=SizeToContent.Height,MaxHeight=650,MinWidth=280,WindowStyle=WindowStyle.ToolWindow,ResizeMode=ResizeMode.CanResizeWithGrip,ShowInTaskbar=false,ShowActivated=false,Topmost=true,Background=Brush("#11171E"),Foreground=Brush("#ECEEF3"),Content=new ScrollViewer{Content=overlayContent},Left=18,Top=80};
            overlay.Closing+=(_,e)=>{if(ready){e.Cancel=true;preferences.OverlayStats=preferences.OverlayBuild=preferences.OverlaySkills=preferences.OverlayRelics=false;_=SavePreferences();overlay.Hide();BuildResearch();}};
        }
        FillOverlay(overlayContent!);if(!overlay.IsVisible)overlay.Show();
    }
    void FillOverlay(StackPanel panel){
        panel.Children.Clear();panel.Children.Add(Text("B A R D O . G G",11,true,Accent));
        panel.Children.Add(Text(catalog.Champions.FirstOrDefault(c=>c.Id==snapshot.ChampionId)?.Name??"Partida",19,true,top:8));
        if(preferences.OverlayStats){
            double rate=snapshot.Cs/Math.Max(1,snapshot.GameTime/60);
            panel.Children.Add(Text($"{snapshot.Kills} / {snapshot.Deaths} / {snapshot.Assists}    ·    {rate:0.0} CS/min",18,true,top:12));
            panel.Children.Add(Text($"Meta pessoal: {preferences.CsTarget:0} CS/min  ·  {rate-preferences.CsTarget:+0.0;-0.0;0.0}",11,false,Muted,top:4));
            panel.Children.Add(Text($"{snapshot.Gold:N0} ouro disponível · {snapshot.Vision:0.#} visão",11,false,Muted,top:5));
        }
        AddRelicClocks(panel);
        if(!preferences.OverlayBuild&&!preferences.OverlaySkills)return;
        var d=current?.Document;
        if(d is null||d.Query.ChampionId!=snapshot.ChampionId||d.Query.Mode!=GameService.DetectMode(snapshot.Mode)){
            panel.Children.Add(Text("Selecione a build deste campeão no Bardo.gg para consultar itens e habilidades.",12,false,Muted,top:15));return;
        }
        var variant=d.Variants[variantIndex];
        if(preferences.OverlayBuild)panel.Children.Add(ItemGroup("◈","Itens principais",variant.Core));
        if(preferences.OverlaySkills)panel.Children.Add(Text(variant.SkillPriority!=""?"Prioridade: "+string.Join(" → ",variant.SkillPriority.ToCharArray()):"Esta fonte não informou prioridade de habilidades.",14,true,Accent,top:10));
        panel.Children.Add(Text(d.Source+" · patch "+d.Patch+(current!.Stale?" · cache":""),10,false,Muted,top:12));
    }
}
