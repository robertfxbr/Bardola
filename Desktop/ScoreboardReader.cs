using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace NexoLoL.Desktop;

public record ClockPoint(double X,double Y);
internal static class ScoreboardReader
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out int processId);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window,out RECT rect);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc,int width,int height);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest,int x,int y,int w,int h,IntPtr src,int sx,int sy,uint operation);

    // Read only the scoreboard band, while Tab is held in the foreground game.
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X,Y; }
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window,ref POINT point);
    public static bool GameBounds(out Int32Rect bounds){
        bounds=Int32Rect.Empty;
        var window=GetForegroundWindow();
        if(window==IntPtr.Zero)return false;
        GetWindowThreadProcessId(window,out int id);
        try{
            using var game=Process.GetProcessById(id);
            if(!game.ProcessName.Equals("League of Legends",StringComparison.OrdinalIgnoreCase))return false;
            var origin=new POINT();
            if(!GetClientRect(window,out var rect)||!ClientToScreen(window,ref origin)||rect.Right<640||rect.Bottom<400)return false;
            bounds=new Int32Rect(origin.X,origin.Y,rect.Right,rect.Bottom);
            return true;
        }catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}catch(System.ComponentModel.Win32Exception){return false;}
    }
    public static BitmapSource? Capture(bool bottom=false)
    {
        var window=GetForegroundWindow();
        if(window==IntPtr.Zero || GetAsyncKeyState(9)>=0)return null;
        GetWindowThreadProcessId(window,out int id);
        using var game=Process.GetProcessById(id);
        if(!game.ProcessName.Equals("League of Legends",StringComparison.OrdinalIgnoreCase))return null;
        if(!GetClientRect(window,out var rect)||rect.Right<640||rect.Bottom<400)return null;
        int width=rect.Right,height=rect.Bottom/3;
        var dc=GetDC(window);
        if(dc==IntPtr.Zero)return null;
        var copy=CreateCompatibleDC(dc);
        var bitmap=CreateCompatibleBitmap(dc,width,height);
        var previous=SelectObject(copy,bitmap);
        try{
            if(previous==IntPtr.Zero || !BitBlt(copy,0,0,width,height,dc,0,bottom?rect.Bottom-height:0,0x00CC0020) || GetForegroundWindow()!=window || GetAsyncKeyState(9)>=0)return null;
            var image=Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }finally{
            SelectObject(copy,previous);DeleteObject(bitmap);DeleteDC(copy);ReleaseDC(window,dc);
        }
    }

    public static async Task<int?> ReadAsync(BitmapSource image,ClockPoint point,OcrEngine engine)
    {
        int w=Math.Max(38,image.PixelWidth/34), h=Math.Max(16,image.PixelHeight/23);
        int x=(int)(image.PixelWidth*point.X)-w/2,y=(int)(image.PixelHeight*point.Y)-h/2;
        if(x<0||y<0||x+w>image.PixelWidth||y+h>image.PixelHeight)return null;
        var crop=new CroppedBitmap(image,new Int32Rect(x,y,w,h));
        var scale=new TransformedBitmap(crop,new ScaleTransform(3,3));
        var format=new FormatConvertedBitmap(scale,PixelFormats.Bgra32,null,0);
        int stride=format.PixelWidth*4;
        byte[] pixels=new byte[stride*format.PixelHeight];
        format.CopyPixels(pixels,stride,0);
        using var input=SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(),BitmapPixelFormat.Bgra8,format.PixelWidth,format.PixelHeight);
        var result=await engine.RecognizeAsync(input);
        return Parse(result.Text);
    }
    internal static int? Parse(string text)
    {
        var m=Regex.Match(text.Trim(),@"^([0-2])\s*:\s*([0-5][0-9])$");
        if(!m.Success){
            if(Regex.IsMatch(text.Trim(),@"^[0-9]{1,2}$") && int.TryParse(text,out int seconds))return seconds;
            return null;
        }
        return int.Parse(m.Groups[1].Value)*60+int.Parse(m.Groups[2].Value);
    }
}
