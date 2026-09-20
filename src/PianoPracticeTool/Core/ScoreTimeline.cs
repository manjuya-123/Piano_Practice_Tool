namespace PianoPracticeTool.Core;

public sealed class ScoreTimeline
{
    private readonly IReadOnlyList<TempoSegment> _segments;
    private readonly double _initialTempoBpm;

    public ScoreTimeline(MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);

        _initialTempoBpm = score.TempoBpm > 0d ? score.TempoBpm : 120d;
        _segments = BuildSegments(score, _initialTempoBpm);
        LengthBeats = Math.Max(0d, score.LengthBeats);
        TotalSeconds = BeatToSeconds(LengthBeats);
    }

    public double LengthBeats { get; }

    public double TotalSeconds { get; }

    public double BeatToSeconds(double beat)
    {
        if (beat <= 0d)
        {
            return beat * 60d / _initialTempoBpm;
        }

        var segment = FindSegmentForBeat(beat);
        return segment.StartSeconds + (beat - segment.StartBeat) * 60d / segment.BeatsPerMinute;
    }

    public double SecondsToBeat(double seconds)
    {
        if (seconds <= 0d)
        {
            return seconds * _initialTempoBpm / 60d;
        }

        foreach (var segment in _segments)
        {
            if (double.IsPositiveInfinity(segment.EndBeat))
            {
                return segment.StartBeat + (seconds - segment.StartSeconds) * segment.BeatsPerMinute / 60d;
            }

            var endSeconds = segment.StartSeconds
                + (segment.EndBeat - segment.StartBeat) * 60d / segment.BeatsPerMinute;
            if (seconds <= endSeconds + ScoreTiming.EventBeatTolerance)
            {
                return segment.StartBeat + (seconds - segment.StartSeconds) * segment.BeatsPerMinute / 60d;
            }
        }

        var last = _segments[^1];
        return last.StartBeat + (seconds - last.StartSeconds) * last.BeatsPerMinute / 60d;
    }

    public double AdvanceBeat(double currentBeat, double elapsedRealSeconds, double speedMultiplier)
    {
        if (elapsedRealSeconds <= 0d || speedMultiplier <= 0d)
        {
            return currentBeat;
        }

        var targetSeconds = BeatToSeconds(currentBeat) + elapsedRealSeconds * speedMultiplier;
        return SecondsToBeat(targetSeconds);
    }

    public TimeSpan BeatToTimeSpan(double beat)
        => TimeSpan.FromSeconds(Math.Max(0d, BeatToSeconds(Math.Max(0d, beat))));

    private TempoSegment FindSegmentForBeat(double beat)
    {
        for (var index = _segments.Count - 1; index >= 0; index--)
        {
            if (beat >= _segments[index].StartBeat - ScoreTiming.EventBeatTolerance)
            {
                return _segments[index];
            }
        }

        return _segments[0];
    }

    private static IReadOnlyList<TempoSegment> BuildSegments(MusicScore score, double initialTempoBpm)
    {
        var tempoPoints = new List<ScoreTempoEvent>
        {
            new(0d, initialTempoBpm)
        };

        foreach (var tempoEvent in score.TempoEvents.OrderBy(item => item.Beat))
        {
            if (tempoEvent.Beat < 0d || tempoEvent.BeatsPerMinute <= 0d)
            {
                continue;
            }

            var existingIndex = tempoPoints.FindIndex(item =>
                Math.Abs(item.Beat - tempoEvent.Beat) <= ScoreTiming.EventBeatTolerance);
            if (existingIndex >= 0)
            {
                tempoPoints[existingIndex] = tempoEvent;
            }
            else
            {
                tempoPoints.Add(tempoEvent);
            }
        }

        tempoPoints = tempoPoints.OrderBy(item => item.Beat).ToList();

        var segments = new List<TempoSegment>(tempoPoints.Count);
        var elapsedSeconds = 0d;
        for (var index = 0; index < tempoPoints.Count; index++)
        {
            var point = tempoPoints[index];
            var endBeat = index + 1 < tempoPoints.Count
                ? tempoPoints[index + 1].Beat
                : double.PositiveInfinity;

            segments.Add(new TempoSegment(
                point.Beat,
                endBeat,
                point.BeatsPerMinute,
                elapsedSeconds));

            if (!double.IsPositiveInfinity(endBeat))
            {
                elapsedSeconds += (endBeat - point.Beat) * 60d / point.BeatsPerMinute;
            }
        }

        return segments;
    }

    private sealed record TempoSegment(
        double StartBeat,
        double EndBeat,
        double BeatsPerMinute,
        double StartSeconds);
}
