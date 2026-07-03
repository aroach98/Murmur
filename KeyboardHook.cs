using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Murmur;

/// <summary>
/// Global low-level keyboard hook (WH_KEYBOARD_LL) that watches a single virtual key
/// and raises HotkeyDown once when it is first pressed and HotkeyUp when released.
/// The hotkey's own events are swallowed so they don't leak into the focused app;
/// all other keys pass through untouched. Must be installed on a thread with a
/// message loop (the WinForms UI thread).
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    public event Action? HotkeyDown;
    public event Action? HotkeyUp;

    /// <summary>Virtual-key code to watch. Can be changed at runtime.</summary>
    public volatile int HotkeyVk;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int LLKHF_INJECTED = 0x10;

    private IntPtr _hook = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc; // held to keep the delegate alive for the unmanaged callback
    private bool _held;

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = Callback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null!), 0);
        if (_hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install global keyboard hook.");
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vk = Marshal.ReadInt32(lParam);            // KBDLLHOOKSTRUCT.vkCode
            int flags = Marshal.ReadInt32(lParam, 8);      // KBDLLHOOKSTRUCT.flags
            bool injected = (flags & LLKHF_INJECTED) != 0; // ignore our own SendInput events

            if (vk == HotkeyVk && !injected)
            {
                int msg = (int)wParam;
                if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    if (!_held)
                    {
                        _held = true;
                        HotkeyDown?.Invoke();
                    }
                    return (IntPtr)1; // swallow (also suppresses key-repeat)
                }
                if (msg is WM_KEYUP or WM_SYSKEYUP)
                {
                    if (_held)
                    {
                        _held = false;
                        HotkeyUp?.Invoke();
                    }
                    return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _proc = null;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
