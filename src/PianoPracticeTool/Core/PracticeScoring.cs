namespace PianoPracticeTool.Core;

public sealed record PracticeScore(
    int EarnedPoints,
    int MaximumPoints,
    int WrongKeyCount,
    int MissedTargetNoteCount,
    int PerfectTargetNoteCount,
    int GreatTargetNoteCount,
    int GoodTargetNoteCount)
{
    public double AchievementRatio => MaximumPoints == 0
        ? 1d
        : (double)EarnedPoints / MaximumPoints;
}

public static class PracticeScoring
{
    public const int PerfectPointsPerNote = 100;
    public const int GreatPointsPerNote = 80;
    public const int GoodPointsPerNote = 50;
    public const int WaitWrongKeyPenalty = 100;
    public const int PerformanceWrongKeyPenalty = 50;

    public static double ToHundredPointScore(PracticeScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        return Math.Clamp(score.AchievementRatio * 100d, 0d, 100d);
    }

    public static PracticeScore CreateWaitScore(int totalTargetNotes, int wrongKeyCount)
        => CreateWaitProgressScore(totalTargetNotes, totalTargetNotes, wrongKeyCount);

    public static PracticeScore CreateWaitProgressScore(
        int totalTargetNotes,
        int matchedTargetNoteCount,
        int wrongKeyCount)
    {
        ValidateNonNegative(totalTargetNotes, nameof(totalTargetNotes));
        ValidateNonNegative(matchedTargetNoteCount, nameof(matchedTargetNoteCount));
        ValidateNonNegative(wrongKeyCount, nameof(wrongKeyCount));
        if (matchedTargetNoteCount > totalTargetNotes)
        {
            throw new ArgumentException(
                "Matched target note count cannot exceed the total target note count.",
                nameof(matchedTargetNoteCount));
        }

        var maximumPoints = totalTargetNotes * PerfectPointsPerNote;
        var earnedPoints = Math.Clamp(
            matchedTargetNoteCount * PerfectPointsPerNote - wrongKeyCount * WaitWrongKeyPenalty,
            0,
            maximumPoints);

        return new PracticeScore(
            earnedPoints,
            maximumPoints,
            wrongKeyCount,
            MissedTargetNoteCount: 0,
            PerfectTargetNoteCount: 0,
            GreatTargetNoteCount: 0,
            GoodTargetNoteCount: 0);
    }

    public static PracticeScore CreatePerformanceScore(
        int totalTargetNotes,
        int perfectTargetNoteCount,
        int greatTargetNoteCount,
        int goodTargetNoteCount,
        int missedTargetNoteCount,
        int wrongKeyCount)
    {
        ValidateNonNegative(totalTargetNotes, nameof(totalTargetNotes));
        ValidateNonNegative(perfectTargetNoteCount, nameof(perfectTargetNoteCount));
        ValidateNonNegative(greatTargetNoteCount, nameof(greatTargetNoteCount));
        ValidateNonNegative(goodTargetNoteCount, nameof(goodTargetNoteCount));
        ValidateNonNegative(missedTargetNoteCount, nameof(missedTargetNoteCount));
        ValidateNonNegative(wrongKeyCount, nameof(wrongKeyCount));

        var judgedTargetNotes = perfectTargetNoteCount
            + greatTargetNoteCount
            + goodTargetNoteCount
            + missedTargetNoteCount;
        if (judgedTargetNotes > totalTargetNotes)
        {
            throw new ArgumentException(
                "Judged target note count cannot exceed the total target note count.",
                nameof(totalTargetNotes));
        }

        var maximumPoints = totalTargetNotes * PerfectPointsPerNote;
        var timingPoints = perfectTargetNoteCount * PerfectPointsPerNote
            + greatTargetNoteCount * GreatPointsPerNote
            + goodTargetNoteCount * GoodPointsPerNote;
        var earnedPoints = Math.Clamp(
            timingPoints - wrongKeyCount * PerformanceWrongKeyPenalty,
            0,
            maximumPoints);

        return new PracticeScore(
            earnedPoints,
            maximumPoints,
            wrongKeyCount,
            missedTargetNoteCount,
            perfectTargetNoteCount,
            greatTargetNoteCount,
            goodTargetNoteCount);
    }

    private static void ValidateNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
