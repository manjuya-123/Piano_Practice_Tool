using System.IO;
using NAudio.Wave;

namespace PianoPracticeTool.Services.Editor;

public sealed record ReferenceAudioWaveform(
    double DurationSeconds,
    IReadOnlyList<float> Peaks);

public static class ReferenceAudioWaveformService
{
    private const int DefaultPeakCount = 6000;

    public static Task<ReferenceAudioWaveform> LoadAsync(
        string filePath,
        int peakCount = DefaultPeakCount,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () => Load(
                filePath,
                peakCount,
                cancellationToken),
            cancellationToken);

    public static ReferenceAudioWaveform Load(
        string filePath,
        int peakCount = DefaultPeakCount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException(
                "元音源ファイルを指定してください。",
                nameof(filePath));
        }

        if (peakCount < 128)
        {
            throw new ArgumentOutOfRangeException(
                nameof(peakCount));
        }

        using var reader = new AudioFileReader(
            Path.GetFullPath(filePath));
        var duration =
            reader.TotalTime.TotalSeconds;
        var channels = Math.Max(
            1,
            reader.WaveFormat.Channels);
        var totalFrames = Math.Max(
            1L,
            (long)Math.Ceiling(
                duration
                * reader.WaveFormat.SampleRate));
        var peaks = new float[peakCount];
        var buffer =
            new float[4096 * channels];
        long frameIndex = 0L;

        while (true)
        {
            cancellationToken
                .ThrowIfCancellationRequested();
            var read = reader.Read(
                buffer,
                0,
                buffer.Length);
            if (read <= 0)
            {
                break;
            }

            var frameCount =
                read / channels;
            for (var frame = 0;
                 frame < frameCount;
                 frame++)
            {
                var amplitude = 0f;
                for (var channel = 0;
                     channel < channels;
                     channel++)
                {
                    amplitude = Math.Max(
                        amplitude,
                        Math.Abs(
                            buffer[
                                frame * channels
                                + channel]));
                }

                var bucket = (int)Math.Clamp(
                    frameIndex
                    * peakCount
                    / totalFrames,
                    0L,
                    peakCount - 1L);
                peaks[bucket] = Math.Max(
                    peaks[bucket],
                    amplitude);
                frameIndex++;
            }
        }

        return new ReferenceAudioWaveform(
            duration,
            peaks);
    }
}
