using PianoPracticeTool.Core;
using PianoPracticeTool.Services.Settings;

namespace PianoPracticeTool.Services.Songs;

public enum KeyboardCompatibilityLevel
{
    Full,
    OneHandOnly,
    Insufficient
}

public sealed record KeyboardPracticeCompatibility(
    bool FitsOriginal,
    bool FitsFullyWithRecommendedShift,
    int RecommendedOctaveShiftSemitones,
    int OriginalPlayableNoteCount,
    int RecommendedPlayableNoteCount,
    int TargetNoteCount,
    IReadOnlyDictionary<int, int> PlayableNoteCountByShift)
{
    public bool HasUsefulRecommendedShift
        => RecommendedOctaveShiftSemitones != 0
            && RecommendedPlayableNoteCount > OriginalPlayableNoteCount;

    public bool RequiresShiftForFullFit
        => !FitsOriginal
            && FitsFullyWithRecommendedShift
            && RecommendedOctaveShiftSemitones != 0;

    public int GetPlayableNoteCount(int octaveShiftSemitones)
        => PlayableNoteCountByShift.TryGetValue(octaveShiftSemitones, out var count)
            ? count
            : OriginalPlayableNoteCount;

    public int GetAutoPlayedNoteCount(int octaveShiftSemitones)
        => Math.Max(0, TargetNoteCount - GetPlayableNoteCount(octaveShiftSemitones));
}

public sealed record KeyboardPracticePlan(
    int OctaveShiftSemitones,
    int PlayableLowestMidi,
    int PlayableHighestMidi,
    int TargetNoteCount,
    int AutoPlayedTargetNoteCount)
{
    public bool FullyPlayable => AutoPlayedTargetNoteCount == 0;

    public bool UsesOctaveShift => OctaveShiftSemitones != 0;
}

public sealed record KeyboardCompatibility(
    KeyboardCompatibilityLevel Level,
    KeyboardPracticeCompatibility BothHands,
    KeyboardPracticeCompatibility RightHand,
    KeyboardPracticeCompatibility LeftHand,
    IReadOnlyList<int> AvailableOctaveShiftsSemitones,
    string Message)
{
    public bool BothHandsFit => BothHands.FitsFullyWithRecommendedShift;

    public bool RightHandFits => RightHand.FitsFullyWithRecommendedShift;

    public bool LeftHandFits => LeftHand.FitsFullyWithRecommendedShift;

    public int? SuggestedOctaveShift
        => BothHands.RecommendedOctaveShiftSemitones == 0
            ? null
            : BothHands.RecommendedOctaveShiftSemitones;

    public bool HasUsefulRecommendedShift
        => BothHands.HasUsefulRecommendedShift
            || RightHand.HasUsefulRecommendedShift
            || LeftHand.HasUsefulRecommendedShift;

    public KeyboardPracticeCompatibility ForHand(PracticeHandMode handMode)
        => handMode switch
        {
            PracticeHandMode.Right => RightHand,
            PracticeHandMode.Left => LeftHand,
            _ => BothHands
        };
}

public enum KeyboardPracticeAvailabilityLevel
{
    BothHands,
    EitherSingleHand,
    RightHand,
    LeftHand,
    Insufficient
}

public sealed record KeyboardPracticeAvailability(
    KeyboardPracticeAvailabilityLevel Level,
    bool RequiresSongCompression,
    string ShortText,
    string Message);

public sealed record KeyboardPracticeAvailabilityOption(
    KeyboardPracticeAvailabilityLevel Level,
    string ShortText,
    string Description);

