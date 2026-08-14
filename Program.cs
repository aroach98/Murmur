using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Text;

namespace Murmur;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        MigrateLegacyAppData();

        if (args.Length > 0)
            return RunCli(args);

        using var mutex = new Mutex(true, @"Local\Murmur_SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Murmur is already running — look for the microphone icon in the system tray.",
                "Murmur", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
        return 0;
    }

    /// <summary>This app used to be called WhisperKey — carry over settings and downloaded models.</summary>
    private static void MigrateLegacyAppData()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string old = Path.Combine(appData, "WhisperKey");
            if (Directory.Exists(old) && !Directory.Exists(Settings.SettingsDir))
                Directory.Move(old, Settings.SettingsDir);
        }
        catch { /* fresh start if the move fails */ }
    }

    /// <summary>
    /// Headless test modes (used to verify the pipeline without a mic/hotkey):
    ///   Murmur --transcribe file.wav [--model base] [--out result.txt]
    ///   Murmur --inject "text to type" [--delay 1500] [--paste]
    ///   Murmur --select-copy [--delay 1500]   (sends Ctrl+A then Ctrl+C to the focused app)
    /// </summary>
    private static int RunCli(string[] args)
    {
        string? Get(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        bool Has(string name) => Array.IndexOf(args, name) >= 0;

        try
        {
            if (Has("--server"))
            {
                // Headless STT server (no tray). Shares the single-instance mutex:
                // if the tray app is up, IT serves HTTP — don't double-bind the port.
                using var mutex = new Mutex(true, @"Local\Murmur_SingleInstance", out bool isFirst);
                if (!isFirst)
                {
                    Console.Error.WriteLine("Murmur is already running — the tray instance serves HTTP.");
                    return 4;
                }
                var settings = Settings.Load();
                int port = int.TryParse(Get("--port"), out int p) ? p : settings.ServerPort;
                using var transcriber = new Transcriber();
                using var server = new MurmurServer(port, transcriber,
                    () => Settings.Load().ModelSize,
                    msg =>
                    {
                        try
                        {
                            File.AppendAllText(Path.Combine(Settings.SettingsDir, "Murmur.log"),
                                $"{DateTime.Now:HH:mm:ss} {msg}{Environment.NewLine}");
                        }
                        catch { /* logging must never break the server */ }
                    });
                server.Start();
                Console.WriteLine($"Murmur STT server on http://127.0.0.1:{port}/");
                Thread.Sleep(Timeout.Infinite);
                return 0;
            }

            if (Has("--transcribe"))
            {
                string wavPath = Get("--transcribe") ?? throw new ArgumentException("--transcribe needs a wav path");
                string size = Get("--model") ?? Settings.Load().ModelSize;
                string modelPath = ModelManager.PathFor(size);
                if (!File.Exists(modelPath)) throw new FileNotFoundException($"Model not downloaded: {modelPath}");

                float[] samples = WavAudio.LoadWavAs16kMono(wavPath);
                using var transcriber = new Transcriber();
                string raw = transcriber.TranscribeAsync(samples, modelPath).GetAwaiter().GetResult();
                string text = Has("--raw") ? raw : Transcriber.StripNoise(raw);

                string? outPath = Get("--out");
                if (outPath != null) File.WriteAllText(outPath, text, new UTF8Encoding(false));
                Console.WriteLine(text);
                return 0;
            }

            // --require-title makes injection abort (exit 3) unless the foreground window
            // title contains the given substring — checked before AND during injection so a
            // focus change mid-test can't spray keystrokes into the wrong window.
            string? requiredTitle = Get("--require-title");
            Func<bool> gate = requiredTitle == null
                ? () => true
                : () => TextInjector.GetForegroundWindowTitle().Contains(requiredTitle, StringComparison.OrdinalIgnoreCase);

            if (Has("--inject"))
            {
                string text = Get("--inject") ?? throw new ArgumentException("--inject needs text");
                int delay = int.TryParse(Get("--delay"), out int d) ? d : 1500;
                Thread.Sleep(delay); // time for the caller to focus the target window
                if (!gate()) return 3;
                bool complete;
                if (Has("--paste")) { TextInjector.PasteText(text); complete = true; }
                else complete = TextInjector.TypeText(text, gate);
                return complete ? 0 : 3;
            }

            if (Has("--select-copy"))
            {
                int delay = int.TryParse(Get("--delay"), out int d) ? d : 1500;
                Thread.Sleep(delay);
                if (!gate()) return 3;
                TextInjector.SendCombo(0x11, (ushort)'A'); // Ctrl+A
                Thread.Sleep(200);
                if (!gate()) return 3;
                TextInjector.SendCombo(0x11, (ushort)'C'); // Ctrl+C
                Thread.Sleep(300);                          // let the target publish the clipboard
                return 0;
            }

            if (Has("--combo"))
            {
                // e.g. --combo ctrl+shift+w  — used by the test harness for save/close.
                string spec = Get("--combo") ?? throw new ArgumentException("--combo needs a chord like ctrl+s");
                int delay = int.TryParse(Get("--delay"), out int d) ? d : 1500;
                Thread.Sleep(delay);
                if (!gate()) return 3;

                var parts = spec.ToLowerInvariant().Split('+', StringSplitOptions.RemoveEmptyEntries);
                var mods = new List<ushort>();
                ushort key = 0;
                foreach (var part in parts)
                {
                    switch (part)
                    {
                        case "ctrl": mods.Add(0x11); break;
                        case "shift": mods.Add(0x10); break;
                        case "alt": mods.Add(0x12); break;
                        default:
                            key = part.Length == 1
                                ? (ushort)char.ToUpperInvariant(part[0])
                                : (ushort)Enum.Parse<Keys>(part, ignoreCase: true);
                            break;
                    }
                }
                if (key == 0) throw new ArgumentException($"No key in chord: {spec}");
                TextInjector.SendChord(mods.ToArray(), key);
                Thread.Sleep(200);
                return 0;
            }

            if (Has("--focus"))
            {
                // Bring a window (by title substring) to the foreground. Exit 0 on
                // success, 4 if it couldn't be found/focused. Test-harness helper.
                string substr = Get("--focus") ?? throw new ArgumentException("--focus needs a title substring");
                int delay = int.TryParse(Get("--delay"), out int d) ? d : 300;
                Thread.Sleep(delay);
                return WindowFocuser.Focus(substr) ? 0 : 4;
            }

            if (Has("--list-mics"))
            {
                var sb = new StringBuilder();
                sb.AppendLine($"waveIn devices: {WaveInEvent.DeviceCount}");
                for (int i = 0; i < WaveInEvent.DeviceCount; i++)
                    sb.AppendLine($"  [{i}] {WaveInEvent.GetCapabilities(i).ProductName}");
                string? outPath = Get("--out");
                if (outPath != null) File.WriteAllText(outPath, sb.ToString());
                Console.Write(sb.ToString());
                return 0;
            }

            Console.Error.WriteLine("Unknown arguments. See Program.RunCli for test modes.");
            return 2;
        }
        catch (Exception ex)
        {
            string? outPath = Get("--out");
            if (outPath != null) File.WriteAllText(outPath, "ERROR: " + ex);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

}
