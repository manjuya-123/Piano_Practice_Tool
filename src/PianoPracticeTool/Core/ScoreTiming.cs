namespace PianoPracticeTool.Core;

public static class ScoreTiming
{
    public const double BeatGroupingTolerance = 0.01d;
    public const double EventBeatTolerance = 0.0001d;
    public const double PlayAlongToleranceBeat = 0.30d;

    // The realtime scheduler is allowed to predict/advance only a short distance past its
    // latest successful pulse. If a thread or GC pause lasts longer than this, the transport
    // intentionally pauses instead of replaying all missed musical events in a burst.
    public const double MaximumRealtimePulseSeconds = 0.025d;

    public const double PerfectAttackSeconds = 0.060d;
    public const double GreatAttackSeconds = 0.120d;
    public const double GoodAttackSeconds = 0.220d;

    public const double PerfectReleaseSeconds = 0.120d;
    public const double GreatReleaseSeconds = 0.250d;
    public const double GoodReleaseSeconds = 0.450d;
    public const double MinimumReleaseEvaluationDurationSeconds = 0.200d;
}