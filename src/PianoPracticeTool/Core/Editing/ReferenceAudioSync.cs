namespace PianoPracticeTool.Core.Editing;

public enum EditorReferencePlaybackMode
{
    ScoreOnly = 0,
    ReferenceOnly = 1,
    Both = 2
}

public sealed record ReferenceAudioSyncPoint(
    double ScoreBeat,
    double AudioSeconds);

public sealed record ReferenceAudioGap(
    double ScoreBeat,
    double StartAudioSeconds,
    double EndAudioSeconds)
{
    public double DurationSeconds =>
        Math.Max(0d, EndAudioSeconds - StartAudioSeconds);
}

public sealed class ReferenceAudioProject
{
    public bool Enabled { get; set; }

    public string AudioPath { get; set; } = string.Empty;

    public EditorReferencePlaybackMode PlaybackMode { get; set; } =
        EditorReferencePlaybackMode.Both;

    public int ReferenceVolumePercent { get; set; } = 80;

    public int ScoreVolumePercent { get; set; } = 100;

    public int ReferencePanPercent { get; set; }

    public int ScorePanPercent { get; set; }

    public List<ReferenceAudioSyncPoint> SyncPoints { get; set; } = new();

    public ReferenceAudioProject Clone()
        => new()
        {
            Enabled = Enabled,
            AudioPath = AudioPath,
            PlaybackMode = PlaybackMode,
            ReferenceVolumePercent = ReferenceVolumePercent,
            ScoreVolumePercent = ScoreVolumePercent,
            ReferencePanPercent = ReferencePanPercent,
            ScorePanPercent = ScorePanPercent,
            SyncPoints = SyncPoints.ToList()
        };
}

public sealed class ReferenceAudioSynchronizer
{
    private const double MinimumAudioAnchorSpacingSeconds = 0.001d;

    private readonly ScoreTimeline _scoreTimeline;
    private readonly IReadOnlyList<ReferenceAudioSyncPoint> _points;
    private readonly IReadOnlyList<ReferenceAudioGap> _gaps;

    public ReferenceAudioSynchronizer(
        MusicScore score,
        IEnumerable<ReferenceAudioSyncPoint> syncPoints)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(syncPoints);

