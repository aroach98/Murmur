namespace Murmur;

/// <summary>
/// The background app: owns the tray icon, global hook, recorder, transcriber and
/// overlay, and wires the hold → record → transcribe → inject pipeline together.
/// Hook callbacks run on the UI thread and stay cheap; transcription runs on the
/// thread pool so the UI (and the hook) never block.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly Settings _settings;
    private readonly NotifyIcon _tray;
    private readonly KeyboardHook _hook;
    private readonly AudioRecorder _recorder;
    private readonly Transcriber _transcriber;
    private readonly RecordingOverlay _overlay;
    private readonly System.Windows.Forms.Timer _maxDurationTimer;
    private readonly Icon _idleIcon;
    private readonly Icon _activeIcon;
    private MurmurServer? _server;
    private SettingsForm? _settingsForm;

    public TrayApplicationContext()
    {
        _settings = Settings.Load();

        _idleIcon = TrayIcons.Load("murmur.ico", Color.FromArgb(124, 58, 237));
        _activeIcon = TrayIcons.Load("murmur-rec.ico", Color.FromArgb(240, 70, 70));

        _tray = new NotifyIcon
        {
            Icon = _idleIcon,
            Visible = true,
            Text = $"Murmur — hold {_settings.HotkeyDisplayName} to dictate",
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowSettings();

        _overlay = new RecordingOverlay();
        _recorder = new AudioRecorder();
        _recorder.RecordingError += ex => BeginInvokeOnUi(() =>
        {
            _overlay.HideOverlay();
            _tray.Icon = _idleIcon;
            ShowBalloon($"Recording stopped: {ex.Message}", ToolTipIcon.Error);
        });
        _transcriber = new Transcriber();

        _maxDurationTimer = new System.Windows.Forms.Timer();
        _maxDurationTimer.Tick += (_, _) => OnHotkeyUp(); // treat cap as a release

        // Force handle creation so BeginInvoke works before the overlay is first shown.
        _ = _overlay.Handle;

        // Post handlers to the message queue instead of running them inside the hook
        // callback — Windows silently drops low-level hooks whose callbacks run long.
        _hook = new KeyboardHook { HotkeyVk = _settings.HotkeyVk };
        _hook.HotkeyDown += () => BeginInvokeOnUi(OnHotkeyDown);
        _hook.HotkeyUp += () => BeginInvokeOnUi(OnHotkeyUp);
        _hook.Install();

        if (!ModelManager.IsDownloaded(_settings.ModelSize))
        {
            ShowBalloon($"The '{_settings.ModelSize}' whisper model isn't downloaded yet. Open Settings to fetch it.", ToolTipIcon.Warning);
            ShowSettings();
        }

        StartServerIfEnabled();
    }

    /// <summary>Server mode (localhost STT for jarvis-core etc.) — shares _transcriber.</summary>
    private void StartServerIfEnabled()
    {
        if (!_settings.ServerEnabled) return;
        try
        {
            _server = new MurmurServer(_settings.ServerPort, _transcriber,
                () => _settings.ModelSize, Log);
            _server.Start();
        }
        catch (Exception ex)
        {
            _server = null;
            Log($"[server] failed to start on port {_settings.ServerPort}: {ex.Message}");
            ShowBalloon($"STT server couldn't start on port {_settings.ServerPort}: {ex.Message}", ToolTipIcon.Warning);
        }
    }

    private void OnHotkeyDown()
    {
        if (_recorder.IsRecording) return;

        if (!ModelManager.IsDownloaded(_settings.ModelSize))
        {
            ShowBalloon($"Model '{_settings.ModelSize}' missing — open Settings to download it.", ToolTipIcon.Warning);
            return;
        }

        string? error = _recorder.Start(_settings.MaxRecordSeconds);
        if (error != null)
        {
            ShowBalloon(error, ToolTipIcon.Error);
            return;
        }

        _tray.Icon = _activeIcon;
        _overlay.ShowRecording();
        _maxDurationTimer.Interval = _settings.MaxRecordSeconds * 1000;
        _maxDurationTimer.Start();
    }

    private void OnHotkeyUp()
    {
        _maxDurationTimer.Stop();
        if (!_recorder.IsRecording) return;

        float[] samples = _recorder.Stop();
        float peak = _recorder.LastPeak;
        _tray.Icon = _idleIcon;

        if (samples.Length < 16000 / 4) // < ~250 ms — accidental tap, skip silently
        {
            _overlay.HideOverlay();
            return;
        }

        _overlay.ShowTranscribing();
        string modelPath = ModelManager.PathFor(_settings.ModelSize);
        bool paste = _settings.UseClipboardPaste;

        Task.Run(async () =>
        {
            try
            {
                DumpLastRecording(samples);
                string raw = await _transcriber.TranscribeAsync(samples, modelPath);
                string text = Transcriber.StripNoise(raw);
                Log($"audio={samples.Length / 16000.0:F1}s peak={peak:F3} raw=\"{raw}\" text=\"{text}\"");
                BeginInvokeOnUi(() =>
                {
                    _overlay.HideOverlay();
                    if (text.Length == 0)
                    {
                        // Distinguish "mic delivered silence" (device/permission problem)
                        // from "audio had no recognizable speech".
                        ShowBalloon(peak < 0.01f
                            ? "The microphone recorded silence — check the default recording device in Windows Sound settings and mic privacy permissions."
                            : "Didn't catch any speech in that recording.", ToolTipIcon.Warning);
                        return;
                    }
                    try
                    {
                        // Some apps garble rapid synthetic keystrokes — paste there
                        // regardless of the configured method.
                        if (paste || TextInjector.ForegroundNeedsPaste()) TextInjector.PasteText(text);
                        else TextInjector.TypeText(text);
                    }
                    catch (Exception ex)
                    {
                        ShowBalloon($"Could not type the text: {ex.Message}", ToolTipIcon.Error);
                    }
                });
            }
            catch (Exception ex)
            {
                BeginInvokeOnUi(() =>
                {
                    _overlay.HideOverlay();
                    ShowBalloon($"Transcription failed: {ex.Message}", ToolTipIcon.Error);
                });
            }
        });
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_settings);
        if (_settingsForm.ShowDialog() == DialogResult.OK)
        {
            _hook.HotkeyVk = _settings.HotkeyVk;
            _tray.Text = $"Murmur — hold {_settings.HotkeyDisplayName} to dictate";

            // Apply server-mode changes without a restart.
            bool running = _server != null;
            if (running && (!_settings.ServerEnabled || _server!.Port != _settings.ServerPort))
            {
                _server!.Dispose();
                _server = null;
            }
            if (_server == null && _settings.ServerEnabled) StartServerIfEnabled();
        }
        _settingsForm = null;
    }

    private void ShowBalloon(string message, ToolTipIcon icon) =>
        _tray.ShowBalloonTip(4000, "Murmur", message, icon);

    /// <summary>Keep the last dictation's audio on disk (local only) so mic problems can be diagnosed.</summary>
    private static void DumpLastRecording(float[] samples)
    {
        try
        {
            using var writer = new NAudio.Wave.WaveFileWriter(
                Path.Combine(Settings.SettingsDir, "last-recording.wav"),
                new NAudio.Wave.WaveFormat(16000, 16, 1));
            writer.WriteSamples(samples, 0, samples.Length);
        }
        catch { /* diagnostics only */ }
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(Settings.SettingsDir, "Murmur.log"),
                $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch { /* logging must never break the pipeline */ }
    }

    private void BeginInvokeOnUi(Action action)
    {
        // The overlay form gives us a handle bound to the UI thread.
        if (_overlay.IsHandleCreated) _overlay.BeginInvoke(action);
        else action();
    }

    protected override void ExitThreadCore()
    {
        _server?.Dispose();
        _hook.Dispose();          // unhook first so no more events arrive
        _maxDurationTimer.Dispose();
        _recorder.Dispose();
        _transcriber.Dispose();
        _overlay.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _idleIcon.Dispose();
        _activeIcon.Dispose();
        base.ExitThreadCore();
    }
}

internal static class TrayIcons
{
    /// <summary>Load a bundled Murmur icon, falling back to a drawn glyph if the asset is missing.</summary>
    public static Icon Load(string fileName, Color fallbackColor)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
            if (File.Exists(path)) return new Icon(path);
        }
        catch { /* fall back to the drawn glyph */ }
        return Make(fallbackColor);
    }

    /// <summary>Draw a simple microphone glyph so we don't need a bundled .ico asset.</summary>
    public static Icon Make(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            using var pen = new Pen(color, 3);
            g.FillEllipse(brush, 11, 3, 10, 16);            // capsule
            g.DrawArc(pen, 7, 8, 18, 16, 0, 180);           // cradle
            g.DrawLine(pen, 16, 24, 16, 29);                // stem
            g.DrawLine(pen, 10, 29, 22, 29);                // base
        }
        IntPtr h = bmp.GetHicon();
        using var tmp = Icon.FromHandle(h);
        var icon = (Icon)tmp.Clone(); // clone so we can DestroyIcon the GDI handle
        DestroyIcon(h);
        return icon;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
