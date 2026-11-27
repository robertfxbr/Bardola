using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Ocr;
using NexoLoL.Core;

namespace NexoLoL.Desktop;
public partial class MainWindow
{
    readonly DispatcherTimer relicPoll=new(){Interval=TimeSpan.FromMilliseconds(650)};
    readonly double?[] deadlines=new double?[4];
    readonly (int Seconds,double At)?[] pendingReads=new (int,double)?[4];
    static readonly string[] padNames=["Azul interna","Azul externa","Vermelha externa","Vermelha interna"];
    OcrEngine? clockOcr;
    bool readingClocks,awaitingCalibrationFrame;
    int clockGeneration;
    Window? mapOverlay,calibrationWindow;
    readonly Canvas mapCanvas=new();
    TextBlock? calibrationStatus;

    void StartRelicPolling(){
        relicPoll.Tick+=async(_,_)=>await PollRelicDisplay();
        relicPoll.Start();
    }
    void ClearRelicReadings(){
        clockGeneration++;
        Array.Clear(deadlines);Array.Clear(pendingReads);
        mapOverlay?.Hide();
    }
    bool IsRelicMatch()=>preferences.AutoMapRelics && snapshot.GameTime>0 &&
        GameService.DetectMode(snapshot.Mode) is GameMode.Aram or GameMode.Mayhem;

