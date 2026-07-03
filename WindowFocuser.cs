using System.Runtime.InteropServices;
using System.Text;

namespace Murmur;

/// <summary>
/// Test-harness helper: bring a window (found by title substring) to the foreground.
/// Background processes normally can't take foreground, so we attach our input queue
/// to the current foreground thread first; if that isn't enough, a quick Alt tap
/// marks our process as the last input source, which unlocks SetForegroundWindow.
/// </summary>
public static class WindowFocuser
{
    public static bool Focus(string titleSubstring)
    {
        IntPtr target = FindWindowByTitle(titleSubstring);
        if (target == IntPtr.Zero) return false;

        if (IsIconic(target)) ShowWindow(target, SW_RESTORE);

        uint ourThread = GetCurrentThreadId();
        uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);

        if (fgThread != ourThread) AttachThreadInput(ourThread, fgThread, true);
        SetForegroundWindow(target);
        BringWindowToTop(target);
        if (fgThread != ourThread) AttachThreadInput(ourThread, fgThread, false);
        Thread.Sleep(150);

        if (Matches(GetForegroundWindow(), titleSubstring)) return true;

        // Alt-nudge fallback (tap Alt, retry, tap Alt again so no menu stays armed)
        TextInjector.SendChord(Array.Empty<ushort>(), VK_MENU);
        SetForegroundWindow(target);
        TextInjector.SendChord(Array.Empty<ushort>(), VK_MENU);
        Thread.Sleep(150);

        return Matches(GetForegroundWindow(), titleSubstring);
    }

    private static bool Matches(IntPtr hwnd, string substr)
    {
        var sb = new StringBuilder(512);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString().Contains(substr, StringComparison.OrdinalIgnoreCase);
    }

    private static IntPtr FindWindowByTitle(string substr)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && Matches(hwnd, substr))
            {
                found = hwnd;
                return false; // stop enumerating
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private const int SW_RESTORE = 9;
    private const ushort VK_MENU = 0x12;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
}
