namespace PianoPracticeTool.Core;

public static class FingeringGenerator
{
    private const double ImpossibleCost = 1_000_000d;
    private const double RepeatedPitchFingerChangePenalty = 3d;
    private const double DirectionMismatchPenalty = 2.5d;
    private const double EdgeFingerPenalty = 0.25d;
    private const double ThumbOnBlackKeyPenalty = 1.5d;
    private const double ThumbBlackWhiteNeighborPenalty = 2.5d;
    private const double ThumbPassWhiteToBlackPenalty = 2.5d;
    private const double ThumbPassSameLevelPenalty = 0.8d;
    private const double ChordStretchPenalty = 1.25d;
    private const double MovementStepPenalty = 0.55d;
    private const double PhraseResetGapBeats = 0.5d;

    public static void Generate(
        MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(
            score);

        GenerateForHand(
            score.Notes
                .Where(note =>
                    note.Hand == Hand.Right)
                .ToArray(),
            Hand.Right);
        GenerateForHand(
            score.Notes
                .Where(note =>
                    note.Hand == Hand.Left)
                .ToArray(),
            Hand.Left);
    }

    private static void GenerateForHand(
        IReadOnlyList<ScoreNote> notes,
        Hand hand)
    {
        if (notes.Count == 0)
        {
            return;
        }

        var groups =
            BuildGroups(
                notes);
        foreach (var phrase in BuildPhrases(
                     groups))
        {
            GeneratePhrase(
                phrase,
                hand);
        }
    }

    private static void GeneratePhrase(
        IReadOnlyList<NoteGroup> groups,
        Hand hand)
    {
        if (groups.Count == 0)
        {
            return;
        }

        var candidatesByGroup =
            groups
                .Select(group =>
                    BuildCandidates(
                        group,
                        hand))
                .ToArray();

        var costs =
            new double[groups.Count][];
        var previousCandidateIndexes =
            new int[groups.Count][];

        for (var groupIndex = 0;
             groupIndex < groups.Count;
             groupIndex++)
        {
            var candidates =
                candidatesByGroup[groupIndex];
            costs[groupIndex] =
                Enumerable
                    .Repeat(
                        double.PositiveInfinity,
                        candidates.Count)
                    .ToArray();
            previousCandidateIndexes[groupIndex] =
                Enumerable
                    .Repeat(
                        -1,
                        candidates.Count)
                    .ToArray();

            for (var candidateIndex = 0;
                 candidateIndex < candidates.Count;
                 candidateIndex++)
            {
                var candidate =
                    candidates[candidateIndex];
                var previousGroup =
                    groupIndex > 0
                        ? groups[groupIndex - 1]
                        : null;
                var nextGroup =
                    groupIndex + 1 < groups.Count
                        ? groups[groupIndex + 1]
                        : null;
                var intrinsicCost =
                    CalculateIntrinsicCost(
                        groups[groupIndex],
                        candidate,
                        previousGroup,
                        nextGroup,
                        hand);

                if (groupIndex == 0)
                {
                    costs[groupIndex][candidateIndex] =
                        intrinsicCost
                        + CalculateStartingCost(
                            groups[groupIndex],
                            candidate,
                            nextGroup,
                            hand);
                    continue;
                }

                var previousCandidates =
                    candidatesByGroup[groupIndex - 1];
                for (var previousIndex = 0;
                     previousIndex < previousCandidates.Count;
                     previousIndex++)
                {
                    var previousCost =
                        costs[groupIndex - 1][previousIndex];
                    if (double.IsPositiveInfinity(
                            previousCost))
                    {
                        continue;
                    }

                    var transitionCost =
                        CalculateTransitionCost(
                            groups[groupIndex - 1],
                            previousCandidates[previousIndex],
                            groups[groupIndex],
                            candidate,
                            hand);
                    var totalCost =
                        previousCost
                        + intrinsicCost
                        + transitionCost;

                    if (totalCost
                        < costs[groupIndex][candidateIndex])
                    {
                        costs[groupIndex][candidateIndex] =
                            totalCost;
                        previousCandidateIndexes[groupIndex][candidateIndex] =
                            previousIndex;
                    }
                }
            }
        }

        var selectedCandidateIndexes =
            Backtrack(
                costs,
                previousCandidateIndexes);
        for (var groupIndex = 0;
             groupIndex < groups.Count;
             groupIndex++)
        {
            var group =
                groups[groupIndex];
            var candidate =
                candidatesByGroup[groupIndex][
                    selectedCandidateIndexes[groupIndex]];

            for (var noteIndex = 0;
                 noteIndex < group.Notes.Count;
                 noteIndex++)
            {
                if (group.Notes[noteIndex].Finger
                    is >= 1 and <= 5)
                {
                    continue;
                }

                group.Notes[noteIndex].Finger =
                    candidate.Fingers[noteIndex];
            }
        }
    }

