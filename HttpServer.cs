using System.Net;
using System.Text;
using System.Text.Json;

namespace Murmur;

/// <summary>
/// Server mode: the Whisper pipeline over loopback HTTP, so other local apps
/// (jarvis-core) can use Murmur as their STT service.
///
///   GET  /health      → { "ok": true, "model": "base", "downloaded": true }
///   POST /transcribe  (body: audio/wav, any rate/channels) → { "text": "...", "raw": "..." }
///
/// Binds 127.0.0.1 only. Transcriptions share the app's long-lived
/// <see cref="Transcriber"/>, whose internal gate already serializes them
/// against hotkey dictations.
/// </summary>
public sealed class MurmurServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Transcriber _transcriber;
    private readonly Func<string> _modelSize;
    private readonly Action<string> _log;

    public int Port { get; }

    public MurmurServer(int port, Transcriber transcriber, Func<string> modelSize, Action<string> log)
    {
        Port = port;
        _transcriber = transcriber;
        _modelSize = modelSize;
        _log = log;
    }

    public void Start()
    {
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _ = Task.Run(AcceptLoop);
        _log($"STT server listening on http://127.0.0.1:{Port}/");
    }

    private async Task AcceptLoop()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception) when (!_listener.IsListening) { return; } // disposed
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            string path = ctx.Request.Url?.AbsolutePath ?? "/";
            if (ctx.Request.HttpMethod == "GET" && path == "/health")
            {
                string size = _modelSize();
                await WriteJson(ctx.Response, 200, new
                {
                    ok = true,
                    model = size,
                    downloaded = ModelManager.IsDownloaded(size),
                });
                return;
            }

            if (ctx.Request.HttpMethod == "POST" && path == "/transcribe")
            {
                string size = _modelSize();
                string modelPath = ModelManager.PathFor(size);
                if (!File.Exists(modelPath))
                {
                    await WriteJson(ctx.Response, 503, new { error = $"model '{size}' not downloaded" });
                    return;
                }

                // WaveFileReader needs a seekable stream — buffer the body first.
                using var body = new MemoryStream();
                await ctx.Request.InputStream.CopyToAsync(body);
                body.Position = 0;

                float[] samples = WavAudio.LoadWavAs16kMono(body);
                string raw = await _transcriber.TranscribeAsync(samples, modelPath);
                string text = Transcriber.StripNoise(raw);
                _log($"[server] transcribed {samples.Length / 16000.0:F1}s → \"{text}\"");
                await WriteJson(ctx.Response, 200, new { text, raw });
                return;
            }

            await WriteJson(ctx.Response, 404, new { error = "unknown endpoint" });
        }
        catch (Exception ex)
        {
            _log($"[server] request failed: {ex.Message}");
            try { await WriteJson(ctx.Response, 500, new { error = ex.Message }); }
            catch { /* client gone */ }
        }
    }

    private static async Task WriteJson(HttpListenerResponse res, int status, object payload)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        res.StatusCode = status;
        res.ContentType = "application/json";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    public void Dispose()
    {
        try { _listener.Stop(); _listener.Close(); }
        catch { /* already down */ }
    }
}
