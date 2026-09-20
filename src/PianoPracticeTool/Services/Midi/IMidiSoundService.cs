namespace PianoPracticeTool.Services.Midi;

public interface IMidiSoundService : IDisposable
{
    bool IsAvailable { get; }

    string? DeviceName { get; }

    IReadOnlyList<MidiOutputDevice> GetDevices();

    void Initialize();

    void Connect(MidiOutputDevice device);

    void Disconnect();

    void ConfigureBuiltInSoundFont(SoundFontConfiguration configuration);

    void NoteOn(int midiNote, int velocity, int channel = 1);

    void NoteOff(int midiNote, int velocity = 0, int channel = 1);

    void ControlChange(int controller, int value, int channel = 1);

    void ProgramChange(int program, int channel = 1);

    void PlayClick(bool accent, int volumePercent);

    void StopClicks();

    void AllNotesOff();
}