    private static IReadOnlyList<NoteGroup> BuildGroups(
        IReadOnlyList<ScoreNote> notes)
    {
        var onsetGroups =
            new List<NoteGroup>();

        foreach (var note in notes
                     .OrderBy(note =>
                         note.StartBeat)
                     .ThenBy(note =>
                         note.MidiNote))
        {
            var currentGroup =
                onsetGroups.LastOrDefault();
            if (currentGroup is null
                || Math.Abs(
                    currentGroup.StartBeat
                    - note.StartBeat)
                    > ScoreTiming.BeatGroupingTolerance)
            {
                onsetGroups.Add(
                    new NoteGroup(
                        note.StartBeat,
                        new List<ScoreNote>
                        {
                            note
                        }));
                continue;
            }

            currentGroup.Notes.Add(
                note);
        }

        var groups =
            new List<NoteGroup>(
                onsetGroups.Count);
        var activeNotes =
            new List<ScoreNote>();

        foreach (var onsetGroup in onsetGroups)
        {
            activeNotes.RemoveAll(note =>
                note.EndBeat
                    <= onsetGroup.StartBeat
                    + ScoreTiming.EventBeatTolerance);
            activeNotes.AddRange(
                onsetGroup.Notes);

            groups.Add(
                new NoteGroup(
                    onsetGroup.StartBeat,
                    activeNotes
                        .OrderBy(note =>
                            note.MidiNote)
                        .ThenBy(note =>
                            note.Id)
                        .ToList()));
        }

        return groups;
    }

    private static IReadOnlyList<IReadOnlyList<NoteGroup>> BuildPhrases(
        IReadOnlyList<NoteGroup> groups)
    {
        var phrases =
            new List<IReadOnlyList<NoteGroup>>();
        var current =
            new List<NoteGroup>();
        var activeUntilBeat =
            double.NegativeInfinity;

        foreach (var group in groups)
        {
            if (current.Count > 0)
            {
                var silentGap =
                    group.StartBeat
                    - activeUntilBeat;
                if (silentGap
                    >= PhraseResetGapBeats
                    - ScoreTiming.EventBeatTolerance)
                {
                    phrases.Add(
                        current.ToArray());
                    current.Clear();
                    activeUntilBeat =
                        double.NegativeInfinity;
                }
            }

            current.Add(
                group);
            activeUntilBeat =
                Math.Max(
                    activeUntilBeat,
                    group.EndBeat);
        }

        if (current.Count > 0)
        {
            phrases.Add(
                current.ToArray());
        }

        return phrases;
    }

    private static IReadOnlyList<FingeringCandidate> BuildCandidates(
        NoteGroup group,
        Hand hand)
    {
        var noteCount =
            group.Notes.Count;
        if (noteCount <= 0)
        {
            return Array.Empty<FingeringCandidate>();
        }

        IReadOnlyList<FingeringCandidate> candidates;
        if (noteCount == 1)
        {
            candidates =
                Enumerable
                    .Range(
                        1,
                        5)
                    .Select(finger =>
                        new FingeringCandidate(
                            new[]
                            {
                                finger
                            }))
                    .ToArray();

            return ApplyExplicitFingeringConstraints(
                group,
                candidates);
        }

        if (noteCount <= 5)
        {
            var combinations =
                new List<int[]>();
            BuildFingerCombinations(
                1,
                noteCount,
                new List<int>(),
                combinations);

            candidates =
                combinations
                    .Select(fingers =>
                        hand == Hand.Right
                            ? fingers
                            : fingers
                                .Reverse()
                                .ToArray())
                    .Select(fingers =>
                        new FingeringCandidate(
                            fingers))
                    .ToArray();

            return ApplyExplicitFingeringConstraints(
                group,
                candidates);
        }

        var fallback =
            new int[noteCount];
        for (var index = 0;
             index < noteCount;
             index++)
        {
            var normalized =
                noteCount == 1
                    ? 0d
                    : (double)index
                        / (noteCount - 1);
            var rightFinger =
                Math.Clamp(
                    1
                    + (int)Math.Round(
                        normalized * 4d),
                    1,
                    5);
            fallback[index] =
                hand == Hand.Right
                    ? rightFinger
                    : 6 - rightFinger;
        }

        return new[]
        {
            new FingeringCandidate(
                fallback)
        };
    }

