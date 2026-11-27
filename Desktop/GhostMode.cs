using System.Runtime.InteropServices;

namespace NexoLoL.Desktop;

// All Bardola UI is created on the WPF dispatcher thread. The thread-local hook also
// covers top-level HWNDs created by WPF popups and native dialogs before they are shown.
internal sealed class GhostMode : IDisposable
{
    const uint ExcludeFromCapture = 0x11;
    const int CallWindowProc = 4;
    const uint ShowWindow = 0x18, WindowPosChanging = 0x46, NcDestroy = 0x82, ShowFlag = 0x40;
    readonly HashSet<nint> protectedWindows = [];
    readonly HashSet<nint> failedWindows = [];
    readonly HookProc callback;
    nint hook;
    bool enabled;

    public GhostMode() => callback = BeforeWindowMessage;
    public event Action? StatusChanged;
    public bool HasFailures => failedWindows.Count != 0 || (enabled && hook == 0);

    public void SetEnabled(bool value)
    {
        enabled = value;
        if (value)
        {
            uint thread = GetCurrentThreadId();
            if (hook == 0) hook = SetWindowsHookExW(CallWindowProc, callback, 0, thread);
            EnumThreadWindows(thread, (hwnd, _) => {
                if (IsWindowVisible(hwnd)) Protect(hwnd);
                return true;
            }, 0);
        }
        else
        {
            failedWindows.Clear();
            foreach (var hwnd in protectedWindows.ToArray())
            {
                if (!IsWindow(hwnd) || SetWindowDisplayAffinity(hwnd, 0)) protectedWindows.Remove(hwnd);
                else failedWindows.Add(hwnd);
            }
            if (hook != 0 && protectedWindows.Count == 0) RemoveHook();
        }
        StatusChanged?.Invoke();
    }

    public bool Protect(nint hwnd)
    {
        if (!enabled || hwnd == 0 || protectedWindows.Contains(hwnd)) return true;
        if (SetWindowDisplayAffinity(hwnd, ExcludeFromCapture))
        {
            protectedWindows.Add(hwnd);
            if (failedWindows.Remove(hwnd)) StatusChanged?.Invoke();
            return true;
        }
        if (failedWindows.Add(hwnd)) StatusChanged?.Invoke();
        return false;
    }

    nint BeforeWindowMessage(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var message = Marshal.PtrToStructure<WindowMessage>(lParam);
            if (message.Id == NcDestroy)
            {
                protectedWindows.Remove(message.Window);
                if (failedWindows.Remove(message.Window)) StatusChanged?.Invoke();
            }
            else if (enabled && (message.Id == ShowWindow && message.WParam != 0 ||
                message.Id == WindowPosChanging && message.LParam != 0 &&
                (Marshal.PtrToStructure<WindowPosition>(message.LParam).Flags & ShowFlag) != 0))
            {
                if (GetAncestor(message.Window, 2 /* GA_ROOT */) == message.Window) Protect(message.Window);
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    void RemoveHook()
    {
        if (UnhookWindowsHookEx(hook)) hook = 0;
    }

    public void Dispose()
    {
        enabled = false;
        if (hook != 0) RemoveHook();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WindowMessage { public nint LParam, WParam; public uint Id; public nint Window; }
    [StructLayout(LayoutKind.Sequential)]
    struct WindowPosition { public nint Window, After; public int X, Y, Width, Height; public uint Flags; }
    delegate nint HookProc(int code, nint wParam, nint lParam);
    delegate bool EnumProc(nint hwnd, nint param);
    [DllImport("kernel32")] static extern uint GetCurrentThreadId();
    [DllImport("user32")] static extern nint SetWindowsHookExW(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32")] static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32")] static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32")] static extern bool EnumThreadWindows(uint thread, EnumProc callback, nint param);
    [DllImport("user32")] static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32")] static extern bool IsWindow(nint hwnd);
    [DllImport("user32")] static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32")] static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
}
