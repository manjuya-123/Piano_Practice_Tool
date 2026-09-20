namespace PianoPracticeTool.Core;

public sealed record WaitPracticeTempoSummary(
    double AverageBpm,
    double OriginalTempoPercent,
    int CompletedIntervalCount,
    double ObservedBeatSpan,
    double ActiveElapsedSeconds);

public sealed class WaitPracticeTempoTracker
{
    private readonly ScoreTimeline _timeline;

    private double? _firstCompletedBeat;
    private double? _firstCompletionActiveSeconds;
    private double? _lastCompletedBeat;
    private double? _lastCompletionActiveSeconds;
    private int _completionCount;

    public WaitPracticeTempoTracker(MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        _timeline = new ScoreTimeline(score);
    }

    public int CompletionCount => _completionCount;

    public void RecordGroupCompletion(double beat, double activeElapsedSeconds)
    {
        if (double.IsNaN(beat) || double.IsInfinity(beat))
        {
            throw new ArgumentOutOfRangeException(nameof(beat));
        }

        if (activeElapsedSeconds < 0d
            || double.IsNaN(activeElapsedSeconds)
            || double.IsInfinity(activeElapsedSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(activeElapsedSeconds));
        }

        if (_lastCompletedBeat is double lastBeat
            && beat < lastBeat - ScoreTiming.EventBeatTolerance)
        {
            throw new ArgumentOutOfRangeException(nameof(beat), "Completed beats must be monotonic.");
        }

        if (_lastCompletionActiveSeconds is double lastSeconds
            && activeElapsedSeconds < lastSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activeElapsedSeconds),
                "Elapsed practice time must be monotonic.");
        }

        if (_firstCompletedBeat is null)
        {
            _firstCompletedBeat = beat;
            _firstCompletionActiveSeconds = activeElapsedSeconds;
        }

        _lastCompletedBeat = beat;
        _lastCompletionActiveSeconds = activeElapsedSeconds;
        _completionCount++;
    }

    public WaitPracticeTempoSummary? CreateSummary()
    {
        if (_completionCount < 2
            || _firstCompletedBeat is not double firstBeat
            || _lastCompletedBeat is not double lastBeat
            || _firstCompletionActiveSeconds is not double firstSeconds
            || _lastCompletionActiveSeconds is not double lastSeconds)
        {
            return null;
        }

        var beatSpan = lastBeat - firstBeat;
        var activeElapsedSeconds = lastSeconds - firstSeconds;
        if (beatSpan <= ScoreTiming.EventBeatTolerance || activeElapsedSeconds <= 0d)
        {
            return null;
        }

        var averageBpm = beatSpan * 60d / activeElapsedSeconds;
        var originalScoreSeconds = _timeline.BeatToSeconds(lastBeat)
            - _timeline.BeatToSeconds(firstBeat);
        var originalTempoPercent = originalScoreSeconds <= 0d
            ? 0d
            : originalScoreSeconds / activeElapsedSeconds * 100d;

        return new WaitPracticeTempoSummary(
            averageBpm,
            originalTempoPercent,
            _completionCount - 1,
            beatSpan,
            activeElapsedSeconds);
    }

    public void Reset()
    {
        _firstCompletedBeat = null;
        _firstCompletionActiveSeconds = null;
        _lastCompletedBeat = null;
        _lastCompletionActiveSeconds = null;
        _completionCount = 0;
    }
}
