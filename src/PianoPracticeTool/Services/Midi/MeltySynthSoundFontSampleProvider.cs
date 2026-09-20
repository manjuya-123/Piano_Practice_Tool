using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using MeltySynth;
using NAudio.Wave;

namespace PianoPracticeTool.Services.Midi;

internal sealed class MeltySynthSoundFontSampleProvider : ISampleProvider
{
    private const int SampleRate = 44100;
    private const int ChannelCount = 2;
    private const int MelodicChannel = 0;
    private const int PercussionChannel = 9;
    private const int PercussionBankOffset = 128;
    private const int BankSelectController = 0x00;
    private const int ReverbSendController = 0x5B;

    private readonly ConcurrentQueue<SynthCommand> _commands = new();
    private readonly Synthesizer _synthesizer;
    private readonly StereoReverbProcessor _reverbProcessor;
    private int _selectedBankNumber;

    public MeltySynthSoundFontSampleProvider(SoundFontConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(configuration.FilePath))
        {
            throw new ArgumentException("SoundFont path is required.", nameof(configuration));
        }

        if (!File.Exists(configuration.FilePath))
        {
            throw new FileNotFoundException("SoundFont was not found.", configuration.FilePath);
        }

        ValidatePreset(configuration.BankNumber, configuration.PatchNumber);

        var volumePercent = Math.Clamp(configuration.VolumePercent, 0, 150);
        var reverbPercent = Math.Clamp(configuration.ReverbPercent, 0, 100);

        var settings = new SynthesizerSettings(SampleRate)
        {
            EnableReverbAndChorus = false,
            MaximumPolyphony = 64
        };
        _synthesizer = new Synthesizer(new SoundFont(configuration.FilePath), settings)
        {
            MasterVolume = volumePercent / 100f
        };
        _reverbProcessor = new StereoReverbProcessor(SampleRate, reverbPercent);
        _selectedBankNumber = configuration.BankNumber;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, ChannelCount);
        ApplyFullChannelVolume();
        ApplyPreset(configuration.BankNumber, configuration.PatchNumber);
    }

    public WaveFormat WaveFormat { get; }


    public void Configure(
        int volumePercent,
        int reverbPercent,
        int bankNumber,
        int patchNumber)
    {
        ValidatePreset(bankNumber, patchNumber);

        _commands.Enqueue(new SynthCommand(
            SynthCommandType.MasterVolume,
            0,
            0,
            Math.Clamp(volumePercent, 0, 150),
            0));

        _commands.Enqueue(new SynthCommand(
            SynthCommandType.Reverb,
            0,
            0,
            Math.Clamp(reverbPercent, 0, 100),
            0));

        Volatile.Write(ref _selectedBankNumber, bankNumber);
        _commands.Enqueue(new SynthCommand(
            SynthCommandType.Preset,
            0,
            0,
            bankNumber,
            patchNumber));
    }

    public void NoteOn(int midiNote, int velocity)
        => EnqueueMidi(GetPlaybackChannel(), 0x90, midiNote, velocity);

    public void NoteOff(int midiNote, int velocity)
        => EnqueueMidi(GetPlaybackChannel(), 0x80, midiNote, velocity);

    public void ControlChange(int controller, int value)
    {
        if (controller is BankSelectController or ReverbSendController)
        {
            return;
        }

        EnqueueMidi(GetPlaybackChannel(), 0xB0, controller, value);
    }

    public void ProgramChange(int program)
        => EnqueueMidi(GetPlaybackChannel(), 0xC0, program, 0);

    public void AllNotesOff()
        => _commands.Enqueue(new SynthCommand(SynthCommandType.AllNotesOff, 0, 0, 0, 0));

    public int Read(float[] buffer, int offset, int count)
    {
        if (count % ChannelCount != 0)
        {
            throw new ArgumentException("Stereo output requires an even sample count.", nameof(count));
        }

        ApplyPendingCommands();
        var samples = buffer.AsSpan(offset, count);
        _synthesizer.RenderInterleaved(samples);
        _reverbProcessor.Process(samples);
        return count;
    }

    private int GetPlaybackChannel()
        => Volatile.Read(ref _selectedBankNumber) >= PercussionBankOffset
            ? PercussionChannel
            : MelodicChannel;

    private void EnqueueMidi(int channel, int command, int data1, int data2)
    {
        _commands.Enqueue(new SynthCommand(
            SynthCommandType.MidiMessage,
            channel,
            command,
            Math.Clamp(data1, 0, 127),
            Math.Clamp(data2, 0, 127)));
    }

    private void ApplyPendingCommands()
    {
        while (_commands.TryDequeue(out var command))
        {
            switch (command.Type)
            {
                case SynthCommandType.MidiMessage:
                    _synthesizer.ProcessMidiMessage(
                        command.Channel,
                        command.Command,
                        command.Data1,
                        command.Data2);
                    break;

                case SynthCommandType.AllNotesOff:
                    _synthesizer.NoteOffAll(immediate: true);
                    break;

                case SynthCommandType.MasterVolume:
                    _synthesizer.MasterVolume = command.Data1 / 100f;
                    break;

                case SynthCommandType.Reverb:
                    _reverbProcessor.SetMixPercent(command.Data1);
                    break;

                case SynthCommandType.Preset:
                    _synthesizer.NoteOffAll(immediate: true);
                    ApplyPreset(command.Data1, command.Data2);
                    break;

                default:
                    throw new InvalidOperationException($"Unknown synth command: {command.Type}");
            }
        }
    }

    private void ApplyFullChannelVolume()
    {
        for (var channel = 0; channel < 16; channel++)
        {
            _synthesizer.ProcessMidiMessage(channel, 0xB0, 0x07, 127);
            _synthesizer.ProcessMidiMessage(channel, 0xB0, 0x0B, 127);
        }
    }

    private void ApplyPreset(int bankNumber, int patchNumber)
    {
        var channel = bankNumber >= PercussionBankOffset
            ? PercussionChannel
            : MelodicChannel;
        var bankSelectValue = bankNumber >= PercussionBankOffset
            ? bankNumber - PercussionBankOffset
            : bankNumber;

        _synthesizer.ProcessMidiMessage(channel, 0xB0, BankSelectController, bankSelectValue);
        _synthesizer.ProcessMidiMessage(channel, 0xC0, patchNumber, 0);
    }

    private static void ValidatePreset(int bankNumber, int patchNumber)
    {
        if (bankNumber is < 0 or > 128)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bankNumber),
                "SoundFont 2 presets use melodic banks 0 through 127 and percussion bank 128.");
        }

        if (patchNumber is < 0 or > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(patchNumber));
        }
    }

    private enum SynthCommandType
    {
        MidiMessage,
        AllNotesOff,
        MasterVolume,
        Reverb,
        Preset
    }

    private readonly record struct SynthCommand(
        SynthCommandType Type,
        int Channel,
        int Command,
        int Data1,
        int Data2);
}
