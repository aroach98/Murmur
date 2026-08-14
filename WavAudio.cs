using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Murmur;

/// <summary>Whisper wants 16 kHz mono floats; these load/convert any WAV into that.</summary>
public static class WavAudio
{
    /// <summary>Load a WAV file, downmixing/resampling as needed.</summary>
    public static float[] LoadWavAs16kMono(string path)
    {
        using var reader = new AudioFileReader(path);
        return Convert(reader);
    }

    /// <summary>
    /// Same for an in-memory WAV (server mode's uploaded body). The stream must be
    /// seekable — buffer a network body into a MemoryStream first.
    /// </summary>
    public static float[] LoadWavAs16kMono(Stream wavStream)
    {
        using var reader = new WaveFileReader(wavStream);
        return Convert(reader.ToSampleProvider());
    }

    private static float[] Convert(ISampleProvider provider)
    {
        if (provider.WaveFormat.Channels == 2)
            provider = new StereoToMonoSampleProvider(provider);
        if (provider.WaveFormat.SampleRate != 16000)
            provider = new WdlResamplingSampleProvider(provider, 16000);

        var all = new List<float>(1 << 20);
        var buffer = new float[16000];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
            all.AddRange(buffer.Take(read));
        return all.ToArray();
    }
}
