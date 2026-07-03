namespace Murmur;

public sealed class SettingsForm : Form
{
    private readonly Settings _settings;
    private readonly TextBox _hotkeyBox;
    private readonly ComboBox _modelCombo;
    private readonly Label _modelStatus;
    private readonly Button _downloadButton;
    private readonly ProgressBar _progress;
    private readonly RadioButton _typeRadio;
    private readonly RadioButton _pasteRadio;
    private int _pendingVk;
    private CancellationTokenSource? _downloadCts;

    public SettingsForm(Settings settings)
    {
        _settings = settings;
        _pendingVk = settings.HotkeyVk;

        Text = "Murmur Settings";
        Icon = TrayIcons.Load("murmur.ico", Color.FromArgb(124, 58, 237));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(420, 320);
        Font = new Font("Segoe UI", 9.5f);

        var hotkeyLabel = new Label { Text = "Hold-to-record hotkey (click box, press a key):", Location = new Point(16, 18), AutoSize = true };
        _hotkeyBox = new TextBox
        {
            Location = new Point(16, 42),
            Width = 200,
            ReadOnly = true,
            Text = settings.HotkeyDisplayName,
            TabStop = true,
        };
        _hotkeyBox.KeyDown += OnHotkeyCapture;

        var modelLabel = new Label { Text = "Whisper model (bigger = more accurate, slower):", Location = new Point(16, 84), AutoSize = true };
        _modelCombo = new ComboBox
        {
            Location = new Point(16, 108),
            Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        foreach (var size in ModelManager.Sizes)
            _modelCombo.Items.Add($"{size}  ({ModelManager.ApproxSize[size]})");
        _modelCombo.SelectedIndex = Math.Max(0, Array.IndexOf(ModelManager.Sizes, settings.ModelSize));
        _modelCombo.SelectedIndexChanged += (_, _) => UpdateModelStatus();

        _modelStatus = new Label { Location = new Point(16, 140), AutoSize = true };
        _downloadButton = new Button { Text = "Download model", Location = new Point(232, 106), Width = 160 };
        _downloadButton.Click += OnDownloadClick;
        _progress = new ProgressBar { Location = new Point(16, 164), Width = 376, Height = 14, Visible = false };

        var injectLabel = new Label { Text = "Text injection method:", Location = new Point(16, 192), AutoSize = true };
        _typeRadio = new RadioButton
        {
            Text = "Type keystrokes (recommended — works almost everywhere)",
            Location = new Point(16, 214),
            AutoSize = true,
            Checked = !settings.UseClipboardPaste,
        };
        _pasteRadio = new RadioButton
        {
            Text = "Clipboard paste (fallback for apps that ignore typed input)",
            Location = new Point(16, 238),
            AutoSize = true,
            Checked = settings.UseClipboardPaste,
        };

        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Location = new Point(232, 276), Width = 76 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(316, 276), Width = 76 };
        ok.Click += (_, _) => ApplySettings();
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.AddRange(new Control[]
        {
            hotkeyLabel, _hotkeyBox, modelLabel, _modelCombo, _modelStatus,
            _downloadButton, _progress, injectLabel, _typeRadio, _pasteRadio, ok, cancel,
        });

        UpdateModelStatus();
    }

    private string SelectedSize => ModelManager.Sizes[_modelCombo.SelectedIndex];

    private void OnHotkeyCapture(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;

        var key = e.KeyCode;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            _hotkeyBox.Text = "Pick a non-modifier key (e.g. F9, CapsLock, Pause)";
            return;
        }
        // Warn on plain character keys — the hotkey is swallowed globally, so binding
        // a letter would make that letter untypable system-wide.
        if ((key >= Keys.A && key <= Keys.Z) || (key >= Keys.D0 && key <= Keys.D9) || key == Keys.Space)
        {
            _hotkeyBox.Text = $"'{key}' would block normal typing — pick an F-key or similar";
            return;
        }

        _pendingVk = (int)key;
        _hotkeyBox.Text = KeyNames.NameOf(_pendingVk);
    }

    private void UpdateModelStatus()
    {
        bool have = ModelManager.IsDownloaded(SelectedSize);
        _modelStatus.Text = have ? "✔ Model downloaded" : "✘ Model not downloaded yet";
        _modelStatus.ForeColor = have ? Color.Green : Color.Firebrick;
        _downloadButton.Enabled = !have;
    }

    private async void OnDownloadClick(object? sender, EventArgs e)
    {
        _downloadButton.Enabled = false;
        _progress.Visible = true;
        _downloadCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(p => _progress.Value = (int)(p * 100));
            await ModelManager.DownloadAsync(SelectedSize, progress, _downloadCts.Token);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Download failed: {ex.Message}", "Murmur", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _progress.Visible = false;
            UpdateModelStatus();
        }
    }

    private void ApplySettings()
    {
        _settings.HotkeyVk = _pendingVk;
        _settings.ModelSize = SelectedSize;
        _settings.UseClipboardPaste = _pasteRadio.Checked;
        _settings.Save();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _downloadCts?.Cancel();
        base.OnFormClosed(e);
    }
}
