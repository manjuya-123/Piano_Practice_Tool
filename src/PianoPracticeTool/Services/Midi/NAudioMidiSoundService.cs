using System.Collections.Concurrent;
using System.IO;
using NAudio.Midi;
using NAudio.Wave;

namespace PianoPracticeTool.Services.Midi;

public sealed class NAudioMidiSoundService : IMidiSoundService
{
    private const int ClickSampleRate = 44100;
    private const int ClickChannels = 1;
    private const int ClickBitsPerSample = 16;
    private const int BuiltInDesiredLatencyMilliseconds = 60;
    private const int BuiltInBufferCount = 3;
    private const double MaximumClickAmplitude = 0.34d;

    private readonly object _outputSyncRoot = new();
    private readonly object _clickSyncRoot = new();
    private readonly ConcurrentDictionary<ClickSampleKey, byte[]> _clickSampleCache = new();
    private MidiOut? _midiOut;
    private WaveOutEvent? _builtInOutput;
    private MeltySynthSoundFontSampleProvider? _builtInProvider;
    private WaveOutEvent? _clickOutput;
    private BufferedWaveProvider? _clickBuffer;
    private SoundFontConfiguration? _soundFontConfiguration;

    public bool IsAvailable
    {
        get
        {
            lock (_outputSyncRoot)
            {
                return _midiOut is not null || _builtInOutput is not null;
            }
        }
    }

    public string? DeviceName { get; private set; }

    public IReadOnlyList<MidiOutputDevice> GetDevices()
    {
        var devices = new List<MidiOutputDevice>();
        if (_soundFontConfiguration is not null
            && File.Exists(_soundFontConfiguration.FilePath))
        {
            devices.Add(MidiOutputDevice.BuiltInSoundFont);
        }

        for (var deviceNumber = 0; deviceNumber < MidiOut.NumberOfDevices; deviceNumber++)
        {
            var capabilities = MidiOut.DeviceInfo(deviceNumber);
            devices.Add(new MidiOutputDevice(deviceNumber, capabilities.ProductName));
        }

        return devices;
    }

    public void Initialize()
    {
        lock (_outputSyncRoot)
        {
            DisconnectOutputCore();
        }
    }

    public void Connect(MidiOutputDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        lock (_outputSyncRoot)
        {
            DisconnectOutputCore();
            if (device.IsBuiltIn)
            {
                ConnectBuiltInSoundFontCore();
            }
            else
            {
                var midiOut = new MidiOut(device.DeviceNumber);
                midiOut.Send(MidiMessage.ChangePatch(0, 1).RawData);
                _midiOut = midiOut;
                DeviceName = device.Name;
            }
        }
    }

    public void Disconnect()
    {
        lock (_outputSyncRoot)
        {
            DisconnectOutputCore();
        }
    }

    public void ConfigureBuiltInSoundFont(SoundFontConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateSoundFontConfiguration(configuration);

        var normalized = configuration with
        {
            FilePath = Path.GetFullPath(configuration.FilePath)
        };

        lock (_outputSyncRoot)
        {
            var pathChanged = _soundFontConfiguration is null
                || !string.Equals(
                    _soundFontConfiguration.FilePath,
                    normalized.FilePath,
                    StringComparison.OrdinalIgnoreCase);
            _soundFontConfiguration = normalized;

            if (_builtInProvider is null)
            {
                return;
            }

            if (pathChanged)
            {
                DisconnectOutputCore();
                ConnectBuiltInSoundFontCore();
                return;
            }

            _builtInProvider.Configure(
                normalized.VolumePercent,
                normalized.ReverbPercent,
                normalized.BankNumber,
                normalized.PatchNumber);
        }
    }

    public void NoteOn(int midiNote, int velocity, int channel = 1)
    {
        ValidateMidiNote(midiNote);
        ValidateMidiValue(velocity, nameof(velocity));
        ValidateChannel(channel);

        lock (_outputSyncRoot)
        {
            if (_builtInProvider is not null)
            {
                _builtInProvider.NoteOn(midiNote, velocity);
                return;
            }

            _midiOut?.Send(MidiMessage.StartNote(midiNote, velocity, channel).RawData);
        }
    }

    public void NoteOff(int midiNote, int velocity = 0, int channel = 1)
    {
        ValidateMidiNote(midiNote);
        ValidateMidiValue(velocity, nameof(velocity));
        ValidateChannel(channel);

        lock (_outputSyncRoot)
        {
            if (_builtInProvider is not null)
            {
                _builtInProvider.NoteOff(midiNote, velocity);
                return;
            }

            _midiOut?.Send(MidiMessage.StopNote(midiNote, velocity, channel).RawData);
        }
    }

    public void ControlChange(int controller, int value, int channel = 1)
    {
        ValidateMidiValue(controller, nameof(controller));
        ValidateMidiValue(value, nameof(value));
        ValidateChannel(channel);

        lock (_outputSyncRoot)
        {
            if (_builtInProvider is not null)
            {
                _builtInProvider.ControlChange(controller, value);
                return;
            }

            _midiOut?.Send(MidiMessage.ChangeControl(controller, value, channel).RawData);
        }
    }

    public void ProgramChange(int program, int channel = 1)
    {
        ValidateMidiValue(program, nameof(program));
        ValidateChannel(channel);

        lock (_outputSyncRoot)
        {
            if (_builtInProvider is not null)
            {
                _builtInProvider.ProgramChange(program);
                return;
            }

            _midiOut?.Send(MidiMessage.ChangePatch(program, channel).RawData);
        }
    }