    async Task PollRelicDisplay(){
        if(readingClocks)return;
        if(!IsRelicMatch() || !ScoreboardReader.GameBounds(out var bounds)){
            mapOverlay?.Hide();
            return;
        }
        int generation=clockGeneration;
        readingClocks=true;
        try{
            if(awaitingCalibrationFrame){
                var top=ScoreboardReader.Capture();
                var bottom=top is null?null:ScoreboardReader.Capture(bottom:true);
                if(top!=null && bottom!=null){
                    awaitingCalibrationFrame=false;
                    calibrationHeight=bounds.Height;ShowCalibration(top,bottom);
                }
                return;
            }
            if(preferences.ClockLocations is not {Length:4} clocks ||
               preferences.MapLocations is not {Length:4} pads ||
               !ValidPoints(clocks) || !ValidPoints(pads) ||
               preferences.CalibrationWidth!=bounds.Width || preferences.CalibrationHeight!=bounds.Height){
                if(calibrationStatus!=null)calibrationStatus.Text="Calibre os quatro tempos e as posições para esta resolução.";
                mapOverlay?.Hide();
                return;
            }
            var frame=ScoreboardReader.Capture();
            if(frame!=null){
                clockOcr??=OcrEngine.TryCreateFromUserProfileLanguages();
                if(clockOcr is null){if(calibrationStatus!=null)calibrationStatus.Text="Instale o reconhecimento de texto de um idioma nas configurações do Windows.";return;}
                double at=snapshot.GameTime;
                for(int i=0;i<4;i++){
                    var seconds=await ScoreboardReader.ReadAsync(frame,clocks[i],clockOcr);
                    if(generation!=clockGeneration || !IsRelicMatch() || !ready)return;
                    if(seconds is not int s || s<=0){pendingReads[i]=null;continue;}
                    if(pendingReads[i] is { } prior && prior.Seconds>s && prior.Seconds-s<=4 &&
                       at-prior.At is >=0 and <=4 && Math.Abs((prior.Seconds-s)-(at-prior.At))<=2)
                        deadlines[i]=at+s;
                    pendingReads[i]=(s,at);
                }
            }else Array.Clear(pendingReads);
            if(ScoreboardReader.GameBounds(out var fresh)&&fresh==bounds)DrawMapClocks(bounds,pads);
            else mapOverlay?.Hide();
        }catch(Exception ex) when(ex is COMException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){
            if(calibrationStatus!=null)calibrationStatus.Text="Não foi possível ler o placar. Use o modo sem bordas e confira a calibração.";
            mapOverlay?.Hide();
        }finally{readingClocks=false;}
    }
    static bool ValidPoints(ClockPoint[] points)=>points.All(p=>p is {X: >=0 and <=1, Y: >=0 and <=1});
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetStyle(IntPtr w,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW")] static extern int SetStyle(IntPtr w,int index,int style);
    void DrawMapClocks(Int32Rect bounds,ClockPoint[] pads){
        mapCanvas.Children.Clear();
        if(deadlines.All(d=>!d.HasValue||d<=snapshot.GameTime)){mapOverlay?.Hide();return;}
        if(mapOverlay==null){
            mapOverlay=new Window{WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true,ShowActivated=false,ShowInTaskbar=false,IsHitTestVisible=false,ResizeMode=ResizeMode.NoResize,Content=mapCanvas,Width=1,Height=1};
            mapOverlay.SourceInitialized+=(_,_)=>{var handle=new WindowInteropHelper(mapOverlay).Handle;SetStyle(handle,-20,GetStyle(handle,-20)|0x20|0x08000000|0x80);};
        }
        if(!mapOverlay.IsVisible)mapOverlay.Show();
        SetWindowPos(new WindowInteropHelper(mapOverlay).Handle,new IntPtr(-1),bounds.X,bounds.Y,bounds.Width,bounds.Height,0x10);
        var dpi=VisualTreeHelper.GetDpi(mapOverlay);
        for(int i=0;i<4;i++)if(deadlines[i] is double end && end>snapshot.GameTime){
            int remaining=(int)Math.Ceiling(end-snapshot.GameTime);
            var label=new Border{CornerRadius=new CornerRadius(5),Background=Brush("#E51A2129"),Padding=new Thickness(4,1,4,1),Child=Text(remaining.ToString(),13,true,remaining<=10?Brush("#71E3A9"):Brushes.White)};
            label.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            Canvas.SetLeft(label,bounds.Width*pads[i].X/dpi.DpiScaleX-label.DesiredSize.Width/2);
            Canvas.SetTop(label,(bounds.Height-bounds.Height/3+bounds.Height/3*pads[i].Y)/dpi.DpiScaleY-label.DesiredSize.Height/2);
            mapCanvas.Children.Add(label);
        }
    }

    void ShowCalibration(BitmapSource top,BitmapSource bottom){
        calibrationWindow?.Close();
        var prompt=Text("",15,true);
        var view=new Image{Source=top,Stretch=Stretch.Fill};
        var clicks=new List<ClockPoint>();
        void Prompt(){int i=clicks.Count;prompt.Text=(i<4?"Clique no centro dos dígitos do placar":"Clique na posição da cura no minimapa")+" · "+padNames[i%4];}
        var panel=new DockPanel{Margin=new Thickness(12)};
        DockPanel.SetDock(prompt,Dock.Top);panel.Children.Add(prompt);panel.Children.Add(view);
        var dialog=new Window{Title="Calibrar curas do ARAM",Width=Math.Min(1150,SystemParameters.WorkArea.Width),Height=380,Background=Brush("#171B23"),Content=panel,ShowActivated=false};
        calibrationWindow=dialog;Prompt();
        view.MouseLeftButtonDown+=(_,e)=>{
            var pos=e.GetPosition(view);
            if(view.ActualWidth<=0||view.ActualHeight<=0)return;
            var point=new ClockPoint(pos.X/view.ActualWidth,pos.Y/view.ActualHeight);
            if(clicks.Skip(clicks.Count<4?0:4).Any(p=>Math.Abs(p.X-point.X)<0.003&&Math.Abs(p.Y-point.Y)<0.008))return;
            clicks.Add(point);
            if(clicks.Count==4)view.Source=bottom;
            if(clicks.Count<8){Prompt();return;}
            preferences.ClockLocations=clicks.Take(4).ToArray();preferences.MapLocations=clicks.Skip(4).ToArray();
            preferences.CalibrationWidth=top.PixelWidth;preferences.CalibrationHeight=calibrationHeight;
            ClearRelicReadings();_=SavePreferences();dialog.Close();BuildResearch();
        };
        dialog.Closed+=(_,_)=>{calibrationWindow=null;view.Source=null;};
        dialog.Show();
    }
    int calibrationHeight;
    void AddAutoRelicSettings(StackPanel panel){
        panel.Children.Add(SettingRow("Curas do ARAM no minimapa","Lê os tempos visíveis enquanto você segura Tab. Continue jogando para ver a contagem sobre cada cura, em modo sem bordas.",preferences.AutoMapRelics,v=>{
            preferences.AutoMapRelics=v;awaitingCalibrationFrame=false;ClearRelicReadings();calibrationWindow?.Close();_=SavePreferences();
        }));
        calibrationStatus=Text(preferences.ClockLocations?.Length==4?"Calibração salva. Recalibre se mover o minimapa, mudar a escala ou a resolução.":"Na primeira partida, calibre os quatro tempos e as quatro posições no minimapa.",12,false,Muted,top:8);
        panel.Children.Add(calibrationStatus);
        panel.Children.Add(Button("Calibrar posições",()=>{
            preferences.AutoMapRelics=true;_=SavePreferences();ClearRelicReadings();awaitingCalibrationFrame=true;BuildResearch();
            if(calibrationStatus!=null)calibrationStatus.Text="Em uma partida ARAM, mantenha Tab pressionado com o placar visível. Depois volte à janela de calibração do Bardola.";
        },top:8));
    }
}
