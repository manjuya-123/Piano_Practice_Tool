using NAudio.Midi;

namespace PianoPracticeTool.Services.Midi;

public sealed class NAudioMidiInputService : IMidiInputService
{
    private MidiIn? _midiIn;

    public event EventHandler<MidiNoteEventArgs>? NoteOn;

    public event EventHandler<MidiNoteEventArgs>? NoteOff;

    public event EventHandler<MidiControlChangeEventArgs>? ControlChange;

    public event EventHandler<MidiInputErrorEventArgs>? InputError;

    public bool IsConnected => _midiIn is not null;

    public MidiInputDevice? ConnectedDevice { get; private set; }

    public IReadOnlyList<MidiInputDevice> GetDevices()
    {
        var devices = new List<MidiInputDevice>(MidiIn.NumberOfDevices);
        for (var deviceNumber = 0; deviceNumber < MidiIn.NumberOfDevices; deviceNumber++)
        {
            var capabilities = MidiIn.DeviceInfo(deviceNumber);
            devices.Add(new MidiInputDevice(deviceNumber, capabilities.ProductName));
        }

        return devices;
    }

    public void Connect(MidiInputDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        Disconnect();

        var midiIn = new MidiIn(device.DeviceNumber);
        midiIn.MessageReceived += MidiIn_MessageReceived;
        midiIn.ErrorReceived += MidiIn_ErrorReceived;

        try
        {
            midiIn.Start();
            _midiIn = midiIn;
            ConnectedDevice = device;
        }
        catch
        {
            midiIn.MessageReceived -= MidiIn_MessageReceived;
            midiIn.ErrorReceived -= MidiIn_ErrorReceived;
            midiIn.Dispose();
            throw;
        }
    }

    public void Disconnect()
    {
        var midiIn = _midiIn;
        _midiIn = null;
        ConnectedDevice = null;

        if (midiIn is null)
        {
            return;
        }

        midiIn.MessageReceived -= MidiIn_MessageReceived;
        midiIn.ErrorReceived -= MidiIn_ErrorReceived;

        try
        {
            midiIn.Stop();
        }
        finally
        {
            midiIn.Dispose();
        }
    }

    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }

    private void MidiIn_MessageReceived(object? sender, MidiInMessageEventArgs e)
    {
        if (e.MidiEvent is NoteOnEvent noteOnEvent)
        {
            if (noteOnEvent.Velocity > 0)
            {
                NoteOn?.Invoke(
                    this,
                    new MidiNoteEventArgs(noteOnEvent.NoteNumber, noteOnEvent.Velocity, noteOnEvent.Channel));
            }
            else
            {
                NoteOff?.Invoke(
                    this,
                    new MidiNoteEventArgs(noteOnEvent.NoteNumber, noteOnEvent.Velocity, noteOnEvent.Channel));
            }

            return;
        }

        if (e.MidiEvent is NoteEvent noteEvent && noteEvent.CommandCode == MidiCommandCode.NoteOff)
        {
            NoteOff?.Invoke(
                this,
                new MidiNoteEventArgs(noteEvent.NoteNumber, noteEvent.Velocity, noteEvent.Channel));
            return;
        }

        if (e.MidiEvent is ControlChangeEvent controlChangeEvent)
        {
            ControlChange?.Invoke(
                this,
                new MidiControlChangeEventArgs(
                    (int)controlChangeEvent.Controller,
                    controlChangeEvent.ControllerValue,
                    controlChangeEvent.Channel));
        }
    }

    private void MidiIn_ErrorReceived(object? sender, MidiInMessageEventArgs e)
    {
        InputError?.Invoke(
            this,
            new MidiInputErrorEventArgs("MIDI入力データの受信中にエラーが発生しました。", e.RawMessage));
    }
}
