using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WhimGroupWin;

internal sealed record DesktopWindow(nint Handle, uint ProcessId, string Title, string ProcessName);

internal static class WindowService
{
    private delegate bool EnumWindowCallback(nint hwnd, nint parameter);

    public static List<DesktopWindow> Enumerate()
    {
        var windows = new List<DesktopWindow>();
        var processNames = new Dictionary<uint, string>();
        var ownProcessId = (uint)Environment.ProcessId;
        EnumWindowCallback callback = (hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, 4) != 0)
                return true;

            GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == ownProcessId || processId == 0)
                return true;

            if (DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            var title = new StringBuilder(1024);
            GetWindowTextW(hwnd, title, title.Capacity);
            if (string.IsNullOrWhiteSpace(title.ToString()))
                return true;

            if (!processNames.TryGetValue(processId, out var processName))
            {
                try
                {
                    using var process = Process.GetProcessById((int)processId);
                    processName = process.ProcessName;
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    processName = $"PID {processId}";
                }
                processNames[processId] = processName;
            }

            windows.Add(new DesktopWindow(hwnd, processId, title.ToString(), processName));
            return true;
        };

        if (!EnumWindows(callback, 0))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        return windows.OrderBy(window => window.ProcessName).ThenBy(window => window.Title).ToList();
    }

    public static bool Activate(DesktopWindow window)
    {
        if (!IsWindow(window.Handle))
            return false;
        GetWindowThreadProcessId(window.Handle, out var processId);
        if (processId != window.ProcessId)
            return false;

        if (IsIconic(window.Handle))
            ShowWindowAsync(window.Handle, 9);
        return SetForegroundWindow(window.Handle);
    }

    public static string? ExecutablePath(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.MainModule?.FileName;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hwnd, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hwnd, StringBuilder text, int maximumCount);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint hwnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hwnd);
}