using System.Text.Json;
using System.Text.Json.Serialization;

namespace Murmur;

public sealed class Settings
{
    /// <summary>Virtual-key code of the hold-to-record hotkey. Default: F9 (0x78).</summary>
    public int HotkeyVk { get; set; } = 0x78;

    /// <summary>Whisper model size: tiny, base, small, medium (English-only ggml models).</summary>
    public string ModelSize { get; set; } = "base";

    /// <summary>When true, inject via clipboard+Ctrl-V instead of synthetic keystrokes.</summary>
    public bool UseClipboardPaste { get; set; }

    /// <summary>Safety cap on a single recording, in seconds.</summary>
    public int MaxRecordSeconds { get; set; } = 120;

    /// <summary>
    /// Serve the transcription pipeline over localhost HTTP (server mode) so other
    /// local apps (e.g. jarvis-core) can use Murmur as their STT service. Loopback
    /// only — nothing is ever exposed off-machine.
    /// </summary>
    public bool ServerEnabled { get; set; } = true;

    /// <summary>Port for the localhost STT server.</summary>
    public int ServerPort { get; set; } = 8722;

    [JsonIgnore]
    public string HotkeyDisplayName => KeyNames.NameOf(HotkeyVk);

    public static string SettingsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Murmur");

    public static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) ?? new Settings();
        }
        catch { /* corrupt settings file — fall back to defaults */ }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public static class KeyNames
{
    public static string NameOf(int vk)
    {
        var key = (Keys)vk;
        return key switch
        {
            Keys.CapsLock => "CapsLock",
            Keys.Scroll => "ScrollLock",
            Keys.Pause => "Pause",
            _ => key.ToString(),
        };
    }
}