    public void PlayClick(bool accent, int volumePercent)
    {
        if (volumePercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(volumePercent));
        }

        if (volumePercent == 0)
        {
            return;
        }

        var key = new ClickSampleKey(accent, volumePercent);
        var samples = _clickSampleCache.GetOrAdd(
            key,
            static item => CreateClickSamples(item.Accent, item.VolumePercent));

        lock (_clickSyncRoot)
        {
            if (_clickOutput is null)
            {
                InitializeClickOutput();
            }

            _clickBuffer!.AddSamples(samples, 0, samples.Length);
        }
    }

    public void StopClicks()
    {
        lock (_clickSyncRoot)
        {
            _clickBuffer?.ClearBuffer();
        }
    }

    public void AllNotesOff()
    {
        lock (_outputSyncRoot)
        {
            _builtInProvider?.AllNotesOff();
            _midiOut?.Reset();
        }

        lock (_clickSyncRoot)
        {
            _clickBuffer?.ClearBuffer();
        }
    }

    public void Dispose()
    {
        lock (_outputSyncRoot)
        {
            DisconnectOutputCore();
        }

        lock (_clickSyncRoot)
        {
            _clickOutput?.Stop();
            _clickOutput?.Dispose();
            _clickOutput = null;
            _clickBuffer = null;
        }

        GC.SuppressFinalize(this);
    }

    private void ConnectBuiltInSoundFontCore()
    {
        var configuration = _soundFontConfiguration
            ?? throw new InvalidOperationException(
                "A SoundFont must be configured before connecting the built-in synthesizer.");

        if (!File.Exists(configuration.FilePath))
        {
            throw new FileNotFoundException("SoundFont was not found.", configuration.FilePath);
        }

        var provider = new MeltySynthSoundFontSampleProvider(configuration);
        var output = new WaveOutEvent
        {
            DesiredLatency = BuiltInDesiredLatencyMilliseconds,
            NumberOfBuffers = BuiltInBufferCount
        };

        try
        {
            output.Init(provider);
            output.Play();
            _builtInProvider = provider;
            _builtInOutput = output;
            DeviceName = MidiOutputDevice.BuiltInSoundFont.Name;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private void InitializeClickOutput()
    {
        _clickOutput?.Stop();
        _clickOutput?.Dispose();

        var waveFormat = new WaveFormat(ClickSampleRate, ClickBitsPerSample, ClickChannels);
        var buffer = new BufferedWaveProvider(waveFormat)
        {
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };
        var output = new WaveOutEvent
        {
            DesiredLatency = 40,
            NumberOfBuffers = 2
        };

        output.Init(buffer);
        output.Play();
        _clickBuffer = buffer;
        _clickOutput = output;
    }

    private void DisconnectOutputCore()
    {
        _midiOut?.Reset();
        _midiOut?.Dispose();
        _midiOut = null;

        _builtInProvider?.AllNotesOff();
        _builtInOutput?.Stop();
        _builtInOutput?.Dispose();
        _builtInOutput = null;
        _builtInProvider = null;

        DeviceName = null;
    }

    private static byte[] CreateClickSamples(bool accent, int volumePercent)
    {
        var durationSeconds = accent ? 0.046d : 0.034d;
        var fundamentalFrequency = accent ? 1900d : 1350d;
        var sampleCount = Math.Max(1, (int)Math.Round(ClickSampleRate * durationSeconds));
        var result = new byte[sampleCount * sizeof(short)];
        var userGain = volumePercent / 100d;
        var accentGain = accent ? 1d : 0.82d;
        var outputGain = MaximumClickAmplitude * userGain * accentGain;
        var attackSamples = Math.Max(1d, ClickSampleRate * 0.0012d);

        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            var time = sampleIndex / (double)ClickSampleRate;
            var progress = sampleIndex / (double)Math.Max(1, sampleCount - 1);
            var attackEnvelope = Math.Min(1d, sampleIndex / attackSamples);
            var decayEnvelope = Math.Exp(-6.2d * progress);
            var envelope = attackEnvelope * decayEnvelope;
            var tone = Math.Sin(2d * Math.PI * fundamentalFrequency * time) * 0.76d
                + Math.Sin(2d * Math.PI * fundamentalFrequency * 1.87d * time) * 0.18d;
            var sample = tone * envelope * outputGain;
            var pcm = (short)Math.Round(Math.Clamp(sample, -0.95d, 0.95d) * short.MaxValue);
            result[sampleIndex * 2] = (byte)(pcm & 0xff);
            result[sampleIndex * 2 + 1] = (byte)((pcm >> 8) & 0xff);
        }

        return result;
    }

    private static void ValidateSoundFontConfiguration(SoundFontConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.FilePath))
        {
            throw new ArgumentException("SoundFont path is required.", nameof(configuration));
        }

        if (configuration.BankNumber is < 0 or > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(configuration));
        }

        if (configuration.PatchNumber is < 0 or > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(configuration));
        }

        if (configuration.VolumePercent is < 0 or > 150)
        {
            throw new ArgumentOutOfRangeException(nameof(configuration));
        }

        if (configuration.ReverbPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(configuration));
        }
    }

    private static void ValidateMidiNote(int midiNote)
    {
        if (midiNote is < 0 or > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(midiNote));
        }
    }

    private static void ValidateMidiValue(int value, string parameterName)
    {
        if (value is < 0 or > 127)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateChannel(int channel)
    {
        if (channel is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }
    }

    private readonly record struct ClickSampleKey(bool Accent, int VolumePercent);
}
