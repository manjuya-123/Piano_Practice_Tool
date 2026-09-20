namespace PianoPracticeTool.Services.Midi;

internal sealed class StereoReverbProcessor
{
    private const int ChannelCount = 2;
    private const float CombFeedback = 0.78f;
    private const float CombDamping = 0.22f;
    private const float AllPassFeedback = 0.50f;
    private const float ReverbInputGain = 0.45f;
    private const float MaximumWetGain = 0.90f;
    private const float MaximumDryReduction = 0.35f;

    private static readonly int[] LeftCombDelaysAt44100Hz = { 1116, 1188, 1277, 1356 };
    private static readonly int[] RightCombDelaysAt44100Hz = { 1139, 1211, 1300, 1379 };
    private static readonly int[] LeftAllPassDelaysAt44100Hz = { 556, 441 };
    private static readonly int[] RightAllPassDelaysAt44100Hz = { 579, 464 };

    private readonly CombFilter[] _leftCombFilters;
    private readonly CombFilter[] _rightCombFilters;
    private readonly AllPassFilter[] _leftAllPassFilters;
    private readonly AllPassFilter[] _rightAllPassFilters;
    private int _mixPercent;

    public StereoReverbProcessor(int sampleRate, int mixPercent)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        _leftCombFilters = CreateCombFilters(sampleRate, LeftCombDelaysAt44100Hz);
        _rightCombFilters = CreateCombFilters(sampleRate, RightCombDelaysAt44100Hz);
        _leftAllPassFilters = CreateAllPassFilters(sampleRate, LeftAllPassDelaysAt44100Hz);
        _rightAllPassFilters = CreateAllPassFilters(sampleRate, RightAllPassDelaysAt44100Hz);
        SetMixPercent(mixPercent);
    }

    public int MixPercent => _mixPercent;

    public void SetMixPercent(int mixPercent)
    {
        var normalized = Math.Clamp(mixPercent, 0, 100);
        if (normalized == 0 && _mixPercent != 0)
        {
            Clear();
        }

        _mixPercent = normalized;
    }

    public void Process(Span<float> interleavedSamples)
    {
        if (interleavedSamples.Length % ChannelCount != 0)
        {
            throw new ArgumentException(
                "Stereo reverb requires an even number of interleaved samples.",
                nameof(interleavedSamples));
        }

        if (_mixPercent == 0)
        {
            return;
        }

        var mix = _mixPercent / 100f;
        var dryGain = 1f - MaximumDryReduction * mix;
        var wetGain = MaximumWetGain * mix;

        for (var index = 0; index < interleavedSamples.Length; index += ChannelCount)
        {
            var dryLeft = interleavedSamples[index];
            var dryRight = interleavedSamples[index + 1];
            var reverbInput = (dryLeft + dryRight) * 0.5f * ReverbInputGain;

            var wetLeft = ProcessChannel(
                reverbInput,
                _leftCombFilters,
                _leftAllPassFilters);
            var wetRight = ProcessChannel(
                reverbInput,
                _rightCombFilters,
                _rightAllPassFilters);

            interleavedSamples[index] = dryLeft * dryGain + wetLeft * wetGain;
            interleavedSamples[index + 1] = dryRight * dryGain + wetRight * wetGain;
        }
    }

    private static float ProcessChannel(
        float input,
        IReadOnlyList<CombFilter> combFilters,
        IReadOnlyList<AllPassFilter> allPassFilters)
    {
        var combined = 0f;
        foreach (var filter in combFilters)
        {
            combined += filter.Process(input);
        }

        combined /= combFilters.Count;
        foreach (var filter in allPassFilters)
        {
            combined = filter.Process(combined);
        }

        return combined;
    }

    private static CombFilter[] CreateCombFilters(
        int sampleRate,
        IReadOnlyList<int> baseDelays)
        => baseDelays
            .Select(delay => new CombFilter(ScaleDelay(sampleRate, delay)))
            .ToArray();

    private static AllPassFilter[] CreateAllPassFilters(
        int sampleRate,
        IReadOnlyList<int> baseDelays)
        => baseDelays
            .Select(delay => new AllPassFilter(ScaleDelay(sampleRate, delay)))
            .ToArray();

    private static int ScaleDelay(int sampleRate, int delayAt44100Hz)
        => Math.Max(1, (int)Math.Round(sampleRate / 44100d * delayAt44100Hz));

    private void Clear()
    {
        foreach (var filter in _leftCombFilters)
        {
            filter.Clear();
        }

        foreach (var filter in _rightCombFilters)
        {
            filter.Clear();
        }

        foreach (var filter in _leftAllPassFilters)
        {
            filter.Clear();
        }

        foreach (var filter in _rightAllPassFilters)
        {
            filter.Clear();
        }
    }

    private sealed class CombFilter
    {
        private readonly float[] _buffer;
        private int _index;
        private float _filterStore;

        public CombFilter(int delaySamples)
        {
            _buffer = new float[delaySamples];
        }

        public float Process(float input)
        {
            var output = _buffer[_index];
            _filterStore = output * (1f - CombDamping) + _filterStore * CombDamping;
            _buffer[_index] = input + _filterStore * CombFeedback;

            _index++;
            if (_index == _buffer.Length)
            {
                _index = 0;
            }

            return output;
        }

        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _index = 0;
            _filterStore = 0f;
        }
    }

    private sealed class AllPassFilter
    {
        private readonly float[] _buffer;
        private int _index;

        public AllPassFilter(int delaySamples)
        {
            _buffer = new float[delaySamples];
        }

        public float Process(float input)
        {
            var buffered = _buffer[_index];
            var output = buffered - input;
            _buffer[_index] = input + buffered * AllPassFeedback;

            _index++;
            if (_index == _buffer.Length)
            {
                _index = 0;
            }

            return output;
        }

        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _index = 0;
        }
    }
}
