using System.ComponentModel;
using System.Runtime.InteropServices;

// Real top-level HWND on the UI thread, placed offscreen so the test does not interrupt play.
internal sealed class GhostWindowProbe : IDisposable
{
    readonly nint handle;
    public GhostWindowProbe(bool layered=false)
    {
        const uint toolNoActivate=0x08000080, layeredStyle=0x80000, popup=0x80000000;
        handle=CreateWindowExW(toolNoActivate | (layered ? layeredStyle : 0), "STATIC", "Bardola QA",
            popup,-32000,-32000,20,20,0,0,0,0);
        if(handle==0)throw new Win32Exception(Marshal.GetLastWin32Error());
        ShowWindow(handle,4 /* SW_SHOWNOACTIVATE */);
    }
    public uint Affinity => GetWindowDisplayAffinity(handle,out uint value)?value:
        throw new Win32Exception(Marshal.GetLastWin32Error());
    public void Dispose()=>DestroyWindow(handle);

    [DllImport("user32",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern nint CreateWindowExW(uint exStyle,string cls,string name,uint style,int x,int y,int w,int h,nint parent,nint menu,nint instance,nint data);
    [DllImport("user32",SetLastError=true)] static extern bool GetWindowDisplayAffinity(nint hwnd,out uint value);
    [DllImport("user32")] static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32")] static extern bool ShowWindow(nint hwnd,int command);
}
