using PianoPracticeTool.Core;

namespace PianoPracticeTool.Core.Editing;

public sealed class EditableNote
{
    public int Id { get; set; }
    public int MidiNote { get; set; }
    public long StartTick { get; set; }
    public long DurationTick { get; set; }
    public int Staff { get; set; }
    public string Voice { get; set; } = "1";
    public Hand Hand { get; set; }
    public int Finger { get; set; }

    public long EndTick => StartTick + DurationTick;

    public EditableNote Clone()
        => new()
        {
            Id = Id,
            MidiNote = MidiNote,
            StartTick = StartTick,
            DurationTick = DurationTick,
            Staff = Staff,
            Voice = Voice,
            Hand = Hand,
            Finger = Finger
        };
}

public sealed record EditableRest(
    long StartTick,
    long DurationTick,
    int Staff,
    string Voice = "1")
{
    public long EndTick => StartTick + DurationTick;
}

public sealed record EditableMeasure(
    int Number,
    long StartTick,
    long DurationTick,
    int Beats,
    int BeatType)
{
    public long EndTick => StartTick + DurationTick;
}

public sealed record EditableTempoEvent(
    long Tick,
    double BeatsPerMinute);

public sealed record EditableKeySignatureEvent(
    long Tick,
    int Fifths,
    string? Mode = null,
    string? ScaleType = null);

public sealed record EditableHarmonyEvent(
    long Tick,
    string Symbol);

public sealed class EditableMusicScore
{
    public const int TicksPerQuarter = 960;
    public const long MinimumDurationTicks = 60;

    public string Title { get; set; } = string.Empty;
    public string Composer { get; set; } = string.Empty;
    public List<EditableNote> Notes { get; } = new();
    public List<EditableRest> Rests { get; } = new();
    public List<EditableMeasure> Measures { get; } = new();
    public List<EditableTempoEvent> TempoEvents { get; } = new();
    public List<EditableKeySignatureEvent> KeySignatureEvents { get; } = new();
    public List<EditableHarmonyEvent> HarmonyEvents { get; } = new();
    public List<ReferenceAudioSyncPoint> ReferenceAudioSyncPoints { get; } = new();

    public long LengthTicks
    {
        get
        {
            if (Measures.Count > 0)
            {
                return Measures.Max(measure => measure.EndTick);
            }

            var noteEnd = Notes.Count == 0 ? 0L : Notes.Max(note => note.EndTick);
            var restEnd = Rests.Count == 0 ? 0L : Rests.Max(rest => rest.EndTick);
            return Math.Max(noteEnd, restEnd);
        }
    }

    public static EditableMusicScore FromMusicScore(MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);

        var editable = new EditableMusicScore
        {
            Title = score.Title,
            Composer = score.Composer
        };

        editable.Notes.AddRange(score.Notes.Select(note => new EditableNote
        {
            Id = note.Id,
            MidiNote = note.MidiNote,
            StartTick = BeatToTick(note.StartBeat),
            DurationTick = Math.Max(MinimumDurationTicks, BeatToTick(note.DurationBeat)),
            Staff = note.Staff,
            Voice = note.Voice,
            Hand = note.Hand,
            Finger = note.Finger
        }));

        editable.Rests.AddRange(score.Rests.Select(rest => new EditableRest(
            BeatToTick(rest.StartBeat),
            Math.Max(1L, BeatToTick(rest.DurationBeat)),
            rest.Staff,
            NormalizeVoice(rest.Voice))));

        editable.Measures.AddRange(score.Measures.Select(measure => new EditableMeasure(
            measure.Number,
            BeatToTick(measure.StartBeat),
            Math.Max(1L, BeatToTick(measure.DurationBeat)),
            measure.Beats,
            measure.BeatType)));

        if (editable.Measures.Count == 0)
        {
            var contentLengthTicks = Math.Max(
                editable.Notes.Count == 0 ? 0L : editable.Notes.Max(note => note.EndTick),
                editable.Rests.Count == 0 ? 0L : editable.Rests.Max(rest => rest.EndTick));
            var lengthTicks = Math.Max(TicksPerQuarter * 4L, contentLengthTicks);
            var measureDuration = TicksPerQuarter * 4L;
            var measureNumber = 1;
            for (var startTick = 0L; startTick < lengthTicks; startTick += measureDuration)
            {
                editable.Measures.Add(new EditableMeasure(
                    measureNumber++,
                    startTick,
                    Math.Min(measureDuration, Math.Max(1L, lengthTicks - startTick)),
                    4,
                    4));
            }
        }

