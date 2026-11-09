using System.Runtime.InteropServices;

namespace WhimGroupWin;

internal static class WindowNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    internal record struct Rect(int Left, int Top, int Right, int Bottom);
    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }
    internal delegate bool EnumCallback(nint handle, nint parameter);
    internal delegate void WinEventCallback(nint hook, uint eventType, nint handle,
        int objectId, int childId, uint threadId, uint eventTime);
    internal static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    internal static bool Valid(nint handle)
    {
        GetWindowThreadProcessId(handle, out var process);
        return IsWindow(handle) && process != 0 && process != Environment.ProcessId
            && IsWindowVisible(handle) && GetWindow(handle, 4) == 0 && GetWindowTextLengthW(handle) > 0;
    }
    internal static nint FindTarget(Point point, nint excluded)
    {
        nint found = 0;
        EnumWindows((handle, _) =>
        {
            if (handle == excluded || !Valid(handle) || IsIconic(handle))
                return true;
            if (DwmGetWindowAttribute(handle, 14, out int cloaked, 4) == 0 && cloaked != 0)
                return true;
            if (GetWindowRect(handle, out var rect) && point.X >= rect.Left && point.X < rect.Right
                && point.Y >= rect.Top && point.Y < rect.Bottom)
            {
                found = handle;
                return false;
            }
            return true;
        }, 0);
        return found;
    }
    internal static void Place(nint handle, Rect rect)
    {
        if (!IsHungAppWindow(handle))
            SetWindowPos(handle, 0, rect.Left, rect.Top,
                rect.Right - rect.Left, rect.Bottom - rect.Top, 0x0004 | 0x0010 | 0x4000);
    }

    internal delegate nint KeyboardCallback(int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookExW(int id, KeyboardCallback callback, nint module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    internal static extern nint SetWinEventHook(uint eventMin, uint eventMax,
        nint module, WinEventCallback callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint handle, uint flags);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumCallback callback, nint parameter);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint handle);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(nint handle);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint handle, uint command);
    [DllImport("user32.dll")] internal static extern int GetWindowLongW(nint handle, int index);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint handle, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowTextLengthW(nint handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowTextW(nint handle, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint handle, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint handle, nint after, int left, int top, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(nint handle, int command);
    [DllImport("user32.dll")] internal static extern bool PostMessageW(nint handle, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint handle);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint handle);
    [DllImport("user32.dll")] internal static extern bool IsHungAppWindow(nint handle);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint handle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint handle, uint attribute, out int value, int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint handle, uint attribute, out Rect value, int size);
}
