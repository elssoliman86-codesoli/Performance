using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Performance.Services;

public sealed class GameWindowTracker
{
    public (bool IsFullscreen, int Pid, string Name) GetForegroundApp()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || GetWindowThreadProcessId(hwnd, out uint pidValue) == 0)
            return (false, 0, "");
        int pid = (int)pidValue;
        string name = "";
        try { using var p = Process.GetProcessById(pid); name = p.ProcessName; } catch { }
        if (pid == Environment.ProcessId || pid <= 4 || string.IsNullOrWhiteSpace(name))
            return (false, pid, name);
        if (!GetWindowRect(hwnd, out var rect)) return (false, pid, name);
        double width = rect.Right - rect.Left;
        double height = rect.Bottom - rect.Top;
        bool fullscreen = width >= SystemParametersWidth() - 8 && height >= SystemParametersHeight() - 8;
        return (fullscreen, pid, name);
    }

    private static double SystemParametersWidth() => System.Windows.SystemParameters.PrimaryScreenWidth;
    private static double SystemParametersHeight() => System.Windows.SystemParameters.PrimaryScreenHeight;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
}