        editable.TempoEvents.AddRange(score.TempoEvents.Select(item => new EditableTempoEvent(
            BeatToTick(item.Beat),
            item.BeatsPerMinute)));

        if (editable.TempoEvents.Count == 0)
        {
            editable.TempoEvents.Add(new EditableTempoEvent(0L, score.TempoBpm));
        }

        editable.KeySignatureEvents.AddRange(score.KeySignatureEvents.Select(item => new EditableKeySignatureEvent(
            BeatToTick(item.Beat),
            item.Fifths,
            item.Mode,
            item.ScaleType)));
        editable.HarmonyEvents.AddRange(score.HarmonyEvents.Select(item => new EditableHarmonyEvent(
            BeatToTick(item.Beat),
            item.Symbol)));

        return editable;
    }

    public EditableMusicScore Clone()
    {
        var clone = new EditableMusicScore
        {
            Title = Title,
            Composer = Composer
        };
        clone.Notes.AddRange(Notes.Select(note => note.Clone()));
        clone.Rests.AddRange(Rests);
        clone.Measures.AddRange(Measures);
        clone.TempoEvents.AddRange(TempoEvents);
        clone.KeySignatureEvents.AddRange(KeySignatureEvents);
        clone.HarmonyEvents.AddRange(HarmonyEvents);
        clone.ReferenceAudioSyncPoints.AddRange(ReferenceAudioSyncPoints);
        return clone;
    }

    public MusicScore ToMusicScore()
    {
        var orderedNotes = Notes
            .OrderBy(note => note.StartTick)
            .ThenBy(note => note.MidiNote)
            .ThenBy(note => note.Id)
            .Select(note => new ScoreNote
            {
                Id = note.Id,
                MidiNote = note.MidiNote,
                StartBeat = TickToBeat(note.StartTick),
                DurationBeat = TickToBeat(note.DurationTick),
                Staff = note.Staff,
                Voice = NormalizeVoice(note.Voice),
                Hand = note.Hand,
                Finger = note.Finger
            })
            .ToArray();

        var rests = Rests
            .OrderBy(rest => rest.StartTick)
            .ThenBy(rest => rest.Staff)
            .ThenBy(rest => NormalizeVoice(rest.Voice), StringComparer.Ordinal)
            .ThenBy(rest => rest.DurationTick)
            .Select(rest => new ScoreRest(
                TickToBeat(rest.StartTick),
                TickToBeat(rest.DurationTick),
                rest.Staff,
                NormalizeVoice(rest.Voice)))
            .ToArray();

        var measures = Measures
            .OrderBy(measure => measure.StartTick)
            .Select(measure => new ScoreMeasure(
                measure.Number,
                TickToBeat(measure.StartTick),
                TickToBeat(measure.DurationTick),
                measure.Beats,
                measure.BeatType))
            .ToArray();

        var tempoEvents = TempoEvents
            .OrderBy(item => item.Tick)
            .Select(item => new ScoreTempoEvent(
                TickToBeat(item.Tick),
                item.BeatsPerMinute))
            .ToArray();

        var keySignatureEvents = KeySignatureEvents
            .OrderBy(item => item.Tick)
            .Select(item => new ScoreKeySignatureEvent(
                TickToBeat(item.Tick),
                item.Fifths,
                item.Mode,
                item.ScaleType))
            .ToArray();
        var harmonyEvents = HarmonyEvents
            .OrderBy(item => item.Tick)
            .Select(item => new ScoreHarmonyEvent(
                TickToBeat(item.Tick),
                item.Symbol))
            .ToArray();

        var initialTempo = tempoEvents
            .LastOrDefault(item => item.Beat <= ScoreTiming.EventBeatTolerance)
            ?.BeatsPerMinute
            ?? tempoEvents.FirstOrDefault()?.BeatsPerMinute
            ?? 120d;

        return new MusicScore
        {
            Title = string.IsNullOrWhiteSpace(Title) ? "Untitled" : Title.Trim(),
            Composer = Composer.Trim(),
            TempoBpm = initialTempo,
            Notes = orderedNotes,
            Rests = rests,
            TempoEvents = tempoEvents,
            KeySignatureEvents = keySignatureEvents,
            HarmonyEvents = harmonyEvents,
            Measures = measures
        };
    }

    public static long BeatToTick(double beat)
        => checked((long)Math.Round(
            beat * TicksPerQuarter,
            MidpointRounding.AwayFromZero));

    public static double TickToBeat(long tick)
        => tick / (double)TicksPerQuarter;

    private static string NormalizeVoice(string? voice)
        => string.IsNullOrWhiteSpace(voice) ? "1" : voice.Trim();
}
