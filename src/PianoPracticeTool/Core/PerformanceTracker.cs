namespace PianoPracticeTool.Core;

public enum PerformanceJudgement
{
    Perfect,
    Great,
    Good,
    Miss
}

public sealed record PerformanceHitResult(
    bool IsHit,
    PerformanceJudgement Judgement,
    int PlayedMidiNote,
    int? MatchedNoteId,
    double TimingErrorSeconds);

public sealed record PerformanceReleaseResult(
    bool IsEvaluated,
    PerformanceJudgement? Judgement,
    int PlayedMidiNote,
    int? MatchedNoteId,
    double TimingErrorSeconds);

public sealed class PerformanceTracker
{
    private readonly IReadOnlyList<ScoreNote> _targetNotes;
    private readonly ScoreTimeline _timeline;
    private readonly PerformanceTimingProfile _timingProfile;
    private readonly HashSet<int> _hitNoteIds = new();
    private readonly HashSet<int> _missedNoteIds = new();
    private readonly Dictionary<int, ActivePress> _activePresses = new();

    private int _nextOverdueIndex;
    private int _perfectCount;
    private int _greatCount;
    private int _goodCount;
    private int _missCount;
    private int _wrongKeyCount;
    private int _missedTargetNoteCount;
    private int _releaseMissCount;
    private int _perfectTargetNoteCount;
    private int _greatTargetNoteCount;
    private int _goodTargetNoteCount;
    private int _currentCombo;
    private int _maxCombo;

    public PerformanceTracker(
        MusicScore score,
        PracticeHandMode handMode,
        double startBeat = 0d,
        double? endBeat = null,
        PerformanceTimingProfile? timingProfile = null,
        PracticeMidiRange? playableMidiRange = null)
    {
        ArgumentNullException.ThrowIfNull(score);

        _timeline = new ScoreTimeline(score);
        _timingProfile = timingProfile ?? PerformanceTimingProfile.Default;
        var rangeEnd = endBeat ?? score.LengthBeats + ScoreTiming.EventBeatTolerance;
        var midiRange = playableMidiRange ?? PracticeMidiRange.Full;
        _targetNotes = score.Notes
            .Where(note => note.StartBeat >= startBeat - ScoreTiming.EventBeatTolerance)
            .Where(note => note.StartBeat < rangeEnd - ScoreTiming.EventBeatTolerance)
            .Where(note => IsPracticedHand(note.Hand, handMode))
            .Where(note => midiRange.Contains(note.MidiNote))
            .OrderBy(note => note.StartBeat)
            .ThenBy(note => note.MidiNote)
            .ToArray();
    }

    public int HitCount => _hitNoteIds.Count;
    public int MissCount => _missCount;
    public int WrongKeyCount => _wrongKeyCount;
    public int MissedTargetNoteCount => _missedTargetNoteCount;
    public int ReleaseMissCount => _releaseMissCount;
    public int PerfectCount => _perfectCount;
    public int GreatCount => _greatCount;
    public int GoodCount => _goodCount;
    public int PerfectTargetNoteCount => _perfectTargetNoteCount;
    public int GreatTargetNoteCount => _greatTargetNoteCount;
    public int GoodTargetNoteCount => _goodTargetNoteCount;
    public int CurrentCombo => _currentCombo;
    public int MaxCombo => _maxCombo;
    public PerformanceJudgement? LastJudgement { get; private set; }
    public bool LastJudgementWasRelease { get; private set; }
    public int TotalCount => _targetNotes.Count;
    public int JudgedTargetCount => _hitNoteIds.Count + _missedNoteIds.Count;

    public double AccuracyRatio
    {
        get
        {
            var judgedEvents = _perfectCount + _greatCount + _goodCount + _missCount;
            if (judgedEvents == 0)
            {
                return 1d;
            }

            var weightedScore = _perfectCount + _greatCount * 0.8d + _goodCount * 0.5d;
            return weightedScore / judgedEvents;
        }
    }