    private static IReadOnlyList<FingeringCandidate> ApplyExplicitFingeringConstraints(
        NoteGroup group,
        IReadOnlyList<FingeringCandidate> candidates)
    {
        var constrained =
            candidates
                .Where(candidate =>
                    group.Notes
                        .Select((note, index) =>
                            note.Finger is < 1 or > 5
                            || candidate.Fingers[index]
                                == note.Finger)
                        .All(matches =>
                            matches))
                .ToArray();

        return constrained.Length > 0
            ? constrained
            : candidates;
    }

    private static void BuildFingerCombinations(
        int nextFinger,
        int remainingCount,
        List<int> current,
        List<int[]> results)
    {
        if (remainingCount == 0)
        {
            results.Add(
                current.ToArray());
            return;
        }

        var maximumStart =
            5
            - remainingCount
            + 1;
        for (var finger = nextFinger;
             finger <= maximumStart;
             finger++)
        {
            current.Add(
                finger);
            BuildFingerCombinations(
                finger + 1,
                remainingCount - 1,
                current,
                results);
            current.RemoveAt(
                current.Count - 1);
        }
    }

    private static double CalculateStartingCost(
        NoteGroup group,
        FingeringCandidate candidate,
        NoteGroup? nextGroup,
        Hand hand)
    {
        var anchor =
            GetAnchor(
                group,
                candidate);
        if (nextGroup is null)
        {
            return Math.Abs(
                       anchor.Finger - 3)
                * 0.1d;
        }

        var pitchDelta =
            GetRepresentativePitch(
                nextGroup)
            - anchor.Pitch;
        var orientedPitchDelta =
            hand == Hand.Right
                ? pitchDelta
                : -pitchDelta;
        var preferredFinger =
            orientedPitchDelta switch
            {
                > 0 =>
                    IsBlackKey(
                        anchor.Pitch)
                        ? 2
                        : 1,
                < 0 =>
                    IsBlackKey(
                        anchor.Pitch)
                        ? 4
                        : 5,
                _ => 3
            };

        return Math.Abs(
                   anchor.Finger
                   - preferredFinger)
            * 0.2d;
    }

    private static double CalculateIntrinsicCost(
        NoteGroup group,
        FingeringCandidate candidate,
        NoteGroup? previousGroup,
        NoteGroup? nextGroup,
        Hand hand)
    {
        if (group.Notes.Count
            != candidate.Fingers.Count)
        {
            return ImpossibleCost;
        }

        var previousPitch =
            previousGroup is null
                ? (int?)null
                : GetRepresentativePitch(
                    previousGroup);
        var nextPitch =
            nextGroup is null
                ? (int?)null
                : GetRepresentativePitch(
                    nextGroup);
        var cost =
            0d;

        for (var index = 0;
             index < group.Notes.Count;
             index++)
        {
            var note =
                group.Notes[index];
            var finger =
                candidate.Fingers[index];

            if (finger is < 1 or > 5)
            {
                return ImpossibleCost;
            }

            if (IsBlackKey(
                    note.MidiNote)
                && finger == 1)
            {
                cost +=
                    ThumbOnBlackKeyPenalty;
                if (previousPitch is int previous
                    && !IsBlackKey(
                        previous))
                {
                    cost +=
                        ThumbBlackWhiteNeighborPenalty;
                }

                if (nextPitch is int next
                    && !IsBlackKey(
                        next))
                {
                    cost +=
                        ThumbBlackWhiteNeighborPenalty;
                }
            }

            if (group.Notes.Count == 1
                && finger is 1 or 5)
            {
                cost +=
                    EdgeFingerPenalty;
            }
        }

        for (var index = 1;
             index < group.Notes.Count;
             index++)
        {
            var previousNote =
                group.Notes[index - 1];
            var note =
                group.Notes[index];
            var previousFinger =
                candidate.Fingers[index - 1];
            var finger =
                candidate.Fingers[index];
            var pitchDistance =
                note.MidiNote
                - previousNote.MidiNote;
            var fingerDistance =
                hand == Hand.Right
                    ? finger
                        - previousFinger
                    : previousFinger
                        - finger;

            if (pitchDistance > 0
                && fingerDistance <= 0)
            {
                return ImpossibleCost;
            }

            var comfortableSemitones =
                GetComfortableSemitoneDistance(
                    previousFinger,
                    finger);
            var excessStretch =
                Math.Max(
                    0d,
                    pitchDistance
                    - comfortableSemitones);
            cost +=
                excessStretch
                * ChordStretchPenalty;
        }

        return cost;
    }

