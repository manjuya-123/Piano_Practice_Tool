using NAudio.Wave;

namespace PianoPracticeTool.Services.Editor;

internal sealed class EditorStereoPanSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceChannels;
    private float[] _monoBuffer = Array.Empty<float>();
    private float _pan;

    public EditorStereoPanSampleProvider(
        ISampleProvider source)
    {
        _source = source
            ?? throw new ArgumentNullException(
                nameof(source));
        _sourceChannels = source.WaveFormat.Channels;
        if (_sourceChannels <= 0)
        {
            throw new ArgumentException(
                "Audio source must contain at least one channel.",
                nameof(source));
        }

        WaveFormat = _sourceChannels == 1
            ? NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(
                source.WaveFormat.SampleRate,
                2)
            : source.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }

    public float Pan
    {
        get => Volatile.Read(ref _pan);
        set => Volatile.Write(
            ref _pan,
            Math.Clamp(
                value,
                -1f,
                1f));
    }

    public int Read(
        float[] buffer,
        int offset,
        int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0
            || count < 0
            || offset + count > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count));
        }

        return _sourceChannels == 1
            ? ReadMonoAsStereo(
                buffer,
                offset,
                count)
            : ReadMultiChannel(
                buffer,
                offset,
                count);
    }

    private int ReadMonoAsStereo(
        float[] buffer,
        int offset,
        int count)
    {
        var frameCapacity = count / 2;
        if (frameCapacity <= 0)
        {
            return 0;
        }

        EnsureMonoBuffer(frameCapacity);
        var samplesRead = _source.Read(
            _monoBuffer,
            0,
            frameCapacity);
        GetChannelGains(
            out var leftGain,
            out var rightGain);

        for (var sampleIndex = 0;
             sampleIndex < samplesRead;
             sampleIndex++)
        {
            var sample = _monoBuffer[sampleIndex];
            var outputIndex =
                offset + sampleIndex * 2;
            buffer[outputIndex] =
                sample * leftGain;
            buffer[outputIndex + 1] =
                sample * rightGain;
        }

        return samplesRead * 2;
    }

    private int ReadMultiChannel(
        float[] buffer,
        int offset,
        int count)
    {
        var samplesRead = _source.Read(
            buffer,
            offset,
            count);
        GetChannelGains(
            out var leftGain,
            out var rightGain);

        var completeFrameSamples =
            samplesRead
            - samplesRead % _sourceChannels;
        var end = offset + completeFrameSamples;
        for (var sampleIndex = offset;
             sampleIndex < end;
             sampleIndex += _sourceChannels)
        {
            buffer[sampleIndex] *=
                leftGain;
            buffer[sampleIndex + 1] *=
                rightGain;
        }

        return samplesRead;
    }

    private void GetChannelGains(
        out float leftGain,
        out float rightGain)
    {
        var pan = Pan;
        leftGain = pan > 0f
            ? 1f - pan
            : 1f;
        rightGain = pan < 0f
            ? 1f + pan
            : 1f;
    }

    private void EnsureMonoBuffer(
        int sampleCount)
    {
        if (_monoBuffer.Length >= sampleCount)
        {
            return;
        }

        _monoBuffer =
            new float[sampleCount];
    }
}
