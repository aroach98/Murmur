using System.Drawing.Drawing2D;

namespace Murmur;

/// <summary>
/// Small always-on-top pill shown at the bottom-center of the primary screen while
/// recording/transcribing. Created with WS_EX_NOACTIVATE so it never steals focus
/// from the window the user is dictating into — that would break text injection.
/// </summary>
public sealed class RecordingOverlay : Form
{
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private readonly Label _label;
    private readonly System.Windows.Forms.Timer _pulse;
    private bool _pulseOn;
    private bool _recording;

    public RecordingOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(28, 28, 30);
        Size = new Size(190, 44);

        _label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
        };
        Controls.Add(_label);

        _pulse = new System.Windows.Forms.Timer { Interval = 450 };
        _pulse.Tick += (_, _) =>
        {
            _pulseOn = !_pulseOn;
            if (_recording)
                _label.ForeColor = _pulseOn ? Color.FromArgb(255, 80, 80) : Color.White;
        };

        var screen = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Bottom - Height - 24);
        Region = new Region(RoundedRect(new Rectangle(0, 0, Width, Height), 20));
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void ShowRecording()
    {
        _recording = true;
        _label.Text = "●  Recording…";
        _label.ForeColor = Color.FromArgb(255, 80, 80);
        _pulse.Start();
        if (!Visible) Show();
    }

    public void ShowTranscribing()
    {
        _recording = false;
        _pulse.Stop();
        _label.Text = "…  Transcribing";
        _label.ForeColor = Color.FromArgb(120, 190, 255);
        if (!Visible) Show();
    }

    public void HideOverlay()
    {
        _recording = false;
        _pulse.Stop();
        Hide();
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _pulse.Dispose();
        base.Dispose(disposing);
    }
}