    private static double CalculateTransitionCost(
        NoteGroup previousGroup,
        FingeringCandidate previousCandidate,
        NoteGroup currentGroup,
        FingeringCandidate currentCandidate,
        Hand hand)
    {
        if (!HeldFingerAssignmentsMatch(
                previousGroup,
                previousCandidate,
                currentGroup,
                currentCandidate))
        {
            return ImpossibleCost;
        }

        var previousAnchor =
            GetAnchor(
                previousGroup,
                previousCandidate);
        var currentAnchor =
            GetAnchor(
                currentGroup,
                currentCandidate);
        var pitchDelta =
            currentAnchor.Pitch
            - previousAnchor.Pitch;
        var fingerDelta =
            hand == Hand.Right
                ? currentAnchor.Finger
                    - previousAnchor.Finger
                : previousAnchor.Finger
                    - currentAnchor.Finger;

        var absolutePitchDelta =
            Math.Abs(
                pitchDelta);
        var expectedFingerDelta =
            Math.Clamp(
                (int)Math.Round(
                    pitchDelta / 2d),
                -4,
                4);
        var movementCost =
            Math.Abs(
                fingerDelta
                - expectedFingerDelta)
            * MovementStepPenalty;

        var directionMismatch =
            pitchDelta != 0
            && Math.Sign(
                fingerDelta)
                != Math.Sign(
                    pitchDelta);
        if (directionMismatch)
        {
            movementCost +=
                IsThumbPass(
                    previousAnchor.Finger,
                    currentAnchor.Finger)
                    ? 0.9d
                    : IsLikelyFingerCrossing(
                        previousAnchor.Finger,
                        currentAnchor.Finger,
                        pitchDelta,
                        hand)
                        ? 0.9d
                        : DirectionMismatchPenalty;
        }

        if (absolutePitchDelta >= 12)
        {
            movementCost *=
                0.55d;
        }

        movementCost +=
            CalculateBlackThumbTransitionCost(
                previousAnchor,
                currentAnchor);
        if (directionMismatch
            && IsThumbPass(
                previousAnchor.Finger,
                currentAnchor.Finger))
        {
            movementCost +=
                CalculateThumbPassingKeyLevelCost(
                    previousAnchor,
                    currentAnchor);
        }

        var repeatedPitchCost =
            CalculateRepeatedPitchCost(
                previousGroup,
                previousCandidate,
                currentGroup,
                currentCandidate);

        var silentGap =
            Math.Max(
                0d,
                currentGroup.StartBeat
                - previousGroup.EndBeat);
        var repositionDiscount =
            silentGap
                >= PhraseResetGapBeats
                ? 0.45d
                : silentGap >= 0.25d
                    ? 0.7d
                    : 1d;

        return movementCost
                * repositionDiscount
            + repeatedPitchCost;
    }

    private static bool HeldFingerAssignmentsMatch(
        NoteGroup previousGroup,
        FingeringCandidate previousCandidate,
        NoteGroup currentGroup,
        FingeringCandidate currentCandidate)
    {
        for (var previousIndex = 0;
             previousIndex < previousGroup.Notes.Count;
             previousIndex++)
        {
            var previousNote =
                previousGroup.Notes[previousIndex];
            if (previousNote.EndBeat
                <= currentGroup.StartBeat
                + ScoreTiming.EventBeatTolerance)
            {
                continue;
            }

            for (var currentIndex = 0;
                 currentIndex < currentGroup.Notes.Count;
                 currentIndex++)
            {
                var currentNote =
                    currentGroup.Notes[currentIndex];
                if (currentNote.Id
                    != previousNote.Id)
                {
                    continue;
                }

                if (previousCandidate.Fingers[previousIndex]
                    != currentCandidate.Fingers[currentIndex])
                {
                    return false;
                }

                break;
            }
        }

        return true;
    }

    private static double CalculateBlackThumbTransitionCost(
        Anchor previous,
        Anchor current)
    {
        var cost =
            0d;
        if (current.Finger == 1
            && IsBlackKey(
                current.Pitch)
            && !IsBlackKey(
                previous.Pitch))
        {
            cost +=
                ThumbBlackWhiteNeighborPenalty;
        }

        if (previous.Finger == 1
            && IsBlackKey(
                previous.Pitch)
            && !IsBlackKey(
                current.Pitch))
        {
            cost +=
                ThumbBlackWhiteNeighborPenalty;
        }

        return cost;
    }