    public double CompletionRatio => TotalCount == 0 ? 1d : (double)JudgedTargetCount / TotalCount;

    public PerformanceHitResult ProcessNoteOn(int midiNote, double currentBeat, double speedMultiplier = 1d)
    {
        ValidateMidiNote(midiNote);
        var speed = NormalizeSpeed(speedMultiplier);
        var currentSeconds = _timeline.BeatToSeconds(currentBeat);
        var candidate = _targetNotes
            .Where(note => note.MidiNote == midiNote)
            .Where(note => !_hitNoteIds.Contains(note.Id) && !_missedNoteIds.Contains(note.Id))
            .Select(note =>
            {
                var errorSeconds = (currentSeconds - _timeline.BeatToSeconds(note.StartBeat)) / speed;
                return new
                {
                    Note = note,
                    ErrorSeconds = errorSeconds,
                    AbsoluteErrorSeconds = Math.Abs(errorSeconds)
                };
            })
            .Where(item => item.AbsoluteErrorSeconds <= _timingProfile.GoodAttackSeconds)
            .OrderBy(item => item.AbsoluteErrorSeconds)
            .ThenBy(item => item.Note.StartBeat)
            .FirstOrDefault();

        if (candidate is null)
        {
            RegisterMiss(PerformanceMissKind.WrongKey);
            return new PerformanceHitResult(false, PerformanceJudgement.Miss, midiNote, null, double.NaN);
        }

        var samePitchAtOnset = _targetNotes
            .Where(note => note.MidiNote == midiNote)
            .Where(note => !_hitNoteIds.Contains(note.Id) && !_missedNoteIds.Contains(note.Id))
            .Where(note => Math.Abs(note.StartBeat - candidate.Note.StartBeat) <= ScoreTiming.BeatGroupingTolerance)
            .ToArray();

        foreach (var note in samePitchAtOnset)
        {
            _hitNoteIds.Add(note.Id);
        }

        var releaseCandidates = samePitchAtOnset.Where(ShouldEvaluateRelease).ToArray();
        if (releaseCandidates.Length > 0)
        {
            var expectedEndSeconds = releaseCandidates.Max(note => _timeline.BeatToSeconds(note.EndBeat));
            _activePresses[midiNote] = new ActivePress(
                releaseCandidates.Select(note => note.Id).ToArray(),
                expectedEndSeconds,
                GetRepeatedKeyEarlyReleaseGraceSeconds(
                    midiNote,
                    candidate.Note.StartBeat,
                    expectedEndSeconds,
                    speed));
        }
        else
        {
            _activePresses.Remove(midiNote);
        }

        var judgement = JudgeAttack(candidate.AbsoluteErrorSeconds);
        RegisterSuccess(
            judgement,
            incrementCombo: true,
            isRelease: false,
            targetNoteCount: samePitchAtOnset.Length);
        return new PerformanceHitResult(true, judgement, midiNote, candidate.Note.Id, candidate.ErrorSeconds);
    }

    public PerformanceReleaseResult ProcessNoteOff(int midiNote, double currentBeat, double speedMultiplier = 1d)
    {
        ValidateMidiNote(midiNote);
        if (!_activePresses.Remove(midiNote, out var activePress))
        {
            return new PerformanceReleaseResult(false, null, midiNote, null, double.NaN);
        }

        var speed = NormalizeSpeed(speedMultiplier);
        var currentSeconds = _timeline.BeatToSeconds(currentBeat);
        var errorSeconds = (currentSeconds - activePress.ExpectedEndSeconds) / speed;
        var judgement = JudgeRelease(errorSeconds, activePress.EarlyReleaseGraceSeconds);

        if (judgement == PerformanceJudgement.Miss)
        {
            RegisterMiss(PerformanceMissKind.Release);
        }
        else
        {
            RegisterSuccess(judgement, incrementCombo: false, isRelease: true, targetNoteCount: 0);
        }

        var matchedNoteId = activePress.NoteIds.Count > 0
            ? activePress.NoteIds[0]
            : (int?)null;
        return new PerformanceReleaseResult(
            true,
            judgement,
            midiNote,
            matchedNoteId,
            errorSeconds);
    }