public static class KeyboardCompatibilityService
{
    public static KeyboardCompatibility Analyze(MusicScore score, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(settings);

        var availableShifts = GetAvailableOctaveShifts(settings);
        var both = AnalyzeRange(score.Notes, settings, availableShifts);
        var right = AnalyzeRange(score.Notes.Where(note => note.Hand == Hand.Right), settings, availableShifts);
        var left = AnalyzeRange(score.Notes.Where(note => note.Hand == Hand.Left), settings, availableShifts);

        if (both.FitsFullyWithRecommendedShift)
        {
            var shiftText = both.RequiresShiftForFullFit
                ? $" MIDIキーボードを {FormatOctaveShift(both.RecommendedOctaveShiftSemitones)} にシフトすると全音域を演奏できます。"
                : string.Empty;
            return new KeyboardCompatibility(
                KeyboardCompatibilityLevel.Full,
                both,
                right,
                left,
                availableShifts,
                $"現在のMIDIキーボードで両手練習できます。{shiftText}".Trim());
        }

        if (right.FitsFullyWithRecommendedShift || left.FitsFullyWithRecommendedShift)
        {
            var hands = right.FitsFullyWithRecommendedShift && left.FitsFullyWithRecommendedShift
                ? "右手・左手それぞれ"
                : right.FitsFullyWithRecommendedShift
                    ? "右手"
                    : "左手";
            return new KeyboardCompatibility(
                KeyboardCompatibilityLevel.OneHandOnly,
                both,
                right,
                left,
                availableShifts,
                $"両手では一部の音が鍵盤範囲外です。{hands}なら全音域を練習できます。範囲外の音は自動演奏できます。");
        }

        return new KeyboardCompatibility(
            KeyboardCompatibilityLevel.Insufficient,
            both,
            right,
            left,
            availableShifts,
            "現在のMIDIキーボードでは片手練習でも一部の音が鍵盤範囲外です。範囲外の音は自動演奏できます。");
    }

    public static KeyboardPracticeAvailabilityOption AnalyzePracticeAvailability(
        MusicScore score,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(settings);

        var compatibility =
            Analyze(
                score,
                settings);
        var level =
            GetBestAvailabilityLevel(
                compatibility);
        return new KeyboardPracticeAvailabilityOption(
            level,
            FormatAvailabilityShortText(
                level),
            DescribeAvailability(
                level,
                compatibility));
    }

    public static KeyboardPracticeAvailability AnalyzeLibraryAvailability(
        MusicScore score,
        AppSettings settings,
        SongOctaveCompressionAnalysis compression)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(compression);

        var originalCompatibility =
            Analyze(
                score,
                settings);
        var originalOption =
            AnalyzePracticeAvailability(
                score,
                settings);
        var originalLevel =
            originalOption.Level;

        KeyboardCompatibility? compressedCompatibility =
            null;
        var compressedLevel =
            KeyboardPracticeAvailabilityLevel.Insufficient;
        if (compression.CanCompress)
        {
            var compressedScore =
                SongOctaveCompressionService.Compress(
                    score,
                    settings.KeyboardLowestMidi,
                    settings.KeyboardHighestMidi);
            compressedCompatibility =
                Analyze(
                    compressedScore,
                    settings);
            compressedLevel =
                GetBestAvailabilityLevel(
                    compressedCompatibility);
        }

        var selectedLevel =
            originalLevel
                != KeyboardPracticeAvailabilityLevel.Insufficient
                ? originalLevel
                : compressedLevel;
        var requiresCompression =
            originalLevel
                == KeyboardPracticeAvailabilityLevel.Insufficient
            && compressedLevel
                != KeyboardPracticeAvailabilityLevel.Insufficient;

        var shortText =
            requiresCompression
                ? FormatAvailabilityShortText(
                    selectedLevel)
                    + "(圧縮)"
                : FormatAvailabilityShortText(
                    selectedLevel);

        var originalDescription =
            DescribeAvailability(
                originalLevel,
                originalCompatibility);
        var compressionDescription =
            !compression.IsNeeded
                ? "不要"
                : !compression.CanCompress
                    ? "不可"
                    : DescribeAvailability(
                        compressedLevel,
                        compressedCompatibility!);
        var priorityDescription =
            originalLevel
                != KeyboardPracticeAvailabilityLevel.Insufficient
            && compressedLevel
                != KeyboardPracticeAvailabilityLevel.Insufficient
                ? "表示は楽曲圧縮なしを優先しています。"
                : string.Empty;

        var message =
            "練習可否:"
            + Environment.NewLine
            + $"楽曲圧縮なし: {originalDescription}"
            + Environment.NewLine
            + $"楽曲圧縮あり: {compressionDescription}";
        if (!string.IsNullOrWhiteSpace(
                priorityDescription))
        {
            message +=
                Environment.NewLine
                + priorityDescription;
        }

