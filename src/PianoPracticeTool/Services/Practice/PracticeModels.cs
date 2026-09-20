using PianoPracticeTool.Core;

namespace PianoPracticeTool.Services.Practice;

public enum PracticeMode
{
    WaitForCorrectNotes,
    PlayAlong,
    OriginalTempo,
    Listen
}

public enum PracticeRunState
{
    Ready,
    CountingIn,
    Playing,
    WaitingForInput,
    Paused,
    Completed
}

public sealed record PracticeLoopRange(double StartBeat, double EndBeat);

public readonly record struct PracticeTransportPosition(
    PracticeRunState State,
    double Beat);

public sealed record PracticeResult(
    DateTimeOffset CompletedAtUtc,
    string ScoreTitle,
    PracticeMode Mode,
    PracticeHandMode HandMode,
    double StartBeat,
    double EndBeat,
    double ActiveDurationSeconds,
    double SpeedMultiplier,
    int TargetNoteCount,
    PracticeScore Score,
    WaitPracticeTempoSummary? WaitTempo,
    int ReleaseMissCount,
    int MaxCombo,
    int AutoPlayedTargetNoteCount = 0);

public sealed record PracticeSnapshot(
    PracticeRunState State,
    PracticeMode Mode,
    PracticeHandMode HandMode,
    double CurrentBeat,
    double LengthBeats,
    double CurrentSeconds,
    double TotalSeconds,
    double SpeedMultiplier,
    double CurrentTempoBpm,
    bool MetronomeEnabled,
    int MetronomeVolumePercent,
    IReadOnlyList<int> ExpectedMidiNotes,
    int CorrectCount,
    int MissCount,
    int TotalTargetNotes,
    int PerfectCount,
    int GreatCount,
    int GoodCount,
    int CurrentCombo,
    int MaxCombo,
    PerformanceJudgement? LastJudgement,
    bool LastJudgementWasRelease,
    double ProgressRatio,
    double AccuracyRatio,
    int CurrentMeasureNumber,
    string StatusMessage,
    PracticeLoopRange? LoopRange,
    int InputTransposeSemitones,
    PracticeResult? LastResult);