    public void AdvanceTo(double currentBeat, double speedMultiplier = 1d)
    {
        var speed = NormalizeSpeed(speedMultiplier);
        var currentSeconds = _timeline.BeatToSeconds(currentBeat);

        while (_nextOverdueIndex < _targetNotes.Count)
        {
            var note = _targetNotes[_nextOverdueIndex];
            if (_hitNoteIds.Contains(note.Id) || _missedNoteIds.Contains(note.Id))
            {
                _nextOverdueIndex++;
                continue;
            }

            var lateBySeconds = (currentSeconds - _timeline.BeatToSeconds(note.StartBeat)) / speed;
            if (lateBySeconds <= _timingProfile.GoodAttackSeconds)
            {
                break;
            }

            _missedNoteIds.Add(note.Id);
            RegisterMiss(PerformanceMissKind.MissedTarget);
            _nextOverdueIndex++;
        }

        List<int>? lateReleaseMidiNotes = null;
        foreach (var pair in _activePresses)
        {
            if ((currentSeconds - pair.Value.ExpectedEndSeconds) / speed <= _timingProfile.GoodReleaseSeconds)
            {
                continue;
            }

            lateReleaseMidiNotes ??= new List<int>();
            lateReleaseMidiNotes.Add(pair.Key);
        }

        if (lateReleaseMidiNotes is null)
        {
            return;
        }

        foreach (var midiNote in lateReleaseMidiNotes)
        {
            _activePresses.Remove(midiNote);
            RegisterMiss(PerformanceMissKind.Release);
        }
    }

    public IReadOnlyList<int> GetNextExpectedMidiNotes(double currentBeat)
    {
        var next = _targetNotes
            .Where(note => !_hitNoteIds.Contains(note.Id) && !_missedNoteIds.Contains(note.Id))
            .FirstOrDefault(note => note.StartBeat >= currentBeat - ScoreTiming.PlayAlongToleranceBeat);
        if (next is null)
        {
            return Array.Empty<int>();
        }

        return _targetNotes
            .Where(note => !_hitNoteIds.Contains(note.Id) && !_missedNoteIds.Contains(note.Id))
            .Where(note => Math.Abs(note.StartBeat - next.StartBeat) <= ScoreTiming.BeatGroupingTolerance)
            .Select(note => note.MidiNote)
            .Distinct()
            .OrderBy(midiNote => midiNote)
            .ToArray();
    }

    private bool ShouldEvaluateRelease(ScoreNote note)
    {
        var durationSeconds = _timeline.BeatToSeconds(note.EndBeat) - _timeline.BeatToSeconds(note.StartBeat);
        return durationSeconds >= ScoreTiming.MinimumReleaseEvaluationDurationSeconds;
    }

    private double GetRepeatedKeyEarlyReleaseGraceSeconds(
        int midiNote,
        double currentStartBeat,
        double currentExpectedEndSeconds,
        double speedMultiplier)
    {
        var nextSamePitch = _targetNotes
            .Where(note => note.MidiNote == midiNote)
            .Where(note => note.StartBeat > currentStartBeat + ScoreTiming.BeatGroupingTolerance)
            .OrderBy(note => note.StartBeat)
            .FirstOrDefault();
        if (nextSamePitch is null)
        {
            return 0d;
        }

        var currentStartSeconds = _timeline.BeatToSeconds(currentStartBeat);
        var nextStartSeconds = _timeline.BeatToSeconds(nextSamePitch.StartBeat);
        var realIntervalSeconds = (nextStartSeconds - currentStartSeconds) / speedMultiplier;
        if (realIntervalSeconds > _timingProfile.RepeatedKeyWindowSeconds)
        {
            return 0d;
        }

        var naturalRetriggerGapSeconds = Math.Max(
            0d,
            (nextStartSeconds - currentExpectedEndSeconds) / speedMultiplier);
        return Math.Clamp(
            _timingProfile.RepeatedKeyEarlyReleaseGraceSeconds - naturalRetriggerGapSeconds,
            0d,
            _timingProfile.RepeatedKeyEarlyReleaseGraceSeconds);
    }