        return new KeyboardPracticeAvailability(
            selectedLevel,
            requiresCompression,
            shortText,
            message);
    }

    public static KeyboardPracticePlan CreatePracticePlan(
        MusicScore score,
        AppSettings settings,
        PracticeHandMode handMode,
        int appliedOctaveShiftSemitones)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(settings);

        var compatibility = Analyze(score, settings);
        var shift = NormalizeAppliedShift(
            appliedOctaveShiftSemitones,
            compatibility.AvailableOctaveShiftsSemitones);
        var playableRange = CreatePlayableMidiRange(settings, shift);
        var playableLowest = playableRange.LowestMidi;
        var playableHighest = playableRange.HighestMidi;
        var targetNotes = GetTargetNotes(score.Notes, handMode).ToArray();
        var autoPlayedCount = targetNotes.Count(note =>
            note.MidiNote < playableLowest || note.MidiNote > playableHighest);

        return new KeyboardPracticePlan(
            shift,
            playableLowest,
            playableHighest,
            targetNotes.Length,
            autoPlayedCount);
    }

    private static KeyboardPracticeCompatibility AnalyzeRange(
        IEnumerable<ScoreNote> notes,
        AppSettings settings,
        IReadOnlyList<int> availableShifts)
    {
        var materialized = notes.ToArray();
        var playableCounts = availableShifts.ToDictionary(
            shift => shift,
            shift => CountPlayable(
                materialized,
                settings.KeyboardLowestMidi + shift,
                settings.KeyboardHighestMidi + shift));

        if (materialized.Length == 0)
        {
            return new KeyboardPracticeCompatibility(
                FitsOriginal: true,
                FitsFullyWithRecommendedShift: true,
                RecommendedOctaveShiftSemitones: 0,
                OriginalPlayableNoteCount: 0,
                RecommendedPlayableNoteCount: 0,
                TargetNoteCount: 0,
                PlayableNoteCountByShift: playableCounts);
        }

        var originalCount = playableCounts.TryGetValue(0, out var unshiftedCount)
            ? unshiftedCount
            : 0;
        var bestShift = FindBestCoverageOctaveShift(playableCounts);
        var bestCount = playableCounts[bestShift];

        return new KeyboardPracticeCompatibility(
            FitsOriginal: originalCount == materialized.Length,
            FitsFullyWithRecommendedShift: bestCount == materialized.Length,
            RecommendedOctaveShiftSemitones: bestShift,
            OriginalPlayableNoteCount: originalCount,
            RecommendedPlayableNoteCount: bestCount,
            TargetNoteCount: materialized.Length,
            PlayableNoteCountByShift: playableCounts);
    }

    private static int FindBestCoverageOctaveShift(IReadOnlyDictionary<int, int> playableCounts)
    {
        var bestShift = 0;
        var bestPlayableCount = playableCounts.TryGetValue(0, out var originalCount)
            ? originalCount
            : -1;

        foreach (var pair in playableCounts)
        {
            if (pair.Value > bestPlayableCount
                || (pair.Value == bestPlayableCount
                    && Math.Abs(pair.Key) < Math.Abs(bestShift)))
            {
                bestPlayableCount = pair.Value;
                bestShift = pair.Key;
            }
        }

        return bestShift;
    }

    public static IReadOnlyList<int> GetAvailableOctaveShifts(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.HasOctaveShift)
        {
            return new[] { 0 };
        }

        var shifts = new List<int>();
        for (var shift = -120; shift <= 120; shift += 12)
        {
            var shiftedLowest = settings.KeyboardLowestMidi + shift;
            var shiftedHighest = settings.KeyboardHighestMidi + shift;
            if (shiftedLowest >= 0 && shiftedHighest <= 127)
            {
                shifts.Add(shift);
            }
        }

        if (!shifts.Contains(0))
        {
            shifts.Add(0);
            shifts.Sort();
        }

        return shifts;
    }

    public static PracticeMidiRange CreatePlayableMidiRange(
        AppSettings settings,
        int appliedOctaveShiftSemitones)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var availableShifts = GetAvailableOctaveShifts(settings);
        var shift = NormalizeAppliedShift(
            appliedOctaveShiftSemitones,
            availableShifts);
        return new PracticeMidiRange(
            settings.KeyboardLowestMidi + shift,
            settings.KeyboardHighestMidi + shift);
    }

    private static int NormalizeAppliedShift(
        int requestedShift,
        IReadOnlyList<int> availableShifts)
    {
        if (availableShifts.Contains(requestedShift))
        {
            return requestedShift;
        }

        return availableShifts.Contains(0)
            ? 0
            : availableShifts.OrderBy(shift => Math.Abs(shift - requestedShift)).First();
    }

    private static int CountPlayable(
        IEnumerable<ScoreNote> notes,
        int keyboardLowest,
        int keyboardHighest)
        => notes.Count(note =>
            note.MidiNote >= keyboardLowest && note.MidiNote <= keyboardHighest);

    private static IEnumerable<ScoreNote> GetTargetNotes(
        IEnumerable<ScoreNote> notes,
        PracticeHandMode handMode)
        => notes.Where(note => handMode switch
        {
            PracticeHandMode.Right => note.Hand == Hand.Right,
            PracticeHandMode.Left => note.Hand == Hand.Left,
            _ => true
        });

    private static KeyboardPracticeAvailabilityLevel GetBestAvailabilityLevel(
        KeyboardCompatibility compatibility)
    {
        var both =
            HasPlayableTarget(
                compatibility.BothHands);
        if (both)
        {
            return KeyboardPracticeAvailabilityLevel.BothHands;
        }

        var right =
            HasPlayableTarget(
                compatibility.RightHand);
        var left =
            HasPlayableTarget(
                compatibility.LeftHand);
        if (right && left)
        {
            return KeyboardPracticeAvailabilityLevel.EitherSingleHand;
        }

        if (right)
        {
            return KeyboardPracticeAvailabilityLevel.RightHand;
        }

        return left
            ? KeyboardPracticeAvailabilityLevel.LeftHand
            : KeyboardPracticeAvailabilityLevel.Insufficient;
    }

    private static bool HasPlayableTarget(
        KeyboardPracticeCompatibility compatibility)
        => compatibility.TargetNoteCount > 0
            && compatibility.FitsFullyWithRecommendedShift;

    private static string FormatAvailabilityShortText(
        KeyboardPracticeAvailabilityLevel level)
        => level switch
        {
            KeyboardPracticeAvailabilityLevel.BothHands =>
                "両手可",
            KeyboardPracticeAvailabilityLevel.EitherSingleHand =>
                "片手可",
            KeyboardPracticeAvailabilityLevel.RightHand =>
                "右手可",
            KeyboardPracticeAvailabilityLevel.LeftHand =>
                "左手可",
            _ => "音域不足"
        };

    private static string DescribeAvailability(
        KeyboardPracticeAvailabilityLevel level,
        KeyboardCompatibility compatibility)
        => level switch
        {
            KeyboardPracticeAvailabilityLevel.BothHands =>
                $"両手可{FormatRecommendedShift(compatibility.BothHands)}",
            KeyboardPracticeAvailabilityLevel.EitherSingleHand =>
                "右手可"
                + FormatRecommendedShift(
                    compatibility.RightHand)
                + " / 左手可"
                + FormatRecommendedShift(
                    compatibility.LeftHand),
            KeyboardPracticeAvailabilityLevel.RightHand =>
                $"右手可{FormatRecommendedShift(compatibility.RightHand)}",
            KeyboardPracticeAvailabilityLevel.LeftHand =>
                $"左手可{FormatRecommendedShift(compatibility.LeftHand)}",
            _ => "不可"
        };

    private static string FormatRecommendedShift(
        KeyboardPracticeCompatibility compatibility)
        => compatibility.RecommendedOctaveShiftSemitones == 0
            ? string.Empty
            : $" ({FormatOctaveShift(compatibility.RecommendedOctaveShiftSemitones)})";

    private static string FormatOctaveShift(int semitones)
        => semitones == 0
            ? "なし"
            : $"{semitones / 12:+#;-#;0} oct ({semitones:+#;-#;0}半音)";
}
