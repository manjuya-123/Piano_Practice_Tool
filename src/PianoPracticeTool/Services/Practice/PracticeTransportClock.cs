using System.Diagnostics;
using System.Threading;
using PianoPracticeTool.Core;

namespace PianoPracticeTool.Services.Practice;

internal sealed class PracticeTransportClock
{
    private PracticeTransportAnchor _anchor = new(
        PracticeRunState.Ready,
        0d,
        1d,
        0d,
        Stopwatch.GetTimestamp(),
        null);

    public PracticeTransportPosition GetPosition()
    {
        var anchor = Volatile.Read(ref _anchor);
        return new PracticeTransportPosition(
            anchor.State,
            GetBeat(anchor, Stopwatch.GetTimestamp()));
    }

    public void SetPosition(
        PracticeRunState state,
        double beat,
        double speedMultiplier,
        ScoreTimeline? timeline,
        double countInBeatsPerSecond = 0d)
    {
        Volatile.Write(
            ref _anchor,
            new PracticeTransportAnchor(
                state,
                beat,
                speedMultiplier,
                countInBeatsPerSecond,
                Stopwatch.GetTimestamp(),
                timeline));
    }

    private static double GetBeat(PracticeTransportAnchor anchor, long timestamp)
    {
        if (anchor.State == PracticeRunState.CountingIn)
        {
            return anchor.Beat
                + GetElapsedSeconds(anchor.Timestamp, timestamp) * anchor.CountInBeatsPerSecond;
        }

        if (anchor.State == PracticeRunState.Playing
            && anchor.Timeline is not null
            && anchor.SpeedMultiplier > 0d)
        {
            var elapsedSeconds = GetElapsedSeconds(anchor.Timestamp, timestamp);
            var scoreSeconds = anchor.Timeline.BeatToSeconds(anchor.Beat)
                + elapsedSeconds * anchor.SpeedMultiplier;
            return anchor.Timeline.SecondsToBeat(scoreSeconds);
        }

        return anchor.Beat;
    }

    private static double GetElapsedSeconds(long fromTimestamp, long toTimestamp)
        => Math.Max(0d, toTimestamp - fromTimestamp) / (double)Stopwatch.Frequency;

    private sealed record PracticeTransportAnchor(
        PracticeRunState State,
        double Beat,
        double SpeedMultiplier,
        double CountInBeatsPerSecond,
        long Timestamp,
        ScoreTimeline? Timeline);
}
