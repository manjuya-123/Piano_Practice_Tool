using PianoPracticeTool.Core;

namespace PianoPracticeTool.Services.Practice;

public sealed record PracticeSetupOptions(
    PracticeMode Mode,
    PracticeHandMode HandMode,
    PracticeLoopRange? LoopRange,
    double SpeedMultiplier,
    int AppliedDeviceOctaveShiftSemitones = 0,
    bool FollowRecommendedDeviceOctaveShift = false);
