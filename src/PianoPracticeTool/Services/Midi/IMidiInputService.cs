namespace PianoPracticeTool.Services.Midi;

public sealed record MidiInputDevice(int DeviceNumber, string Name);

public sealed class MidiNoteEventArgs : EventArgs
{
    public MidiNoteEventArgs(int midiNote, int velocity, int channel = 1)
    {
        MidiNote = midiNote;
        Velocity = velocity;
        Channel = channel;
    }

    public int MidiNote { get; }

    public int Velocity { get; }

    public int Channel { get; }
}

public sealed class MidiControlChangeEventArgs : EventArgs
{
    public MidiControlChangeEventArgs(int controller, int value, int channel)
    {
        Controller = controller;
        Value = value;
        Channel = channel;
    }

    public int Controller { get; }

    public int Value { get; }

    public int Channel { get; }
}

public sealed class MidiInputErrorEventArgs : EventArgs
{
    public MidiInputErrorEventArgs(string message, int rawMessage)
    {
        Message = message;
        RawMessage = rawMessage;
    }

    public string Message { get; }

    public int RawMessage { get; }
}

public interface IMidiInputService : IDisposable
{
    event EventHandler<MidiNoteEventArgs>? NoteOn;

    event EventHandler<MidiNoteEventArgs>? NoteOff;

    event EventHandler<MidiControlChangeEventArgs>? ControlChange;

    event EventHandler<MidiInputErrorEventArgs>? InputError;

    bool IsConnected { get; }

    MidiInputDevice? ConnectedDevice { get; }

    IReadOnlyList<MidiInputDevice> GetDevices();

    void Connect(MidiInputDevice device);

    void Disconnect();
}
