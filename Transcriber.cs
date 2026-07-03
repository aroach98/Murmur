using System.Text;
using System.Text.RegularExpressions;
using Whisper.net;

namespace Murmur;

/// <summary>
/// Wraps Whisper.net (whisper.cpp). The factory (model weights) is loaded lazily on
/// first use and cached until the model path changes. Calls are serialized — whisper
/// processors are not thread-safe and concurrent transcriptions are pointless here.
/// </summary>
public sealed class Transcriber : IDisposable
{
    private WhisperFactory? _factory;
    private string? _loadedModelPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Whisper emits bracketed/parenthesized noise annotations for non-speech audio.
    private static readonly Regex NoiseToken = new(@"[\[\(][^\]\)]*[\]\)]", RegexOptions.Compiled);

    public async Task<string> TranscribeAsync(float[] samples, string modelPath, CancellationToken ct = default)
    {
        if (samples.Length < 16000 / 4) return ""; // under ~250 ms of audio: accidental tap

        NormalizeGain(samples);
        await _gate.WaitAsync(ct);
        try
        {
            if (_factory == null || _loadedModelPath != modelPath)
            {
                _factory?.Dispose();
                _factory = WhisperFactory.FromPath(modelPath);
                _loadedModelPath = modelPath;
            }

            await using var processor = _factory.CreateBuilder()
                .WithLanguage("en")
                .WithThreads(Math.Max(2, Environment.ProcessorCount / 2))
                .Build();

            var sb = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples, ct))
                sb.Append(segment.Text);

            return sb.ToString().Trim(); // raw whisper output — callers apply StripNoise
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Quiet mics (gain turned way down) still transcribe, but with less margin —
    /// scale low-peak recordings up toward full scale before feeding whisper.
    /// </summary>
    private static void NormalizeGain(float[] samples)
    {
        float peak = 0f;
        foreach (float s in samples)
        {
            float abs = Math.Abs(s);
            if (abs > peak) peak = abs;
        }
        if (peak < 0.001f || peak >= 0.25f) return; // silence, or already loud enough

        float scale = 0.9f / peak;
        for (int i = 0; i < samples.Length; i++)
            samples[i] *= scale;
    }

    /// <summary>Returns "" if the text is nothing but whisper noise annotations ([BLANK_AUDIO], (wind), …).</summary>
    public static string StripNoise(string text)
    {
        return NoiseToken.Replace(text, "").Trim().Length == 0 ? "" : text;
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _factory = null;
        _gate.Dispose();
    }
}
