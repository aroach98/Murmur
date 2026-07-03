using System.Runtime.InteropServices;

namespace Murmur;

/// <summary>
/// Injects text into whatever window has keyboard focus.
/// Primary path: SendInput with KEYEVENTF_UNICODE events — works across native Win32,
/// UWP, browsers and Electron apps without per-app integration. Fallback path:
/// clipboard + Ctrl-V with best-effort clipboard restore, for apps that ignore
/// synthetic unicode keystrokes.
/// </summary>
public static class TextInjector
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_CONTROL = 0x11;

    // Batching keeps each SendInput call small enough that slower consumers
    // (Electron/browser message pumps) don't drop events.
    private const int ChunkChars = 16;
    private const int ChunkDelayMs = 12;

    /// <summary>
    /// Apps whose input handling drops/repeats rapid synthetic unicode keystrokes —
    /// for those, clipboard-paste is the reliable path. Console hosts are notorious;
    /// Windows 11 Notepad's RichEdit garbles fast unicode input too.
    /// </summary>
    private static readonly string[] PasteWindowClasses =
    {
        "CASCADIA_HOSTING_WINDOW_CLASS", // Windows Terminal
        "ConsoleWindowClass",            // legacy conhost
        "Notepad",                       // Windows 11 Notepad
    };

    public static bool ForegroundNeedsPaste() =>
        PasteWindowClasses.Contains(GetForegroundWindowClass());

    public static string GetForegroundWindowClass()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return "";
        var sb = new System.Text.StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// Type text into the focused window. If <paramref name="gate"/> is supplied it is
    /// checked before every chunk; returning false aborts injection immediately (used
    /// by the test harness to stop typing the moment focus moves off the target window).
    /// Returns true if the full text was sent.
    /// </summary>
    public static bool TypeText(string text, Func<bool>? gate = null)
    {
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (char ch in text)
        {
            if (ch == '\r') continue;
            if (ch == '\n')
            {
                AddKey(inputs, VK_RETURN, down: true);
                AddKey(inputs, VK_RETURN, down: false);
                continue;
            }
            AddUnicode(inputs, ch, down: true);
            AddUnicode(inputs, ch, down: false);
        }

        for (int i = 0; i < inputs.Count; i += ChunkChars * 2)
        {
            if (gate != null && !gate()) return false;
            var chunk = inputs.Skip(i).Take(ChunkChars * 2).ToArray();
            if (SendInput((uint)chunk.Length, chunk, Marshal.SizeOf<INPUT>()) != chunk.Length)
                throw new InvalidOperationException("SendInput was blocked (is the focused app running elevated while Murmur is not?).");
            Thread.Sleep(ChunkDelayMs);
        }
        return true;
    }

    /// <summary>Title of the window that currently has focus (used by the focus gate).</summary>
    public static string GetForegroundWindowTitle()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return "";
        var sb = new System.Text.StringBuilder(512);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Clipboard fallback: preserves existing clipboard text, pastes, restores. Must run on an STA (UI) thread.</summary>
    public static void PasteText(string text)
    {
        string? previous = null;
        try { if (Clipboard.ContainsText()) previous = Clipboard.GetText(); }
        catch { /* clipboard busy — skip restore */ }

        Clipboard.SetText(text);
        SendCombo(VK_CONTROL, (ushort)'V');

        // Give the target app time to consume the paste before restoring.
        Thread.Sleep(400);
        try
        {
            if (previous != null) Clipboard.SetText(previous);
            else Clipboard.Clear();
        }
        catch { /* best effort */ }
    }

    /// <summary>Send a modifier+key chord (e.g. Ctrl+V). Used by the paste path and test harness.</summary>
    public static void SendCombo(ushort modifierVk, ushort keyVk) =>
        SendChord(new[] { modifierVk }, keyVk);

    /// <summary>Send a chord with any number of modifiers (e.g. Ctrl+Shift+W).</summary>
    public static void SendChord(ushort[] modifierVks, ushort keyVk)
    {
        var inputs = new List<INPUT>();
        foreach (var mod in modifierVks) AddKey(inputs, mod, down: true);
        AddKey(inputs, keyVk, down: true);
        AddKey(inputs, keyVk, down: false);
        foreach (var mod in modifierVks.Reverse()) AddKey(inputs, mod, down: false);
        var arr = inputs.ToArray();
        SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
    }

    private static void AddUnicode(List<INPUT> inputs, char ch, bool down)
    {
        inputs.Add(new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = ch, // UTF-16 code unit; receivers reassemble surrogate pairs
                    dwFlags = KEYEVENTF_UNICODE | (down ? 0 : KEYEVENTF_KEYUP),
                }
            }
        });
    }

    private static void AddKey(List<INPUT> inputs, ushort vk, bool down)
    {
        inputs.Add(new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = down ? 0 : KEYEVENTF_KEYUP,
                }
            }
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
}
