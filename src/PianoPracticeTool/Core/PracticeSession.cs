namespace PianoPracticeTool.Core;

public enum PracticeHandMode
{
    Both,
    Right,
    Left
}

public sealed class PracticeSession
{
    private readonly IReadOnlyList<ScoreNote> _practiceNotes;
    private readonly HashSet<int> _matchedNoteIds = new();
    private readonly IReadOnlyList<NoteGroup> _noteGroups;
    private int _nextGroupIndex;
    private int _incorrectNoteCount;

    public PracticeSession(
        MusicScore score,
        PracticeHandMode handMode = PracticeHandMode.Both,
        double startBeat = 0d,
        double? endBeat = null,
        PracticeMidiRange? playableMidiRange = null)
    {
        ArgumentNullException.ThrowIfNull(score);

        if (startBeat < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(startBeat));
        }

        var rangeEnd = endBeat ?? score.LengthBeats + ScoreTiming.EventBeatTolerance;
        if (rangeEnd < startBeat)
        {
            throw new ArgumentOutOfRangeException(nameof(endBeat));
        }

        HandMode = handMode;
        _practiceNotes = FilterNotes(
            score.Notes,
            handMode,
            startBeat,
            rangeEnd,
            playableMidiRange ?? PracticeMidiRange.Full);
        _noteGroups = BuildNoteGroups(_practiceNotes);
    }

    public PracticeHandMode HandMode { get; }

    public int MatchedNoteCount => _matchedNoteIds.Count;

    public int IncorrectNoteCount => _incorrectNoteCount;

    public int TotalNoteCount => _practiceNotes.Count;

    public bool IsCompleted => _nextGroupIndex >= _noteGroups.Count;

    public double ProgressRatio => TotalNoteCount == 0 ? 1d : (double)MatchedNoteCount / TotalNoteCount;

    public double? CurrentBeat => IsCompleted ? null : _noteGroups[_nextGroupIndex].StartBeat;

    public IReadOnlyList<int> CurrentExpectedMidiNotes => IsCompleted
        ? Array.Empty<int>()
        : GetRemainingExpectedMidiNotes(_noteGroups[_nextGroupIndex]);

    public PracticeStepResult ProcessNoteOn(int midiNote)
    {
        if (midiNote is < 0 or > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(midiNote), midiNote, "MIDI note must be between 0 and 127.");
        }

        if (IsCompleted)
        {
            return PracticeStepResult.Completed(MatchedNoteCount, TotalNoteCount);
        }

        var group = _noteGroups[_nextGroupIndex];
        var expectedMidiNotes = GetRemainingExpectedMidiNotes(group);
        var matchingNotes = group.Notes
            .Where(note => note.MidiNote == midiNote && !_matchedNoteIds.Contains(note.Id))
            .ToArray();

        if (matchingNotes.Length == 0)
        {
            _incorrectNoteCount++;
            return PracticeStepResult.Incorrect(
                midiNote,
                expectedMidiNotes,
                MatchedNoteCount,
                TotalNoteCount);
        }

        foreach (var note in matchingNotes)
        {
            _matchedNoteIds.Add(note.Id);
        }

        var groupCompleted = group.Notes.All(note => _matchedNoteIds.Contains(note.Id));
        if (groupCompleted)
        {
            _nextGroupIndex++;
        }

        var nextExpectedNotes = IsCompleted
            ? Array.Empty<int>()
            : GetRemainingExpectedMidiNotes(_noteGroups[_nextGroupIndex]);

        return new PracticeStepResult(
            IsCorrect: true,
            GroupCompleted: groupCompleted,
            SessionCompleted: IsCompleted,
            PlayedMidiNote: midiNote,
            ExpectedMidiNotes: groupCompleted ? nextExpectedNotes : GetRemainingExpectedMidiNotes(group),
            MatchedNoteCount: MatchedNoteCount,
            TotalNoteCount: TotalNoteCount);
    }

    public void Reset()
    {
        _matchedNoteIds.Clear();
        _nextGroupIndex = 0;
        _incorrectNoteCount = 0;
    }

    private static IReadOnlyList<ScoreNote> FilterNotes(
        IReadOnlyList<ScoreNote> notes,
        PracticeHandMode handMode,
        double startBeat,
        double endBeat,
        PracticeMidiRange playableMidiRange)
    {
        return notes
            .Where(note => note.StartBeat >= startBeat - ScoreTiming.EventBeatTolerance)
            .Where(note => note.StartBeat < endBeat - ScoreTiming.EventBeatTolerance)
            .Where(note => handMode switch
            {
                PracticeHandMode.Right => note.Hand == Hand.Right,
                PracticeHandMode.Left => note.Hand == Hand.Left,
                _ => true
            })
            .Where(note => playableMidiRange.Contains(note.MidiNote))
            .ToArray();
    }

    private static IReadOnlyList<NoteGroup> BuildNoteGroups(IReadOnlyList<ScoreNote> notes)
    {
        var groups = new List<NoteGroup>();

        foreach (var note in notes.OrderBy(note => note.StartBeat).ThenBy(note => note.MidiNote))
        {
            var existingGroup = groups.LastOrDefault();
            if (existingGroup is null
                || Math.Abs(existingGroup.StartBeat - note.StartBeat) > ScoreTiming.BeatGroupingTolerance)
            {
                groups.Add(new NoteGroup(note.StartBeat, new List<ScoreNote> { note }));
                continue;
            }

            existingGroup.Notes.Add(note);
        }

        return groups;
    }

    private IReadOnlyList<int> GetRemainingExpectedMidiNotes(NoteGroup group)
        => group.Notes
            .Where(note => !_matchedNoteIds.Contains(note.Id))
            .Select(note => note.MidiNote)
            .Distinct()
            .OrderBy(midiNote => midiNote)
            .ToArray();

    private sealed record NoteGroup(double StartBeat, List<ScoreNote> Notes);
}

public sealed record PracticeStepResult(
    bool IsCorrect,
    bool GroupCompleted,
    bool SessionCompleted,
    int? PlayedMidiNote,
    IReadOnlyList<int> ExpectedMidiNotes,
    int MatchedNoteCount,
    int TotalNoteCount)
{
    public static PracticeStepResult Incorrect(
        int playedMidiNote,
        IReadOnlyList<int> expectedMidiNotes,
        int matchedNoteCount,
        int totalNoteCount)
        => new(
            IsCorrect: false,
            GroupCompleted: false,
            SessionCompleted: false,
            PlayedMidiNote: playedMidiNote,
            ExpectedMidiNotes: expectedMidiNotes,
            MatchedNoteCount: matchedNoteCount,
            TotalNoteCount: totalNoteCount);

    public static PracticeStepResult Completed(int matchedNoteCount, int totalNoteCount)
        => new(
            IsCorrect: true,
            GroupCompleted: false,
            SessionCompleted: true,
            PlayedMidiNote: null,
            ExpectedMidiNotes: Array.Empty<int>(),
            MatchedNoteCount: matchedNoteCount,
            TotalNoteCount: totalNoteCount);
}
