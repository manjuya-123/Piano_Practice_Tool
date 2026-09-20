namespace PianoPracticeTool.Core;

public sealed record PerformanceTimingProfile(
    double PerfectAttackSeconds,
    double GreatAttackSeconds,
    double GoodAttackSeconds,
    double PerfectReleaseSeconds,
    double GreatReleaseSeconds,
    double GoodReleaseSeconds,
    double RepeatedKeyWindowSeconds,
    double RepeatedKeyEarlyReleaseGraceSeconds)
{
    public static PerformanceTimingProfile Default { get; } = new(
        ScoreTiming.PerfectAttackSeconds,
        ScoreTiming.GreatAttackSeconds,
        ScoreTiming.GoodAttackSeconds,
        ScoreTiming.PerfectReleaseSeconds,
        ScoreTiming.GreatReleaseSeconds,
        ScoreTiming.GoodReleaseSeconds,
        RepeatedKeyWindowSeconds: 0.50d,
        RepeatedKeyEarlyReleaseGraceSeconds: 0.10d);

    public static PerformanceTimingProfile FromScale(double scale)
    {
        var normalizedScale = Math.Clamp(scale, 0.50d, 1.50d);
        return Default with
        {
            PerfectAttackSeconds = Default.PerfectAttackSeconds * normalizedScale,
            GreatAttackSeconds = Default.GreatAttackSeconds * normalizedScale,
            GoodAttackSeconds = Default.GoodAttackSeconds * normalizedScale,
            PerfectReleaseSeconds = Default.PerfectReleaseSeconds * normalizedScale,
            GreatReleaseSeconds = Default.GreatReleaseSeconds * normalizedScale,
            GoodReleaseSeconds = Default.GoodReleaseSeconds * normalizedScale,
            RepeatedKeyEarlyReleaseGraceSeconds =
                Default.RepeatedKeyEarlyReleaseGraceSeconds * normalizedScale
        };
    }
}
