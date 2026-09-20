using PianoPracticeTool.Services.Midi;
using PianoPracticeTool.Services.Songs;

namespace PianoPracticeTool.Services.Settings;

public enum EditorTimelineDragMode
{
    TouchScroll = 0,
    DirectFollow = 1
}

public sealed class AppSettings
{
    public List<string> SongFolders { get; set; } = new();

    public string? PreferredMidiDeviceName { get; set; }

    public string? PreferredMidiOutputDeviceName { get; set; } =
        MidiOutputDevice.BuiltInSoundFont.Name;

    public bool AutoConnectMidi { get; set; } = true;

    public string BuiltInSoundFontPath { get; set; } = string.Empty;

    public int BuiltInSoundFontBankNumber { get; set; }

    public int BuiltInSoundFontPatchNumber { get; set; }

    public int BuiltInSoundFontVolumePercent { get; set; } = 100;

    public int BuiltInSoundFontReverbPercent { get; set; }

    public int AutomaticPlaybackVelocity { get; set; } = 100;

    public int PianoRollVisibleRangePercent { get; set; } = 100;

    public int MetronomeVolumePercent { get; set; } = 100;

    public bool ShowHandColors { get; set; } = true;

    public bool ShowFingering { get; set; } = true;

    public bool ShowExpectedKeyboardGuide { get; set; } = true;

    public EditorTimelineDragMode EditorTimelineDragMode { get; set; } =
        EditorTimelineDragMode.TouchScroll;

    public bool EditorPanPitchEnabled { get; set; } = true;

    public int JudgementTimingPercent { get; set; } = 100;

    public int KeyboardLowestMidi { get; set; } = 21;

    public int KeyboardHighestMidi { get; set; } = 108;

    public bool HasOctaveShift { get; set; }

    public Dictionary<string, SongDifficulty> SongDifficultyOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public AppSettings Clone()
    {
        return new AppSettings
        {
            SongFolders = SongFolders.ToList(),
            PreferredMidiDeviceName = PreferredMidiDeviceName,
            PreferredMidiOutputDeviceName = PreferredMidiOutputDeviceName,
            AutoConnectMidi = AutoConnectMidi,
            BuiltInSoundFontPath = BuiltInSoundFontPath,
            BuiltInSoundFontBankNumber = BuiltInSoundFontBankNumber,
            BuiltInSoundFontPatchNumber = BuiltInSoundFontPatchNumber,
            BuiltInSoundFontVolumePercent = BuiltInSoundFontVolumePercent,
            BuiltInSoundFontReverbPercent = BuiltInSoundFontReverbPercent,
            AutomaticPlaybackVelocity = AutomaticPlaybackVelocity,
            PianoRollVisibleRangePercent = PianoRollVisibleRangePercent,
            MetronomeVolumePercent = MetronomeVolumePercent,
            ShowHandColors = ShowHandColors,
            ShowFingering = ShowFingering,
            ShowExpectedKeyboardGuide = ShowExpectedKeyboardGuide,
            EditorTimelineDragMode = EditorTimelineDragMode,
            EditorPanPitchEnabled = EditorPanPitchEnabled,
            JudgementTimingPercent = JudgementTimingPercent,
            KeyboardLowestMidi = KeyboardLowestMidi,
            KeyboardHighestMidi = KeyboardHighestMidi,
            HasOctaveShift = HasOctaveShift,
            SongDifficultyOverrides = new Dictionary<string, SongDifficulty>(
                SongDifficultyOverrides,
                StringComparer.OrdinalIgnoreCase)
        };
    }
}