    private static double CalculateThumbPassingKeyLevelCost(
        Anchor previous,
        Anchor current)
    {
        var thumb =
            previous.Finger == 1
                ? previous
                : current;
        var other =
            previous.Finger == 1
                ? current
                : previous;
        var thumbIsBlack =
            IsBlackKey(
                thumb.Pitch);
        var otherIsBlack =
            IsBlackKey(
                other.Pitch);

        if (thumbIsBlack
            && !otherIsBlack)
        {
            return ThumbPassWhiteToBlackPenalty;
        }

        return thumbIsBlack
               == otherIsBlack
            ? ThumbPassSameLevelPenalty
            : 0d;
    }

    private static double CalculateRepeatedPitchCost(
        NoteGroup previousGroup,
        FingeringCandidate previousCandidate,
        NoteGroup currentGroup,
        FingeringCandidate currentCandidate)
    {
        var cost =
            0d;

        for (var previousIndex = 0;
             previousIndex < previousGroup.Notes.Count;
             previousIndex++)
        {
            var previousNote =
                previousGroup.Notes[previousIndex];
            for (var currentIndex = 0;
                 currentIndex < currentGroup.Notes.Count;
                 currentIndex++)
            {
                var currentNote =
                    currentGroup.Notes[currentIndex];
                if (previousNote.MidiNote
                    != currentNote.MidiNote)
                {
                    continue;
                }

                if (previousCandidate.Fingers[previousIndex]
                    != currentCandidate.Fingers[currentIndex])
                {
                    cost +=
                        RepeatedPitchFingerChangePenalty;
                }
            }
        }

        return cost;
    }

    private static Anchor GetAnchor(
        NoteGroup group,
        FingeringCandidate candidate)
    {
        var index =
            (group.Notes.Count - 1)
            / 2;
        return new Anchor(
            group.Notes[index].MidiNote,
            candidate.Fingers[index]);
    }

    private static int GetRepresentativePitch(
        NoteGroup group)
        => group.Notes[
            (group.Notes.Count - 1)
            / 2].MidiNote;

    private static double GetComfortableSemitoneDistance(
        int lowerPitchFinger,
        int higherPitchFinger)
    {
        var distance =
            Math.Abs(
                higherPitchFinger
                - lowerPitchFinger);
        return distance switch
        {
            0 => 0d,
            1 => 3d,
            2 => 5d,
            3 => 8d,
            _ => 12d
        };
    }

    private static bool IsThumbPass(
        int previousFinger,
        int currentFinger)
        => previousFinger == 1
            || currentFinger == 1;

    private static bool IsLikelyFingerCrossing(
        int previousFinger,
        int currentFinger,
        int pitchDelta,
        Hand hand)
    {
        if (hand == Hand.Right)
        {
            return (pitchDelta > 0
                    && previousFinger >= 3
                    && currentFinger == 1)
                || (pitchDelta < 0
                    && previousFinger <= 2
                    && currentFinger >= 4);
        }

        return (pitchDelta > 0
                && previousFinger <= 3
                && currentFinger == 5)
            || (pitchDelta < 0
                && previousFinger >= 4
                && currentFinger <= 2);
    }

    private static IReadOnlyList<int> Backtrack(
        IReadOnlyList<double[]> costs,
        IReadOnlyList<int[]> previousCandidateIndexes)
    {
        var result =
            new int[costs.Count];
        var lastCosts =
            costs[^1];
        var selectedIndex =
            0;
        var selectedCost =
            double.PositiveInfinity;

        for (var index = 0;
             index < lastCosts.Length;
             index++)
        {
            if (lastCosts[index]
                < selectedCost)
            {
                selectedCost =
                    lastCosts[index];
                selectedIndex =
                    index;
            }
        }

        for (var groupIndex = costs.Count - 1;
             groupIndex >= 0;
             groupIndex--)
        {
            result[groupIndex] =
                selectedIndex;
            selectedIndex =
                groupIndex > 0
                    ? previousCandidateIndexes[groupIndex][selectedIndex]
                    : -1;

            if (groupIndex > 0
                && selectedIndex < 0)
            {
                selectedIndex =
                    0;
            }
        }

        return result;
    }

    private static bool IsBlackKey(
        int midiNote)
        => midiNote % 12
            is 1 or 3 or 6 or 8 or 10;

    private sealed record NoteGroup(
        double StartBeat,
        List<ScoreNote> Notes)
    {
        public double EndBeat =>
            Notes.Count == 0
                ? StartBeat
                : Notes.Max(note =>
                    note.EndBeat);
    }

    private sealed record FingeringCandidate(
        IReadOnlyList<int> Fingers);

    private readonly record struct Anchor(
        int Pitch,
        int Finger);
}
