using NAudio.Wave;

namespace Murmur;

/// <summary>
/// Captures microphone audio as 16 kHz / 16-bit / mono PCM (what whisper.cpp expects)
/// into memory. Start() on hotkey down, Stop() on hotkey up returns float samples.
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _buffer;
    private readonly ManualResetEventSlim _stopped = new(false);
    private readonly object _sync = new();
    private long _maxBytes;

    public bool IsRecording { get; private set; }

    /// <summary>Peak amplitude (0..1) of the last recording — 0 means the mic delivered pure silence.</summary>
    public float LastPeak { get; private set; }

    /// <summary>Raised if the device stops with an error mid-recording (e.g. mic unplugged).</summary>
    public event Action<Exception>? RecordingError;

    /// <summary>
    /// Begin capturing. Returns an error message on failure (no mic, device busy), or null on success.
    /// </summary>
    public string? Start(int maxSeconds)
    {
        lock (_sync)
        {
            if (IsRecording) return null;

            if (WaveInEvent.DeviceCount == 0)
                return "No microphone found. Plug one in (or check Windows privacy settings) and try again.";

            _buffer = new MemoryStream();
            _maxBytes = (long)maxSeconds * 16000 * 2;
            _stopped.Reset();

            _waveIn = new WaveInEvent
            {
                DeviceNumber = -1, // WAVE_MAPPER: the Windows default recording device,
                                   // NOT device 0 (which is just whatever enumerates first)
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 50,
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            try
            {
                _waveIn.StartRecording();
            }
            catch (Exception ex)
            {
                CleanupDevice();
                return $"Could not open the microphone: {ex.Message}";
            }

            IsRecording = true;
            return null;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_sync)
        {
            if (_buffer == null) return;
            _buffer.Write(e.Buffer, 0, e.BytesRecorded);
            if (_buffer.Length > _maxBytes && _waveIn != null)
                _waveIn.StopRecording(); // safety cap; captured audio is still returned by Stop()
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _stopped.Set();
        if (e.Exception != null && IsRecording)
            RecordingError?.Invoke(e.Exception);
    }

    /// <summary>Stop capturing and return the audio as 16 kHz mono float samples.</summary>
    public float[] Stop()
    {
        WaveInEvent? device;
        lock (_sync)
        {
            if (!IsRecording || _waveIn == null) return Array.Empty<float>();
            IsRecording = false;
            device = _waveIn;
        }

        try { device.StopRecording(); } catch { /* device may already be gone */ }
        _stopped.Wait(TimeSpan.FromMilliseconds(500)); // let the final buffer flush

        lock (_sync)
        {
            byte[] bytes = _buffer?.ToArray() ?? Array.Empty<byte>();
            CleanupDevice();

            var samples = new float[bytes.Length / 2];
            float peak = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
                float abs = Math.Abs(samples[i]);
                if (abs > peak) peak = abs;
            }
            LastPeak = peak;
            return samples;
        }
    }

    private void CleanupDevice()
    {
        _waveIn?.Dispose();
        _waveIn = null;
        _buffer?.Dispose();
        _buffer = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            IsRecording = false;
            CleanupDevice();
        }
        _stopped.Dispose();
    }
}