        _scoreTimeline = new ScoreTimeline(score);
        _points = Normalize(syncPoints);
        _gaps = CreateGaps(_points);
    }

    public IReadOnlyList<ReferenceAudioSyncPoint> SyncPoints => _points;

    public IReadOnlyList<ReferenceAudioGap> AudioOnlyGaps => _gaps;

    public static IReadOnlyList<ReferenceAudioSyncPoint> EnsureOuterAnchors(
        IEnumerable<ReferenceAudioSyncPoint> syncPoints,
        double scoreLengthBeat,
        double audioDurationSeconds)
    {
        ArgumentNullException.ThrowIfNull(syncPoints);
        if (double.IsNaN(scoreLengthBeat)
            || double.IsInfinity(scoreLengthBeat)
            || scoreLengthBeat < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scoreLengthBeat));
        }

        if (double.IsNaN(audioDurationSeconds)
            || double.IsInfinity(audioDurationSeconds)
            || audioDurationSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(audioDurationSeconds));
        }

        var points =
            Normalize(syncPoints)
                .ToList();
        AddBoundaryPoint(
            points,
            0d,
            0d);
        AddBoundaryPoint(
            points,
            scoreLengthBeat,
            audioDurationSeconds);
        return NormalizeDistinct(
            points);
    }

    public static IReadOnlyList<ReferenceAudioSyncPoint> AdjustAudioSegment(
        MusicScore score,
        IEnumerable<ReferenceAudioSyncPoint> syncPoints,
        double audioDurationSeconds,
        double targetScoreBeat,
        double deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(syncPoints);
        if (double.IsNaN(audioDurationSeconds)
            || double.IsInfinity(audioDurationSeconds)
            || audioDurationSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(audioDurationSeconds));
        }

        if (double.IsNaN(targetScoreBeat)
            || double.IsInfinity(targetScoreBeat))
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetScoreBeat));
        }

        if (double.IsNaN(deltaSeconds)
            || double.IsInfinity(deltaSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(deltaSeconds));
        }

        var scoreLength =
            Math.Max(
                0d,
                score.LengthBeats);
        var points =
            EnsureOuterAnchors(
                    syncPoints,
                    scoreLength,
                    audioDurationSeconds)
                .ToList();
        if (scoreLength
                <= ScoreTiming.EventBeatTolerance
            || Math.Abs(deltaSeconds)
                <= ScoreTiming.EventBeatTolerance)
        {
            return points;
        }

        var boundaries =
            BuildBoundaryBeats(
                points,
                scoreLength);
        if (boundaries.Count < 2)
        {
            return points;
        }

        var boundedTargetBeat =
            Math.Clamp(
                targetScoreBeat,
                0d,
                scoreLength);
        var segmentIndex =
            FindSegmentIndex(
                boundaries,
                boundedTargetBeat,
                deltaSeconds);
        var startBeat =
            boundaries[segmentIndex];
        var endBeat =
            boundaries[segmentIndex + 1];

        var startAnchors =
            GetBoundaryAudioSeconds(
                points,
                startBeat);
        var endAnchors =
            GetBoundaryAudioSeconds(
                points,
                endBeat);
        if (startAnchors.Count == 0
            || endAnchors.Count == 0)
        {
            return points;
        }

        var previousSegmentEnd =
            startAnchors[0];
        var segmentStart =
            startAnchors[^1];
        var segmentEnd =
            endAnchors[0];
        var nextSegmentStart =
            endAnchors[^1];

        if (segmentEnd
            <= segmentStart
            + MinimumAudioAnchorSpacingSeconds)
        {
            return points;
        }

        var adjustedStart =
            segmentStart;
        var adjustedEnd =
            segmentEnd;
        var isFirstSegment =
            startBeat
            <= ScoreTiming.EventBeatTolerance;
        var isLastSegment =
            Math.Abs(
                endBeat
                - scoreLength)
            <= ScoreTiming.EventBeatTolerance;

        if (isFirstSegment
            && !isLastSegment)
        {
            adjustedStart =
                Math.Clamp(
                    segmentStart
                    + deltaSeconds,
                    previousSegmentEnd,
                    segmentEnd
                    - MinimumAudioAnchorSpacingSeconds);
        }
        else
        {
            AdjustInteriorSegment(
                previousSegmentEnd,
                segmentStart,
                segmentEnd,
                nextSegmentStart,
                deltaSeconds,
                out adjustedStart,
                out adjustedEnd);
        }

        if (Math.Abs(
                adjustedStart
                - segmentStart)
                <= ScoreTiming.EventBeatTolerance
            && Math.Abs(
                adjustedEnd
                - segmentEnd)
                <= ScoreTiming.EventBeatTolerance)
        {
            return points;
        }

        ReplaceBoundaryPair(
            points,
            startBeat,
            previousSegmentEnd,
            adjustedStart);
        ReplaceBoundaryPair(
            points,
            endBeat,
            adjustedEnd,
            nextSegmentStart);
        return NormalizeDistinct(
            points);
    }

    private static void AdjustInteriorSegment(
        double previousSegmentEnd,
        double segmentStart,
        double segmentEnd,
        double nextSegmentStart,
        double deltaSeconds,
        out double adjustedStart,
        out double adjustedEnd)
    {
        adjustedStart =
            segmentStart;
        adjustedEnd =
            segmentEnd;

        if (deltaSeconds > 0d)
        {
            var remaining =
                deltaSeconds;
            var rightGap =
                Math.Max(
                    0d,
                    nextSegmentStart
                    - adjustedEnd);
            var closeRightGap =
                Math.Min(
                    remaining,
                    rightGap);
            adjustedEnd +=
                closeRightGap;
            remaining -=
                closeRightGap;

            if (remaining
                > ScoreTiming.EventBeatTolerance)
            {
                adjustedStart =
                    Math.Min(
                        adjustedStart
                        + remaining,
                        adjustedEnd
                        - MinimumAudioAnchorSpacingSeconds);
            }

            return;
        }

        var remainingBackward =
            -deltaSeconds;
        var leftGap =
            Math.Max(
                0d,
                adjustedStart
                - previousSegmentEnd);
        var closeLeftGap =
            Math.Min(
                remainingBackward,
                leftGap);
        adjustedStart -=
            closeLeftGap;
        remainingBackward -=
            closeLeftGap;

        if (remainingBackward
            > ScoreTiming.EventBeatTolerance)
        {
            adjustedEnd =
                Math.Max(
                    adjustedEnd
                    - remainingBackward,
                    adjustedStart
                    + MinimumAudioAnchorSpacingSeconds);
        }
    }

    private static IReadOnlyList<double> BuildBoundaryBeats(
        IReadOnlyList<ReferenceAudioSyncPoint> points,
        double scoreLength)
    {
        var candidates =
            points
                .Select(point =>
                    Math.Clamp(
                        point.ScoreBeat,
                        0d,
                        scoreLength))
                .Append(0d)
                .Append(scoreLength)
                .OrderBy(beat =>
                    beat)
                .ToArray();
        var result =
            new List<double>(
                candidates.Length);
        foreach (var beat in candidates)
        {
            if (result.Count == 0
                || Math.Abs(
                    result[^1]
                    - beat)
                    > ScoreTiming.EventBeatTolerance)
            {
                result.Add(beat);
            }
        }

        return result;
    }

    private static int FindSegmentIndex(
        IReadOnlyList<double> boundaries,
        double targetBeat,
        double deltaSeconds)
    {
        for (var index = 0;
             index < boundaries.Count;
             index++)
        {
            if (Math.Abs(
                    boundaries[index]
                    - targetBeat)
                > ScoreTiming.EventBeatTolerance)
            {
                continue;
            }

            if (index == 0)
            {
                return 0;
            }

            if (index == boundaries.Count - 1)
            {
                return boundaries.Count - 2;
            }

            return deltaSeconds >= 0d
                ? index
                : index - 1;
        }

        for (var index = 0;
             index < boundaries.Count - 1;
             index++)
        {
            if (targetBeat
                    > boundaries[index]
                    + ScoreTiming.EventBeatTolerance
                && targetBeat
                    < boundaries[index + 1]
                    - ScoreTiming.EventBeatTolerance)
            {
                return index;
            }
        }

        return targetBeat
            <= boundaries[0]
            ? 0
            : boundaries.Count - 2;
    }

    private static IReadOnlyList<double> GetBoundaryAudioSeconds(
        IReadOnlyList<ReferenceAudioSyncPoint> points,
        double scoreBeat)
        => points
            .Where(point =>
                Math.Abs(
                    point.ScoreBeat
                    - scoreBeat)
                <= ScoreTiming.EventBeatTolerance)
            .OrderBy(point =>
                point.AudioSeconds)
            .Select(point =>
                point.AudioSeconds)
            .ToArray();

    private static void ReplaceBoundaryPair(
        List<ReferenceAudioSyncPoint> points,
        double scoreBeat,
        double earlierAudioSeconds,
        double laterAudioSeconds)
    {
        points.RemoveAll(point =>
            Math.Abs(
                point.ScoreBeat
                - scoreBeat)
            <= ScoreTiming.EventBeatTolerance);
        points.Add(
            new ReferenceAudioSyncPoint(
                scoreBeat,
                earlierAudioSeconds));
        if (laterAudioSeconds
            > earlierAudioSeconds
            + ScoreTiming.EventBeatTolerance)
        {
            points.Add(
                new ReferenceAudioSyncPoint(
                    scoreBeat,
                    laterAudioSeconds));
        }
    }

    private static void AddBoundaryPoint(
        List<ReferenceAudioSyncPoint> points,
        double scoreBeat,
        double audioSeconds)
    {
        if (points.Any(point =>
                Math.Abs(
                    point.ScoreBeat
                    - scoreBeat)
                    <= ScoreTiming.EventBeatTolerance
                && Math.Abs(
                    point.AudioSeconds
                    - audioSeconds)
                    <= ScoreTiming.EventBeatTolerance))
        {
            return;
        }

        points.Add(
            new ReferenceAudioSyncPoint(
                scoreBeat,
                audioSeconds));
    }

    private static IReadOnlyList<ReferenceAudioSyncPoint> NormalizeDistinct(
        IEnumerable<ReferenceAudioSyncPoint> syncPoints)
    {
        var normalized =
            Normalize(syncPoints);
        var result =
            new List<ReferenceAudioSyncPoint>(
                normalized.Count);
        foreach (var point in normalized)
        {
            if (result.Count > 0)
            {
                var previous =
                    result[^1];
                if (Math.Abs(
                        previous.ScoreBeat
                        - point.ScoreBeat)
                        <= ScoreTiming.EventBeatTolerance
                    && Math.Abs(
                        previous.AudioSeconds
                        - point.AudioSeconds)
                        <= ScoreTiming.EventBeatTolerance)
                {
                    continue;
                }
            }

            result.Add(point);
        }

        return result;
    }

    public double ScoreBeatToAudioSeconds(double scoreBeat)
    {
        var beat = Math.Max(0d, scoreBeat);
        if (_points.Count == 0)
        {
            return Math.Max(0d, _scoreTimeline.BeatToSeconds(beat));
        }

        // At a duplicate score beat, the later audio anchor is the canonical
        // start position. This skips an audio-only gap when playback starts
        // exactly at the score boundary while still preserving the gap when
        // playback approaches the boundary from an earlier beat.
        var exact = _points
            .Where(point =>
                Math.Abs(point.ScoreBeat - beat)
                <= ScoreTiming.EventBeatTolerance)
            .OrderByDescending(point => point.AudioSeconds)
            .FirstOrDefault();
        if (exact is not null)
        {
            return Math.Max(0d, exact.AudioSeconds);
        }

        var before = _points
            .Where(point =>
                point.ScoreBeat
                < beat - ScoreTiming.EventBeatTolerance)
            .OrderByDescending(point => point.ScoreBeat)
            .ThenByDescending(point => point.AudioSeconds)
            .FirstOrDefault();
        var after = _points
            .Where(point =>
                point.ScoreBeat
                > beat + ScoreTiming.EventBeatTolerance)
            .OrderBy(point => point.ScoreBeat)
            .ThenBy(point => point.AudioSeconds)
            .FirstOrDefault();

        if (before is null)
        {
            var first = _points[0];
            return Math.Max(
                0d,
                first.AudioSeconds
                + ScoreSecondsBetween(
                    first.ScoreBeat,
                    beat));
        }

        if (after is null)
        {
            var last = _points[^1];
            return Math.Max(
                0d,
                last.AudioSeconds
                + ScoreSecondsBetween(
                    last.ScoreBeat,
                    beat));
        }

        return InterpolateBeatToAudio(
            beat,
            before,
            after);
    }

    public double ScoreBeatToAudioSecondsForRangeEnd(
        double scoreBeat)
    {
        var beat = Math.Max(
            0d,
            scoreBeat);
        var exact = _points
            .Where(point =>
                Math.Abs(
                    point.ScoreBeat
                    - beat)
                <= ScoreTiming.EventBeatTolerance)
            .OrderBy(point =>
                point.AudioSeconds)
            .FirstOrDefault();
        return exact is not null
            ? Math.Max(
                0d,
                exact.AudioSeconds)
            : ScoreBeatToAudioSeconds(
                beat);
    }

    public double AudioSecondsToScoreBeat(double audioSeconds)
    {
        var seconds = Math.Max(0d, audioSeconds);
        if (_points.Count == 0)
        {
            return Math.Max(
                0d,
                _scoreTimeline.SecondsToBeat(seconds));
        }

        for (var index = 0;
             index < _points.Count - 1;
             index++)
        {
            var left = _points[index];
            var right = _points[index + 1];
            if (seconds
                    < left.AudioSeconds
                    - ScoreTiming.EventBeatTolerance
                || seconds
                    > right.AudioSeconds
                    + ScoreTiming.EventBeatTolerance)
            {
                continue;
            }

            if (Math.Abs(
                    right.ScoreBeat
                    - left.ScoreBeat)
                <= ScoreTiming.EventBeatTolerance)
            {
                return left.ScoreBeat;
            }

            var audioDuration =
                right.AudioSeconds
                - left.AudioSeconds;
            if (audioDuration
                <= ScoreTiming.EventBeatTolerance)
            {
                return right.ScoreBeat;
            }

            var ratio = Math.Clamp(
                (seconds - left.AudioSeconds)
                / audioDuration,
                0d,
                1d);
            var leftScoreSeconds =
                _scoreTimeline.BeatToSeconds(
                    left.ScoreBeat);
            var rightScoreSeconds =
                _scoreTimeline.BeatToSeconds(
                    right.ScoreBeat);
            var targetScoreSeconds =
                leftScoreSeconds
                + (rightScoreSeconds - leftScoreSeconds)
                * ratio;
            return _scoreTimeline.SecondsToBeat(
                targetScoreSeconds);
        }

        if (seconds < _points[0].AudioSeconds)
        {
            var first = _points[0];
            var targetScoreSeconds =
                _scoreTimeline.BeatToSeconds(
                    first.ScoreBeat)
                + seconds
                - first.AudioSeconds;
            return Math.Max(
                0d,
                _scoreTimeline.SecondsToBeat(
                    Math.Max(0d, targetScoreSeconds)));
        }

        var last = _points[^1];
        return Math.Max(
            0d,
            _scoreTimeline.SecondsToBeat(
                _scoreTimeline.BeatToSeconds(
                    last.ScoreBeat)
                + seconds
                - last.AudioSeconds));
    }

    public bool IsAudioOnlyGap(double audioSeconds)
        => _gaps.Any(gap =>
            audioSeconds
                >= gap.StartAudioSeconds
                - ScoreTiming.EventBeatTolerance
            && audioSeconds
                < gap.EndAudioSeconds
                - ScoreTiming.EventBeatTolerance);

    public double SkipAudioOnlyGaps(double audioSeconds)
    {
        var seconds =
            Math.Max(
                0d,
                audioSeconds);
        foreach (var gap in _gaps)
        {
            if (seconds
                    < gap.StartAudioSeconds
                    - ScoreTiming.EventBeatTolerance
                || seconds
                    >= gap.EndAudioSeconds
                    - ScoreTiming.EventBeatTolerance)
            {
                continue;
            }

            seconds =
                gap.EndAudioSeconds;
        }

        return seconds;
    }

    public ReferenceAudioSyncPoint? FindNearestPoint(
        double scoreBeat,
        double maxBeatDistance = 0.25d,
        double? audioSeconds = null)
    {
        if (_points.Count == 0)
        {
            return null;
        }

        var preferredAudioSeconds =
            audioSeconds
            ?? ScoreBeatToAudioSeconds(
                scoreBeat);
        var nearest = _points
            .OrderBy(point =>
                Math.Abs(
                    point.ScoreBeat
                    - scoreBeat))
            .ThenBy(point =>
                Math.Abs(
                    point.AudioSeconds
                    - preferredAudioSeconds))
            .First();
        return Math.Abs(
                nearest.ScoreBeat
                - scoreBeat)
            <= maxBeatDistance
                ? nearest
                : null;
    }

    public static IReadOnlyList<ReferenceAudioSyncPoint> Normalize(
        IEnumerable<ReferenceAudioSyncPoint> syncPoints)
    {
        ArgumentNullException.ThrowIfNull(syncPoints);
        var ordered = syncPoints
            .Where(point =>
                !double.IsNaN(point.ScoreBeat)
                && !double.IsInfinity(point.ScoreBeat)
                && !double.IsNaN(point.AudioSeconds)
                && !double.IsInfinity(point.AudioSeconds)
                && point.ScoreBeat >= 0d
                && point.AudioSeconds >= 0d)
            .OrderBy(point => point.ScoreBeat)
            .ThenBy(point => point.AudioSeconds)
            .ToArray();

        for (var index = 1;
             index < ordered.Length;
             index++)
        {
            if (ordered[index].AudioSeconds
                < ordered[index - 1].AudioSeconds
                - ScoreTiming.EventBeatTolerance)
            {
                throw new InvalidOperationException(
                    "同期ポイントの原音源時刻は、譜面位置が進むにつれて後ろへ進む必要があります。");
            }
        }

        return ordered;
    }

    private double InterpolateBeatToAudio(
        double beat,
        ReferenceAudioSyncPoint left,
        ReferenceAudioSyncPoint right)
    {
        var leftScoreSeconds =
            _scoreTimeline.BeatToSeconds(
                left.ScoreBeat);
        var rightScoreSeconds =
            _scoreTimeline.BeatToSeconds(
                right.ScoreBeat);
        var targetScoreSeconds =
            _scoreTimeline.BeatToSeconds(beat);
        var duration =
            rightScoreSeconds
            - leftScoreSeconds;
        if (duration
            <= ScoreTiming.EventBeatTolerance)
        {
            return right.AudioSeconds;
        }

        var ratio = Math.Clamp(
            (targetScoreSeconds - leftScoreSeconds)
            / duration,
            0d,
            1d);
        return left.AudioSeconds
            + (right.AudioSeconds
                - left.AudioSeconds)
            * ratio;
    }

    private double ScoreSecondsBetween(
        double fromBeat,
        double toBeat)
        => _scoreTimeline.BeatToSeconds(toBeat)
           - _scoreTimeline.BeatToSeconds(fromBeat);

    private static IReadOnlyList<ReferenceAudioGap> CreateGaps(
        IReadOnlyList<ReferenceAudioSyncPoint> points)
    {
        var result =
            new List<ReferenceAudioGap>();
        for (var index = 0;
             index < points.Count - 1;
             index++)
        {
            var left = points[index];
            var right = points[index + 1];
            if (Math.Abs(
                    right.ScoreBeat
                    - left.ScoreBeat)
                <= ScoreTiming.EventBeatTolerance
                && right.AudioSeconds
                    > left.AudioSeconds
                    + ScoreTiming.EventBeatTolerance)
            {
                result.Add(
                    new ReferenceAudioGap(
                        left.ScoreBeat,
                        left.AudioSeconds,
                        right.AudioSeconds));
            }
        }

        return result;
    }
}
