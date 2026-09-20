namespace PianoPracticeTool.Core;

public enum ScoreConsistencyIssueKind
{
    RepeatedKeyOverlap,
    TooManySimultaneousKeys
}

public sealed record ScoreConsistencyIssue(
    ScoreConsistencyIssueKind Kind,
    double Beat,
    string Description,
    bool CanAutoCorrect);

public sealed record ScoreConsistencyAnalysis(
    IReadOnlyList<ScoreConsistencyIssue> Issues)
{
    public static ScoreConsistencyAnalysis Empty { get; } =
        new(Array.Empty<ScoreConsistencyIssue>());

    public bool HasIssues =>
        Issues.Count > 0;

    public bool CanAutoCorrect =>
        Issues.Any(issue =>
            issue.CanAutoCorrect);

    public int AutoCorrectableIssueCount =>
        Issues.Count(issue =>
            issue.CanAutoCorrect);

    public int ManualReviewIssueCount =>
        Issues.Count(issue =>
            !issue.CanAutoCorrect);
}

public static class PracticeScorePreparation
{
    private const int MaximumKeysPerHand = 5;
    private const int MaximumHandCorrectionPasses = 3;
    private const int RapidLeapMinimumSemitones = 9;
    private const double RapidLeapMaximumGapBeats = 0.75d;
    private const double SourceHandChangePenalty = 4d;
    private const double HandReassignmentImprovementThreshold = 6d;

    public static int ApplyAutomaticHandCorrections(
        MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);

        var notes =
            score.Notes
                .OrderBy(note =>
                    note.StartBeat)
                .ThenBy(note =>
                    note.MidiNote)
                .ToArray();
        var changedNoteIds =
            new HashSet<int>();

        ApplyEmbeddedRangeHandCorrections(
            notes,
            changedNoteIds);
        ApplyRapidLeapHandCorrections(
            notes,
            changedNoteIds);

