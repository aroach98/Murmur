namespace Murmur;

/// <summary>
/// Locates and downloads ggml whisper models. Download is a one-time setup step;
/// the transcription pipeline itself never touches the network.
/// </summary>
public static class ModelManager
{
    public static readonly string[] Sizes = { "tiny", "base", "small", "medium" };

    public static readonly Dictionary<string, string> ApproxSize = new()
    {
        ["tiny"] = "75 MB",
        ["base"] = "142 MB",
        ["small"] = "466 MB",
        ["medium"] = "1.5 GB",
    };

    public static string ModelsDir => Path.Combine(Settings.SettingsDir, "models");

    public static string PathFor(string size) => Path.Combine(ModelsDir, $"ggml-{size}.en.bin");

    public static bool IsDownloaded(string size) => File.Exists(PathFor(size));

    public static async Task DownloadAsync(string size, IProgress<double> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(ModelsDir);
        string url = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{size}.en.bin";
        string tmp = PathFor(size) + ".partial";

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? -1;
        await using (var src = await response.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(tmp))
        {
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress.Report((double)done / total);
            }
        }
        File.Move(tmp, PathFor(size), overwrite: true);
    }
}
