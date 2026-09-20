namespace PianoPracticeTool.Core;

public enum Hand
{
    Left,
    Right
}

public sealed class ScoreNote
{
    public required int Id { get; init; }
    public required int MidiNote { get; init; }
    public required double StartBeat { get; init; }
    public required double DurationBeat { get; set; }
    public required int Staff { get; init; }
    public string Voice { get; init; } = "1";
    public required Hand Hand { get; set; }
    public int Finger { get; set; }

    public double EndBeat => StartBeat + DurationBeat;

    public string PitchName => MidiPitch.ToName(MidiNote);
}

public sealed record ScoreRest(
    double StartBeat,
    double DurationBeat,
    int Staff,
    string Voice = "1");

public sealed record ScoreTempoEvent(
    double Beat,
    double BeatsPerMinute);

public sealed record ScoreKeySignatureEvent(
    double Beat,
    int Fifths,
    string? Mode = null,
    string? ScaleType = null);

public sealed record ScoreHarmonyEvent(
    double Beat,
    string Symbol);

public sealed record ScoreMeasure(
    int Number,
    double StartBeat,
    double DurationBeat,
    int Beats,
    int BeatType)
{
    public double EndBeat => StartBeat + DurationBeat;
}

public sealed class MusicScore
{
    public required string Title { get; init; }
    public string Composer { get; init; } = string.Empty;
    public required double TempoBpm { get; init; }
    public required IReadOnlyList<ScoreNote> Notes { get; init; }
    public IReadOnlyList<ScoreRest> Rests { get; init; } = Array.Empty<ScoreRest>();
    public IReadOnlyList<ScoreTempoEvent> TempoEvents { get; init; } = Array.Empty<ScoreTempoEvent>();
    public IReadOnlyList<ScoreKeySignatureEvent> KeySignatureEvents { get; init; } = Array.Empty<ScoreKeySignatureEvent>();
    public IReadOnlyList<ScoreHarmonyEvent> HarmonyEvents { get; init; } = Array.Empty<ScoreHarmonyEvent>();
    public IReadOnlyList<ScoreMeasure> Measures { get; init; } = Array.Empty<ScoreMeasure>();

    public int MinMidiNote => Notes.Count == 0 ? 60 : Notes.Min(note => note.MidiNote);

    public int MaxMidiNote => Notes.Count == 0 ? 60 : Notes.Max(note => note.MidiNote);

    public double LengthBeats
    {
        get
        {
            var noteEnd = Notes.Count == 0
                ? 0d
                : Notes.Max(note => note.EndBeat);
            var restEnd = Rests.Count == 0
                ? 0d
                : Rests.Max(rest => rest.StartBeat + rest.DurationBeat);
            var measureEnd = Measures.Count == 0
                ? 0d
                : Measures.Max(measure => measure.EndBeat);

            return Math.Max(noteEnd, Math.Max(restEnd, measureEnd));
        }
    }

    public double GetTempoAt(double beat)
    {
        var tempoEvent = TempoEvents
            .Where(item => item.Beat <= beat + ScoreTiming.EventBeatTolerance)
            .OrderByDescending(item => item.Beat)
            .FirstOrDefault();

        return tempoEvent?.BeatsPerMinute ?? TempoBpm;
    }

    public int? GetKeyFifthsAt(double beat)
    {
        var keyEvent = KeySignatureEvents
            .Where(item => item.Beat <= beat + ScoreTiming.EventBeatTolerance)
            .OrderByDescending(item => item.Beat)
            .FirstOrDefault();
        return keyEvent?.Fifths;
    }

    public ScoreMeasure? GetMeasureAt(double beat)
    {
        if (Measures.Count == 0)
        {
            return null;
        }

        return Measures.LastOrDefault(measure => measure.StartBeat <= beat + ScoreTiming.EventBeatTolerance)
            ?? Measures[0];
    }
}

public static class MidiPitch
{
    private static readonly string[] Names =
    {
        "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
    };

    public static string ToName(int midiNote)
    {
        var normalizedMidiNote = Math.Clamp(midiNote, 0, 127);
        var pitchClass = normalizedMidiNote % 12;
        var octave = normalizedMidiNote / 12 - 1;
        return $"{Names[pitchClass]}{octave}";
    }
}