    private void RegisterSuccess(
        PerformanceJudgement judgement,
        bool incrementCombo,
        bool isRelease,
        int targetNoteCount)
    {
        switch (judgement)
        {
            case PerformanceJudgement.Perfect:
                _perfectCount++;
                if (!isRelease)
                {
                    _perfectTargetNoteCount += targetNoteCount;
                }

                break;
            case PerformanceJudgement.Great:
                _greatCount++;
                if (!isRelease)
                {
                    _greatTargetNoteCount += targetNoteCount;
                }

                break;
            case PerformanceJudgement.Good:
                _goodCount++;
                if (!isRelease)
                {
                    _goodTargetNoteCount += targetNoteCount;
                }

                break;
            case PerformanceJudgement.Miss:
                throw new ArgumentOutOfRangeException(nameof(judgement));
        }

        if (incrementCombo)
        {
            _currentCombo++;
            _maxCombo = Math.Max(_maxCombo, _currentCombo);
        }

        LastJudgement = judgement;
        LastJudgementWasRelease = isRelease;
    }

    private void RegisterMiss(PerformanceMissKind kind)
    {
        _missCount++;
        switch (kind)
        {
            case PerformanceMissKind.WrongKey:
                _wrongKeyCount++;
                break;
            case PerformanceMissKind.MissedTarget:
                _missedTargetNoteCount++;
                break;
            case PerformanceMissKind.Release:
                _releaseMissCount++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        _currentCombo = 0;
        LastJudgement = PerformanceJudgement.Miss;
        LastJudgementWasRelease = kind == PerformanceMissKind.Release;
    }

    private PerformanceJudgement JudgeAttack(double absoluteErrorSeconds)
    {
        if (absoluteErrorSeconds <= _timingProfile.PerfectAttackSeconds)
        {
            return PerformanceJudgement.Perfect;
        }

        if (absoluteErrorSeconds <= _timingProfile.GreatAttackSeconds)
        {
            return PerformanceJudgement.Great;
        }

        return absoluteErrorSeconds <= _timingProfile.GoodAttackSeconds
            ? PerformanceJudgement.Good
            : PerformanceJudgement.Miss;
    }

    private PerformanceJudgement JudgeRelease(double errorSeconds, double earlyReleaseGraceSeconds)
    {
        var absoluteErrorSeconds = errorSeconds < 0d
            ? Math.Max(0d, -errorSeconds - earlyReleaseGraceSeconds)
            : errorSeconds;

        if (absoluteErrorSeconds <= _timingProfile.PerfectReleaseSeconds)
        {
            return PerformanceJudgement.Perfect;
        }

        if (absoluteErrorSeconds <= _timingProfile.GreatReleaseSeconds)
        {
            return PerformanceJudgement.Great;
        }

        return absoluteErrorSeconds <= _timingProfile.GoodReleaseSeconds
            ? PerformanceJudgement.Good
            : PerformanceJudgement.Miss;
    }

    private static double NormalizeSpeed(double speedMultiplier)
        => speedMultiplier > 0d ? speedMultiplier : 1d;

    private static void ValidateMidiNote(int midiNote)
    {
        if (midiNote is < 0 or > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(midiNote), midiNote, "MIDI note must be between 0 and 127.");
        }
    }

    private static bool IsPracticedHand(Hand hand, PracticeHandMode handMode)
        => handMode == PracticeHandMode.Both
            || (handMode == PracticeHandMode.Right && hand == Hand.Right)
            || (handMode == PracticeHandMode.Left && hand == Hand.Left);

    private enum PerformanceMissKind
    {
        WrongKey,
        MissedTarget,
        Release
    }

    private sealed record ActivePress(
        IReadOnlyList<int> NoteIds,
        double ExpectedEndSeconds,
        double EarlyReleaseGraceSeconds);
}