        return changedNoteIds.Count;
    }

    private static void ApplyEmbeddedRangeHandCorrections(
        IReadOnlyList<ScoreNote> notes,
        ISet<int> changedNoteIds)
    {
        foreach (var note in notes)
        {
            var oppositeHand =
                GetOppositeHand(
                    note.Hand);
            var ownActive =
                GetActiveNotes(
                    notes,
                    note.StartBeat,
                    note.Hand,
                    note.Id);
            if (ownActive.Count > 0)
            {
                continue;
            }

            var oppositeActive =
                GetActiveNotes(
                    notes,
                    note.StartBeat,
                    oppositeHand,
                    note.Id);
            if (oppositeActive.Count == 0)
            {
                continue;
            }

            var minimumPitch =
                oppositeActive.Min(item =>
                    item.MidiNote);
            var maximumPitch =
                oppositeActive.Max(item =>
                    item.MidiNote);
            if (note.MidiNote < minimumPitch
                || note.MidiNote > maximumPitch
                || !CanFitWithHand(
                    oppositeActive,
                    note))
            {
                continue;
            }

            note.Hand =
                oppositeHand;
            changedNoteIds.Add(
                note.Id);
        }
    }

    private static void ApplyRapidLeapHandCorrections(
        IReadOnlyList<ScoreNote> notes,
        ISet<int> changedNoteIds)
    {
        for (var pass = 0;
             pass < MaximumHandCorrectionPasses;
             pass++)
        {
            var changedThisPass =
                false;

            foreach (var note in notes)
            {
                var currentHand =
                    note.Hand;
                if (HasOnsetCompanion(
                        notes,
                        note,
                        currentHand))
                {
                    continue;
                }

                var previous =
                    FindNeighborAnchor(
                        notes,
                        note,
                        currentHand,
                        previous: true);
                var next =
                    FindNeighborAnchor(
                        notes,
                        note,
                        currentHand,
                        previous: false);
                if (!IsRapidLargeLeap(
                        note,
                        previous)
                    && !IsRapidLargeLeap(
                        note,
                        next))
                {
                    continue;
                }

                var oppositeHand =
                    GetOppositeHand(
                        currentHand);
                var oppositeActive =
                    GetActiveNotes(
                        notes,
                        note.StartBeat,
                        oppositeHand,
                        note.Id);
                if (!CanFitWithHand(
                        oppositeActive,
                        note))
                {
                    continue;
                }

                var currentCost =
                    CalculateRapidTravelCost(
                        note,
                        previous,
                        next);
                var oppositePrevious =
                    FindNeighborAnchor(
                        notes,
                        note,
                        oppositeHand,
                        previous: true);
                var oppositeNext =
                    FindNeighborAnchor(
                        notes,
                        note,
                        oppositeHand,
                        previous: false);
                var oppositeCost =
                    CalculateRapidTravelCost(
                        note,
                        oppositePrevious,
                        oppositeNext);
                if (oppositeHand
                    != GetSourceHand(
                        note))
                {
                    oppositeCost +=
                        SourceHandChangePenalty;
                }

                if (oppositeCost
                        + HandReassignmentImprovementThreshold
                    >= currentCost)
                {
                    continue;
                }

                note.Hand =
                    oppositeHand;
                changedNoteIds.Add(
                    note.Id);
                changedThisPass =
                    true;
            }

            if (!changedThisPass)
            {
                break;
            }
        }
    }

    public static ScoreConsistencyAnalysis AnalyzeConsistency(
        MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);

        var issues =
            new List<ScoreConsistencyIssue>();
        AddRepeatedKeyOverlapIssues(
            score,
            issues);
        AddTooManySimultaneousKeysIssues(
            score,
            issues);
        return issues.Count == 0
            ? ScoreConsistencyAnalysis.Empty
            : new ScoreConsistencyAnalysis(
                issues
                    .OrderBy(issue =>
                        issue.Beat)
                    .ThenBy(issue =>
                        issue.Kind)
                    .ToArray());
    }

    public static MusicScore ApplyPhysicalCorrections(
        MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);

        var notes =
            score.Notes
                .Select(CloneNote)
                .ToArray();
        foreach (var pitchGroup in notes
                     .GroupBy(note =>
                         note.MidiNote))
        {
            var ordered =
                pitchGroup
                    .OrderBy(note =>
                        note.StartBeat)
                    .ThenBy(note =>
                        note.Id)
                    .ToArray();
            for (var index = 0;
                 index < ordered.Length - 1;
                 index++)
            {
                var current =
                    ordered[index];
                var next =
                    ordered[index + 1];
                if (next.StartBeat
                        <= current.StartBeat
                        + ScoreTiming.BeatGroupingTolerance
                    || next.StartBeat
                        >= current.EndBeat
                        - ScoreTiming.EventBeatTolerance)
                {
                    continue;
                }

                current.DurationBeat =
                    Math.Max(
                        ScoreTiming.EventBeatTolerance,
                        next.StartBeat
                        - current.StartBeat);
            }
        }

        return new MusicScore
        {
            Title = score.Title,
            Composer = score.Composer,
            TempoBpm = score.TempoBpm,
            Notes = notes,
            Rests = score.Rests,
            TempoEvents = score.TempoEvents,
            KeySignatureEvents = score.KeySignatureEvents,
            HarmonyEvents = score.HarmonyEvents,
            Measures = score.Measures
        };
    }

    public static string BuildIssueSummary(
        MusicScore score,
        ScoreConsistencyAnalysis analysis,
        int maximumDetails = 6)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(analysis);

        if (!analysis.HasIssues)
        {
            return "譜面上の物理的な矛盾は検出されませんでした。";
        }

        var lines =
            new List<string>
            {
                $"譜面の整合性に {analysis.Issues.Count:N0} 件の注意点があります。"
            };
        foreach (var issue in analysis.Issues.Take(
                     Math.Max(
                         1,
                         maximumDetails)))
        {
            lines.Add(
                "・"
                + FormatBeatLocation(
                    score,
                    issue.Beat)
                + " "
                + issue.Description);
        }

        var remaining =
            analysis.Issues.Count
            - Math.Max(
                1,
                maximumDetails);
        if (remaining > 0)
        {
            lines.Add(
                $"ほか {remaining:N0} 件");
        }

        if (analysis.CanAutoCorrect)
        {
            lines.Add(
                "自動補正では、再打鍵が必要な同じキーの先行保持を後続打鍵位置までに短縮します。");
        }

        if (analysis.ManualReviewIssueCount > 0)
        {
            lines.Add(
                "自動補正できない項目は、元の譜面またはエディタでの確認が必要です。");
        }

        return string.Join(
            Environment.NewLine,
            lines);
    }

    private static Hand GetOppositeHand(
        Hand hand)
        => hand == Hand.Right
            ? Hand.Left
            : Hand.Right;

    private static Hand GetSourceHand(
        ScoreNote note)
        => note.Staff switch
        {
            1 => Hand.Right,
            2 => Hand.Left,
            _ => note.MidiNote < 60
                ? Hand.Left
                : Hand.Right
        };

    private static bool HasOnsetCompanion(
        IReadOnlyList<ScoreNote> notes,
        ScoreNote candidate,
        Hand hand)
        => notes.Any(note =>
            note.Id
                != candidate.Id
            && note.Hand
                == hand
            && Math.Abs(
                note.StartBeat
                - candidate.StartBeat)
                <= ScoreTiming.BeatGroupingTolerance);

    private static HandNeighbor? FindNeighborAnchor(
        IReadOnlyList<ScoreNote> notes,
        ScoreNote candidate,
        Hand hand,
        bool previous)
    {
        var neighbor =
            notes
                .Where(note =>
                    note.Id
                        != candidate.Id
                    && note.Hand
                        == hand
                    && (previous
                        ? note.StartBeat
                            < candidate.StartBeat
                            - ScoreTiming.BeatGroupingTolerance
                        : note.StartBeat
                            > candidate.StartBeat
                            + ScoreTiming.BeatGroupingTolerance))
                .OrderBy(note =>
                    previous
                        ? -note.StartBeat
                        : note.StartBeat)
                .FirstOrDefault();
        if (neighbor is null)
        {
            return null;
        }

        var neighborBeat =
            neighbor.StartBeat;
        var group =
            notes
                .Where(note =>
                    note.Id
                        != candidate.Id
                    && note.Hand
                        == hand
                    && Math.Abs(
                        note.StartBeat
                        - neighborBeat)
                        <= ScoreTiming.BeatGroupingTolerance)
                .OrderBy(note =>
                    note.MidiNote)
                .ToArray();
        var anchor =
            group[
                (group.Length - 1)
                / 2];
        return new HandNeighbor(
            neighborBeat,
            anchor.MidiNote);
    }

    private static bool IsRapidLargeLeap(
        ScoreNote note,
        HandNeighbor? neighbor)
    {
        if (neighbor is null)
        {
            return false;
        }

        var gap =
            Math.Abs(
                note.StartBeat
                - neighbor.Value.StartBeat);
        var distance =
            Math.Abs(
                note.MidiNote
                - neighbor.Value.MidiNote);
        return gap
                <= RapidLeapMaximumGapBeats
                + ScoreTiming.BeatGroupingTolerance
            && distance
                >= RapidLeapMinimumSemitones;
    }

    private static double CalculateRapidTravelCost(
        ScoreNote note,
        HandNeighbor? previous,
        HandNeighbor? next)
        => CalculateNeighborTravelCost(
               note,
               previous)
            + CalculateNeighborTravelCost(
                note,
                next);

    private static double CalculateNeighborTravelCost(
        ScoreNote note,
        HandNeighbor? neighbor)
    {
        if (neighbor is null)
        {
            return 0d;
        }

        var gap =
            Math.Abs(
                note.StartBeat
                - neighbor.Value.StartBeat);
        if (gap
            > RapidLeapMaximumGapBeats
                + ScoreTiming.BeatGroupingTolerance)
        {
            return 0d;
        }

        var distance =
            Math.Abs(
                note.MidiNote
                - neighbor.Value.MidiNote);
        var weight =
            gap <= 0.25d
                + ScoreTiming.BeatGroupingTolerance
                ? 1.8d
                : gap <= 0.5d
                    + ScoreTiming.BeatGroupingTolerance
                    ? 1.4d
                    : 1d;

        return Math.Max(
                   0d,
                   distance - 5d)
                * weight
            + Math.Max(
                0d,
                distance - 9d)
                * 1.8d
                * weight;
    }

    private static IReadOnlyList<ScoreNote> GetActiveNotes(
        IReadOnlyList<ScoreNote> notes,
        double beat,
        Hand hand,
        int excludedNoteId)
        => notes
            .Where(note =>
                note.Id
                    != excludedNoteId
                && note.Hand
                    == hand
                && note.StartBeat
                    <= beat
                    + ScoreTiming.EventBeatTolerance
                && note.EndBeat
                    > beat
                    + ScoreTiming.EventBeatTolerance)
            .ToArray();

    private static bool CanFitWithHand(
        IReadOnlyList<ScoreNote> activeNotes,
        ScoreNote candidate)
    {
        if (activeNotes.Any(note =>
                note.MidiNote
                    == candidate.MidiNote))
        {
            return false;
        }

        var pitches =
            activeNotes
                .Select(note =>
                    note.MidiNote)
                .Append(
                    candidate.MidiNote)
                .Distinct()
                .OrderBy(midi =>
                    midi)
                .ToArray();
        if (pitches.Length > MaximumKeysPerHand)
        {
            return false;
        }

        return pitches.Length <= 1
            || pitches[^1]
                - pitches[0]
                <= 12;
    }

    private static void AddRepeatedKeyOverlapIssues(
        MusicScore score,
        List<ScoreConsistencyIssue> issues)
    {
        foreach (var pitchGroup in score.Notes
                     .GroupBy(note =>
                         note.MidiNote))
        {
            var ordered =
                pitchGroup
                    .OrderBy(note =>
                        note.StartBeat)
                    .ThenBy(note =>
                        note.Id)
                    .ToArray();
            for (var index = 0;
                 index < ordered.Length - 1;
                 index++)
            {
                var current =
                    ordered[index];
                var next =
                    ordered[index + 1];
                if (next.StartBeat
                        <= current.StartBeat
                        + ScoreTiming.BeatGroupingTolerance
                    || next.StartBeat
                        >= current.EndBeat
                        - ScoreTiming.EventBeatTolerance)
                {
                    continue;
                }

                issues.Add(
                    new ScoreConsistencyIssue(
                        ScoreConsistencyIssueKind.RepeatedKeyOverlap,
                        next.StartBeat,
                        $"{MidiPitch.ToName(current.MidiNote)} を保持中に同じキーの再打鍵があります。",
                        CanAutoCorrect: true));
            }
        }
    }

    private static void AddTooManySimultaneousKeysIssues(
        MusicScore score,
        List<ScoreConsistencyIssue> issues)
    {
        var eventBeats =
            score.Notes
                .Select(note =>
                    note.StartBeat)
                .Distinct()
                .OrderBy(beat =>
                    beat)
                .ToArray();

        foreach (var beat in eventBeats)
        {
            foreach (var hand in new[]
                     {
                         Hand.Right,
                         Hand.Left
                     })
            {
                var activePitches =
                    score.Notes
                        .Where(note =>
                            note.Hand
                                == hand
                            && note.StartBeat
                                <= beat
                                + ScoreTiming.EventBeatTolerance
                            && note.EndBeat
                                > beat
                                + ScoreTiming.EventBeatTolerance)
                        .Select(note =>
                            note.MidiNote)
                        .Distinct()
                        .ToArray();
                if (activePitches.Length
                    <= MaximumKeysPerHand)
                {
                    continue;
                }

                var handText =
                    hand == Hand.Right
                        ? "右手"
                        : "左手";
                issues.Add(
                    new ScoreConsistencyIssue(
                        ScoreConsistencyIssueKind.TooManySimultaneousKeys,
                        beat,
                        $"{handText}に同時 {activePitches.Length:N0} 鍵が割り当てられています。",
                        CanAutoCorrect: false));
            }
        }
    }

    private static string FormatBeatLocation(
        MusicScore score,
        double beat)
    {
        var measure =
            score.GetMeasureAt(
                beat);
        if (measure is null)
        {
            return $"{beat:0.###} beat";
        }

        var localBeat =
            Math.Max(
                0d,
                beat
                - measure.StartBeat);
        return $"小節 {measure.Number} + {localBeat:0.###} beat";
    }

    private readonly record struct HandNeighbor(
        double StartBeat,
        int MidiNote);

    private static ScoreNote CloneNote(
        ScoreNote note)
        => new()
        {
            Id = note.Id,
            MidiNote = note.MidiNote,
            StartBeat = note.StartBeat,
            DurationBeat = note.DurationBeat,
            Staff = note.Staff,
            Voice = note.Voice,
            Hand = note.Hand,
            Finger = note.Finger
        };
}
