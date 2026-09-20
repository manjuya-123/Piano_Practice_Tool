namespace PianoPracticeTool.Core;

public sealed record MusicScaleDefinition(
    string Id,
    string DisplayName,
    string MusicXmlMode,
    int TonicOffsetFromMajor,
    IReadOnlyList<int> Intervals,
    bool IsDiatonic,
    bool IncludeInSuggestions = true);

public sealed record MusicScaleCandidate(
    int Fifths,
    string ScaleId,
    string Label,
    double Score);

public sealed record MusicKeyAnalysis(
    int Fifths,
    string Mode,
    string ScaleId,
    string DisplayName,
    bool IsInferred,
    int RootPitchClass,
    IReadOnlyList<int> PitchClasses);

public sealed record MeasureHarmonyAnalysis(
    int MeasureNumber,
    double Beat,
    string Symbol,
    bool IsInferred,
    double Confidence,
    IReadOnlyList<int> PitchClasses);

public sealed record EditorScaleGuideRegion(
    double StartBeat,
    double EndBeat,
    int RootPitchClass,
    IReadOnlyList<int> PitchClasses);

public sealed record EditorChordGuideRegion(
    double StartBeat,
    double EndBeat,
    IReadOnlyList<int> PitchClasses);

public sealed record HarmonyTimelineSegment(
    double StartBeat,
    double EndBeat,
    string Symbol,
    bool IsInferred,
    double Confidence,
    IReadOnlyList<int> PitchClasses);

public static class MusicTheoryAnalyzer
{
    public const string ScaleEventsMetadataFieldName =
        "piano-practice-tool:scale-events";

    private static readonly string[] SharpPitchNames =
    {
        "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
    };

    private static readonly string[] FlatPitchNames =
    {
        "C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"
    };

    private static readonly string[] MajorNamesByFifths =
    {
        "Cb", "Gb", "Db", "Ab", "Eb", "Bb", "F", "C",
        "G", "D", "A", "E", "B", "F#", "C#"
    };

    private static readonly string[] MinorNamesByFifths =
    {
        "Ab", "Eb", "Bb", "F", "C", "G", "D", "A",
        "E", "B", "F#", "C#", "G#", "D#", "A#"
    };

    private static readonly double[] MajorProfile =
    {
        6.35d, 2.23d, 3.48d, 2.33d, 4.38d, 4.09d,
        2.52d, 5.19d, 2.39d, 3.66d, 2.29d, 2.88d
    };

    private static readonly double[] MinorProfile =
    {
        6.33d, 2.68d, 3.52d, 5.38d, 2.60d, 3.53d,
        2.54d, 4.75d, 3.98d, 2.69d, 3.34d, 3.17d
    };

    private static readonly MusicScaleDefinition[] ScaleDefinitionArray =
    {
        new("major", "Major", "major", 0, new[] { 0, 2, 4, 5, 7, 9, 11 }, true),
        new("dorian", "Dorian", "dorian", 2, new[] { 0, 2, 3, 5, 7, 9, 10 }, true),
        new("phrygian", "Phrygian", "phrygian", 4, new[] { 0, 1, 3, 5, 7, 8, 10 }, true),
        new("lydian", "Lydian", "lydian", 5, new[] { 0, 2, 4, 6, 7, 9, 11 }, true),
        new("mixolydian", "Mixolydian", "mixolydian", 7, new[] { 0, 2, 4, 5, 7, 9, 10 }, true),
        new("minor", "Natural Minor", "minor", 9, new[] { 0, 2, 3, 5, 7, 8, 10 }, true),
        new("locrian", "Locrian", "locrian", 11, new[] { 0, 1, 3, 5, 6, 8, 10 }, true),
        new("harmonic-minor", "Harmonic Minor", "minor", 9, new[] { 0, 2, 3, 5, 7, 8, 11 }, false),
        new("melodic-minor", "Melodic Minor", "minor", 9, new[] { 0, 2, 3, 5, 7, 9, 11 }, false),
        new("major-pentatonic", "Major Pentatonic", "major", 0, new[] { 0, 2, 4, 7, 9 }, false),
        new("minor-pentatonic", "Minor Pentatonic", "minor", 9, new[] { 0, 3, 5, 7, 10 }, false),
        new("blues", "Blues", "minor", 9, new[] { 0, 3, 5, 6, 7, 10 }, false),
        new("whole-tone", "Whole Tone", "none", 0, new[] { 0, 2, 4, 6, 8, 10 }, false),
        new(
            "chromatic",
            "Chromatic",
            "none",
            0,
            Enumerable.Range(0, 12).ToArray(),
            false,
            IncludeInSuggestions: false)
    };

    private static readonly ChordTemplate[] ChordTemplates =
    {
        new("", new[] { 0, 4, 7 }),
        new("m", new[] { 0, 3, 7 }),
        new("7", new[] { 0, 4, 7, 10 }),
        new("maj7", new[] { 0, 4, 7, 11 }),
        new("m7", new[] { 0, 3, 7, 10 }),
        new("dim", new[] { 0, 3, 6 }),
        new("dim7", new[] { 0, 3, 6, 9 }),
        new("aug", new[] { 0, 4, 8 }),
        new("aug7", new[] { 0, 4, 8, 10 }),
        new("m7♭5", new[] { 0, 3, 6, 10 }),
        new("mMaj7", new[] { 0, 3, 7, 11 }),
        new("6", new[] { 0, 4, 7, 9 }),
        new("m6", new[] { 0, 3, 7, 9 }),
        new("sus2", new[] { 0, 2, 7 }),
        new("sus4", new[] { 0, 5, 7 }),
        new("5", new[] { 0, 7 }),
        new("add9", new[] { 0, 2, 4, 7 }),
        new("madd9", new[] { 0, 2, 3, 7 }),
        new("9", new[] { 0, 2, 4, 7, 10 }),
        new("maj9", new[] { 0, 2, 4, 7, 11 }),
        new("m9", new[] { 0, 2, 3, 7, 10 }),
        new("11", new[] { 0, 2, 4, 5, 7, 10 }),
        new("maj11", new[] { 0, 2, 4, 5, 7, 11 }),
        new("m11", new[] { 0, 2, 3, 5, 7, 10 }),
        new("13", new[] { 0, 2, 4, 5, 7, 9, 10 }),
        new("maj13", new[] { 0, 2, 4, 5, 7, 9, 11 }),
        new("m13", new[] { 0, 2, 3, 5, 7, 9, 10 }),
        new("7sus4", new[] { 0, 5, 7, 10 })
    };

    public static IReadOnlyList<MusicScaleDefinition> ScaleDefinitions
        => ScaleDefinitionArray;

    public static string GetScaleDisplayName(MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        return AnalyzeKeyAt(score, 0d)?.DisplayName ?? string.Empty;
    }

    public static MusicKeyAnalysis? AnalyzeKeyAt(MusicScore score, double beat)
    {
        ArgumentNullException.ThrowIfNull(score);

        var explicitKey = score.KeySignatureEvents
            .Where(item => item.Beat <= beat + ScoreTiming.EventBeatTolerance)
            .OrderByDescending(item => item.Beat)
            .FirstOrDefault();
        if (explicitKey is not null)
        {
            var definition = ResolveScaleDefinition(explicitKey);
            if (definition is not null)
            {
                return CreateKeyAnalysis(
                    explicitKey.Fifths,
                    definition,
                    explicitKey.Mode ?? definition.MusicXmlMode,
                    isInferred: false);
            }

            var inferredMode = InferModeForKeySignature(
                score,
                explicitKey.Fifths);
            var inferredDefinition = GetScaleDefinition(inferredMode)
                ?? GetScaleDefinition("major")!;
            return CreateKeyAnalysis(
                explicitKey.Fifths,
                inferredDefinition,
                inferredDefinition.MusicXmlMode,
                isInferred: true);
        }

        var candidates = CreateScaleCandidates(score, 1);
        if (candidates.Count == 0)
        {
            return null;
        }

        var candidate = candidates[0];

        var inferredScale = GetScaleDefinition(candidate.ScaleId);
        return inferredScale is null
            ? null
            : CreateKeyAnalysis(
                candidate.Fifths,
                inferredScale,
                inferredScale.MusicXmlMode,
                isInferred: true);
    }

    public static MusicScaleDefinition? GetScaleDefinition(string? scaleId)
    {
        if (string.IsNullOrWhiteSpace(scaleId))
        {
            return null;
        }

        var normalized = scaleId.Trim().ToLowerInvariant();
        return ScaleDefinitionArray.FirstOrDefault(item =>
            string.Equals(item.Id, normalized, StringComparison.Ordinal));
    }

    public static string GetMusicXmlMode(string scaleId)
        => GetScaleDefinition(scaleId)?.MusicXmlMode ?? "none";

    public static string GetScaleDisplayName(int fifths, string scaleId)
    {
        var definition = GetScaleDefinition(scaleId)
            ?? throw new ArgumentException(
                "Unsupported scale type.",
                nameof(scaleId));
        var root = GetScaleRootPitchClass(fifths, definition);
        var preferFlats = fifths < 0;
        return $"{GetPitchName(root, preferFlats)} {definition.DisplayName}";
    }

    public static string GetKeyDisplayName(int fifths, string mode)
    {
        fifths = Math.Clamp(fifths, -7, 7);
        var normalized = NormalizeMode(mode);
        if (normalized is "major" or "ionian")
        {
            return $"{MajorNamesByFifths[fifths + 7]} major";
        }

        if (normalized is "minor" or "aeolian")
        {
            return $"{MinorNamesByFifths[fifths + 7]} minor";
        }

        var definition = GetScaleDefinition(normalized);
        return definition is null
            ? $"key {fifths:+#;-#;0} / {mode}"
            : GetScaleDisplayName(fifths, definition.Id);
    }

    public static IReadOnlyList<int> GetScalePitchClasses(
        int fifths,
        string scaleId)
    {
        var definition = GetScaleDefinition(scaleId);
        if (definition is null)
        {
            return Array.Empty<int>();
        }

        var root = GetScaleRootPitchClass(fifths, definition);
        return definition.Intervals
            .Select(interval => PositiveModulo(root + interval, 12))
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
    }

    public static int GetScaleRootPitchClass(
        int fifths,
        string scaleId)
    {
        var definition = GetScaleDefinition(scaleId)
            ?? throw new ArgumentException(
                "Unsupported scale type.",
                nameof(scaleId));
        return GetScaleRootPitchClass(fifths, definition);
    }

    public static IReadOnlyList<MusicScaleCandidate> CreateScaleCandidates(
        MusicScore score,
        int maximumCount = 6)
    {
        ArgumentNullException.ThrowIfNull(score);
        maximumCount = Math.Max(1, maximumCount);

        var weights = BuildPitchClassWeights(score);
        var totalWeight = weights.Sum();
        if (totalWeight <= ScoreTiming.EventBeatTolerance)
        {
            return Array.Empty<MusicScaleCandidate>();
        }

        var byScaleAndRoot = new Dictionary<(string ScaleId, int Root), MusicScaleCandidate>();
        foreach (var definition in ScaleDefinitionArray.Where(item => item.IncludeInSuggestions))
        {
            for (var fifths = -7; fifths <= 7; fifths++)
            {
                var root = GetScaleRootPitchClass(fifths, definition);
                var pitchClasses = definition.Intervals
                    .Select(interval => PositiveModulo(root + interval, 12))
                    .Distinct()
                    .ToHashSet();
                var matchedWeight = weights
                    .Where((weight, pitchClass) => pitchClasses.Contains(pitchClass))
                    .Sum();
                var coverage = matchedWeight / totalWeight;
                var tonicShare = weights[root] / totalWeight;
                var outsidePenalty = (1d - coverage) * 0.85d;
                var broadScalePenalty = Math.Max(0, pitchClasses.Count - 7) * 0.03d;
                var diatonicBonus = definition.IsDiatonic ? 0.04d : 0d;
                var scoreValue =
                    coverage
                    - outsidePenalty
                    - broadScalePenalty
                    + tonicShare * 0.08d
                    + diatonicBonus;

                var candidate = new MusicScaleCandidate(
                    fifths,
                    definition.Id,
                    GetScaleDisplayName(fifths, definition.Id),
                    scoreValue);
                var key = (definition.Id, root);
                if (!byScaleAndRoot.TryGetValue(key, out var existing)
                    || Math.Abs(fifths) < Math.Abs(existing.Fifths))
                {
                    byScaleAndRoot[key] = candidate;
                }
            }
        }

        return byScaleAndRoot.Values
            .OrderByDescending(item => item.Score)
            .ThenBy(item =>
                GetScaleDefinition(item.ScaleId)?.IsDiatonic == true
                    ? 0
                    : 1)
            .ThenBy(item => Math.Abs(item.Fifths))
            .ThenBy(item => item.Label, StringComparer.Ordinal)
            .Take(maximumCount)
            .ToArray();
    }

    public static IReadOnlyList<EditorScaleGuideRegion> CreateScaleGuideRegions(
        MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        var length = Math.Max(0d, score.LengthBeats);
        if (length <= ScoreTiming.EventBeatTolerance)
        {
            return Array.Empty<EditorScaleGuideRegion>();
        }

        var keyEvents = score.KeySignatureEvents
            .OrderBy(item => item.Beat)
            .ToArray();
        var result = new List<EditorScaleGuideRegion>();

        if (keyEvents.Length == 0)
        {
            var inferred = AnalyzeKeyAt(score, 0d);
            if (inferred is not null)
            {
                result.Add(new EditorScaleGuideRegion(
                    0d,
                    length,
                    inferred.RootPitchClass,
                    inferred.PitchClasses));
            }

            return result;
        }

        if (keyEvents[0].Beat > ScoreTiming.EventBeatTolerance)
        {
            var candidates = CreateScaleCandidates(score, 1);
            if (candidates.Count > 0)
            {
                var inferredBeforeFirst = candidates[0];
                var definition = GetScaleDefinition(inferredBeforeFirst.ScaleId)!;
                result.Add(new EditorScaleGuideRegion(
                    0d,
                    Math.Min(length, keyEvents[0].Beat),
                    GetScaleRootPitchClass(
                        inferredBeforeFirst.Fifths,
                        definition),
                    GetScalePitchClasses(
                        inferredBeforeFirst.Fifths,
                        inferredBeforeFirst.ScaleId)));
            }
        }

        for (var index = 0; index < keyEvents.Length; index++)
        {
            var start = Math.Clamp(keyEvents[index].Beat, 0d, length);
            var end = index + 1 < keyEvents.Length
                ? Math.Clamp(keyEvents[index + 1].Beat, start, length)
                : length;
            if (end - start <= ScoreTiming.EventBeatTolerance)
            {
                continue;
            }

            var analysis = AnalyzeKeyAt(score, start);
            if (analysis is null)
            {
                continue;
            }

            result.Add(new EditorScaleGuideRegion(
                start,
                end,
                analysis.RootPitchClass,
                analysis.PitchClasses));
        }

        return result;
    }

    public static IReadOnlyDictionary<int, string> CreateMeasureHarmonyLabels(
        MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        return CreateMeasureHarmonyAnalyses(score)
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value.IsInferred
                    ? pair.Value.Symbol + "?"
                    : pair.Value.Symbol);
    }

    public static IReadOnlyList<HarmonyTimelineSegment> CreateHarmonyTimeline(
        MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);

        var scoreEnd = Math.Max(0d, score.LengthBeats);
        if (scoreEnd <= ScoreTiming.EventBeatTolerance)
        {
            return Array.Empty<HarmonyTimelineSegment>();
        }

        var result = new List<HarmonyTimelineSegment>();
        var explicitHarmony = score.HarmonyEvents
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Symbol)
                && item.Beat < scoreEnd - ScoreTiming.EventBeatTolerance)
            .OrderBy(item => item.Beat)
            .ToArray();

        if (explicitHarmony.Length == 0)
        {
            foreach (var measure in score.Measures)
            {
                AddInferredHarmonySegment(
                    result,
                    score,
                    measure.StartBeat,
                    measure.EndBeat);
            }

            return result;
        }

        var firstExplicitBeat = Math.Clamp(
            explicitHarmony[0].Beat,
            0d,
            scoreEnd);
        if (firstExplicitBeat > ScoreTiming.EventBeatTolerance)
        {
            foreach (var measure in score.Measures)
            {
                var startBeat = Math.Max(0d, measure.StartBeat);
                var endBeat = Math.Min(
                    firstExplicitBeat,
                    measure.EndBeat);
                AddInferredHarmonySegment(
                    result,
                    score,
                    startBeat,
                    endBeat);
            }
        }

        for (var index = 0; index < explicitHarmony.Length; index++)
        {
            var startBeat = Math.Clamp(
                explicitHarmony[index].Beat,
                0d,
                scoreEnd);
            var endBeat = index + 1 < explicitHarmony.Length
                ? Math.Clamp(
                    explicitHarmony[index + 1].Beat,
                    0d,
                    scoreEnd)
                : scoreEnd;
            if (endBeat - startBeat <= ScoreTiming.EventBeatTolerance)
            {
                continue;
            }

            _ = TryGetChordPitchClasses(
                explicitHarmony[index].Symbol,
                out var pitchClasses);
            result.Add(new HarmonyTimelineSegment(
                startBeat,
                endBeat,
                explicitHarmony[index].Symbol.Trim(),
                IsInferred: false,
                Confidence: 1d,
                pitchClasses));
        }

        return result;
    }

    public static IReadOnlyDictionary<int, MeasureHarmonyAnalysis>
        CreateMeasureHarmonyAnalyses(MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);

        var timeline = CreateHarmonyTimeline(score);
        var analyses = new Dictionary<int, MeasureHarmonyAnalysis>();
        foreach (var measure in score.Measures)
        {
            var segments = timeline
                .Where(item =>
                    item.EndBeat > measure.StartBeat + ScoreTiming.EventBeatTolerance
                    && item.StartBeat < measure.EndBeat - ScoreTiming.EventBeatTolerance)
                .OrderBy(item => item.StartBeat)
                .ToArray();
            if (segments.Length == 0)
            {
                continue;
            }

            var symbols = new List<string>();
            foreach (var segment in segments)
            {
                if (symbols.Count == 0
                    || !string.Equals(
                        symbols[^1],
                        segment.Symbol,
                        StringComparison.Ordinal))
                {
                    symbols.Add(segment.Symbol);
                }
            }

            var weightedConfidence = 0d;
            var totalDuration = 0d;
            var pitchClasses = new HashSet<int>();
            foreach (var segment in segments)
            {
                var overlapStart = Math.Max(
                    measure.StartBeat,
                    segment.StartBeat);
                var overlapEnd = Math.Min(
                    measure.EndBeat,
                    segment.EndBeat);
                var duration = Math.Max(0d, overlapEnd - overlapStart);
                weightedConfidence += segment.Confidence * duration;
                totalDuration += duration;
                foreach (var pitchClass in segment.PitchClasses)
                {
                    pitchClasses.Add(pitchClass);
                }
            }

            analyses[measure.Number] = new MeasureHarmonyAnalysis(
                measure.Number,
                Math.Max(measure.StartBeat, segments[0].StartBeat),
                string.Join(" → ", symbols),
                segments.All(item => item.IsInferred),
                totalDuration > ScoreTiming.EventBeatTolerance
                    ? weightedConfidence / totalDuration
                    : segments[0].Confidence,
                pitchClasses.OrderBy(value => value).ToArray());
        }

        return analyses;
    }

    public static IReadOnlyList<EditorChordGuideRegion> CreateChordGuideRegions(
        MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);

        return CreateHarmonyTimeline(score)
            .Where(item => item.PitchClasses.Count > 0)
            .Select(item => new EditorChordGuideRegion(
                item.StartBeat,
                item.EndBeat,
                item.PitchClasses))
            .ToArray();
    }

    public static HarmonyTimelineSegment? GetHarmonySegmentAt(
        MusicScore score,
        double beat)
    {
        ArgumentNullException.ThrowIfNull(score);

        var boundedBeat = Math.Clamp(
            beat,
            0d,
            Math.Max(0d, score.LengthBeats));
        var timeline = CreateHarmonyTimeline(score);
        foreach (var item in timeline)
        {
            if (boundedBeat >= item.StartBeat - ScoreTiming.EventBeatTolerance
                && (boundedBeat < item.EndBeat - ScoreTiming.EventBeatTolerance
                    || Math.Abs(boundedBeat - item.EndBeat) <= ScoreTiming.EventBeatTolerance
                        && Math.Abs(item.EndBeat - score.LengthBeats)
                            <= ScoreTiming.EventBeatTolerance))
            {
                return item;
            }
        }

        return null;
    }

    public static MeasureHarmonyAnalysis? GetHarmonyAt(
        MusicScore score,
        double beat)
    {
        ArgumentNullException.ThrowIfNull(score);

        var measure = score.GetMeasureAt(beat);
        var segment = GetHarmonySegmentAt(score, beat);
        if (measure is null || segment is null)
        {
            return null;
        }

        return new MeasureHarmonyAnalysis(
            measure.Number,
            segment.StartBeat,
            segment.Symbol,
            segment.IsInferred,
            segment.Confidence,
            segment.PitchClasses);
    }

    public static IReadOnlyList<string> CreateChordSuggestions(
        MusicScore score,
        ScoreMeasure measure,
        int maximumCount = 12)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(measure);
        maximumCount = Math.Max(1, maximumCount);

        var suggestions = new List<string>();
        var explicitSymbols = score.HarmonyEvents
            .Where(item =>
                item.Beat >= measure.StartBeat - ScoreTiming.EventBeatTolerance
                && item.Beat < measure.EndBeat - ScoreTiming.EventBeatTolerance)
            .Select(item => item.Symbol.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item));
        suggestions.AddRange(explicitSymbols);

        var weights = BuildMeasurePitchClassWeights(score, measure);
        var totalWeight = weights.Sum();
        if (totalWeight > ScoreTiming.EventBeatTolerance)
        {
            var preferFlats = score.GetKeyFifthsAt(measure.StartBeat) is int fifths
                && fifths < 0;
            var candidates = EvaluateChordCandidates(weights, totalWeight)
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.Coverage)
                .Take(maximumCount * 2)
                .Select(item =>
                    GetPitchName(item.RootPitchClass, preferFlats)
                    + item.Suffix);
            suggestions.AddRange(candidates);
        }

        return suggestions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maximumCount)
            .ToArray();
    }

    public static bool IsValidChordSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        var primarySymbol = symbol.Trim();
        var slashParts = primarySymbol.Split(
            '/',
            2,
            StringSplitOptions.TrimEntries);
        if (!TryParseChordRoot(
                slashParts[0],
                out _,
                out _))
        {
            return false;
        }

        if (slashParts.Length == 1)
        {
            return true;
        }

        return TryParseChordRoot(
                slashParts[1],
                out _,
                out var bassSuffix)
            && string.IsNullOrEmpty(bassSuffix);
    }

    public static bool TryGetChordPitchClasses(
        string symbol,
        out IReadOnlyList<int> pitchClasses)
    {
        pitchClasses = Array.Empty<int>();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        var primarySymbol = symbol.Split(
            '→',
            2,
            StringSplitOptions.TrimEntries)[0];
        var slashParts = primarySymbol.Split(
            '/',
            2,
            StringSplitOptions.TrimEntries);
        if (!TryParseChordRoot(
                slashParts[0],
                out var rootPitchClass,
                out var suffix))
        {
            return false;
        }

        var template = ChordTemplates.FirstOrDefault(item =>
            string.Equals(
                item.Suffix,
                suffix,
                StringComparison.OrdinalIgnoreCase));
        if (template is null)
        {
            return false;
        }

        var result = template.Intervals
            .Select(interval => PositiveModulo(rootPitchClass + interval, 12))
            .ToHashSet();
        if (slashParts.Length == 2
            && TryParseChordRoot(
                slashParts[1],
                out var bassPitchClass,
                out var bassSuffix)
            && string.IsNullOrEmpty(bassSuffix))
        {
            result.Add(bassPitchClass);
        }

        pitchClasses = result.OrderBy(value => value).ToArray();
        return pitchClasses.Count > 0;
    }

    private static MusicScaleDefinition? ResolveScaleDefinition(
        ScoreKeySignatureEvent keyEvent)
    {
        var explicitScale = GetScaleDefinition(keyEvent.ScaleType);
        if (explicitScale is not null)
        {
            return explicitScale;
        }

        var mode = NormalizeMode(keyEvent.Mode);
        return mode switch
        {
            "major" or "ionian" => GetScaleDefinition("major"),
            "minor" or "aeolian" => GetScaleDefinition("minor"),
            "dorian" => GetScaleDefinition("dorian"),
            "phrygian" => GetScaleDefinition("phrygian"),
            "lydian" => GetScaleDefinition("lydian"),
            "mixolydian" => GetScaleDefinition("mixolydian"),
            "locrian" => GetScaleDefinition("locrian"),
            _ => null
        };
    }

    private static MusicKeyAnalysis CreateKeyAnalysis(
        int fifths,
        MusicScaleDefinition definition,
        string mode,
        bool isInferred)
    {
        fifths = Math.Clamp(fifths, -7, 7);
        var root = GetScaleRootPitchClass(fifths, definition);
        var display = GetScaleDisplayName(fifths, definition.Id)
            + (isInferred ? " (推定)" : string.Empty);
        return new MusicKeyAnalysis(
            fifths,
            mode,
            definition.Id,
            display,
            isInferred,
            root,
            GetScalePitchClasses(fifths, definition.Id));
    }

    private static int GetScaleRootPitchClass(
        int fifths,
        MusicScaleDefinition definition)
        => PositiveModulo(
            FifthsToMajorPitchClass(fifths)
            + definition.TonicOffsetFromMajor,
            12);

    private static InferredChord? InferChordForMeasure(
        MusicScore score,
        ScoreMeasure measure)
        => InferChordForRange(
            score,
            measure.StartBeat,
            measure.EndBeat);

    private static InferredChord? InferChordForRange(
        MusicScore score,
        double startBeat,
        double endBeat)
    {
        if (endBeat - startBeat <= ScoreTiming.EventBeatTolerance)
        {
            return null;
        }

        var weights = BuildPitchClassWeights(
            score,
            startBeat,
            endBeat);
        var totalWeight = weights.Sum();
        if (totalWeight <= ScoreTiming.EventBeatTolerance)
        {
            return null;
        }

        var best = EvaluateChordCandidates(weights, totalWeight)
            .OrderByDescending(item => item.Score)
            .FirstOrDefault();
        if (best is null || best.Coverage < 0.72d)
        {
            return null;
        }

        var preferFlats = score.GetKeyFifthsAt(startBeat) is int fifths
            && fifths < 0;
        var symbol = GetPitchName(best.RootPitchClass, preferFlats)
            + best.Suffix;
        var pitchClasses = best.Intervals
            .Select(interval =>
                PositiveModulo(best.RootPitchClass + interval, 12))
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        return new InferredChord(
            symbol,
            best.Coverage,
            pitchClasses);
    }

    private static void AddInferredHarmonySegment(
        ICollection<HarmonyTimelineSegment> result,
        MusicScore score,
        double startBeat,
        double endBeat)
    {
        if (endBeat - startBeat <= ScoreTiming.EventBeatTolerance)
        {
            return;
        }

        var inferred = InferChordForRange(
            score,
            startBeat,
            endBeat);
        if (inferred is null)
        {
            return;
        }

        result.Add(new HarmonyTimelineSegment(
            startBeat,
            endBeat,
            inferred.Value.Symbol,
            IsInferred: true,
            inferred.Value.Coverage,
            inferred.Value.PitchClasses));
    }

    private static IReadOnlyList<ChordCandidate> EvaluateChordCandidates(
        IReadOnlyList<double> weights,
        double totalWeight)
    {
        var distinctPitchClasses = weights.Count(weight =>
            weight > ScoreTiming.EventBeatTolerance);
        if (distinctPitchClasses < 2)
        {
            return Array.Empty<ChordCandidate>();
        }

        var result = new List<ChordCandidate>();
        for (var root = 0; root < 12; root++)
        {
            foreach (var template in ChordTemplates)
            {
                var matchedWeight = 0d;
                var matchedToneCount = 0;
                foreach (var interval in template.Intervals)
                {
                    var pitchClass = PositiveModulo(root + interval, 12);
                    var weight = weights[pitchClass];
                    matchedWeight += weight;
                    if (weight > ScoreTiming.EventBeatTolerance)
                    {
                        matchedToneCount++;
                    }
                }

                var distinctTemplateToneCount = template.Intervals
                    .Select(interval => PositiveModulo(interval, 12))
                    .Distinct()
                    .Count();
                var minimumToneCount = distinctTemplateToneCount >= 3
                    ? Math.Min(3, distinctTemplateToneCount)
                    : distinctTemplateToneCount;
                if (matchedToneCount < minimumToneCount)
                {
                    continue;
                }

                var coverage = matchedWeight / totalWeight;
                var rootShare = weights[root] / totalWeight;
                var complexityPenalty =
                    Math.Max(0, distinctTemplateToneCount - 4) * 0.012d;
                var scoreValue =
                    coverage
                    + rootShare * 0.15d
                    - complexityPenalty;
                result.Add(new ChordCandidate(
                    root,
                    template.Suffix,
                    coverage,
                    scoreValue,
                    template.Intervals));
            }
        }

        return result;
    }

    private static double[] BuildMeasurePitchClassWeights(
        MusicScore score,
        ScoreMeasure measure)
        => BuildPitchClassWeights(
            score,
            measure.StartBeat,
            measure.EndBeat);

    private static double[] BuildPitchClassWeights(
        MusicScore score,
        double startBeat,
        double endBeat)
    {
        var weights = new double[12];
        foreach (var note in score.Notes)
        {
            var overlapStart = Math.Max(note.StartBeat, startBeat);
            var overlapEnd = Math.Min(note.EndBeat, endBeat);
            var overlap = overlapEnd - overlapStart;
            if (overlap <= ScoreTiming.EventBeatTolerance)
            {
                continue;
            }

            weights[PositiveModulo(note.MidiNote, 12)] += overlap;
        }

        return weights;
    }

    private static IReadOnlyList<int> GetPitchClassesForSymbols(
        IReadOnlyList<string> symbols)
    {
        var pitchClasses = new HashSet<int>();
        foreach (var symbol in symbols)
        {
            if (!TryGetChordPitchClasses(
                    symbol,
                    out var chordPitchClasses))
            {
                continue;
            }

            foreach (var pitchClass in chordPitchClasses)
            {
                pitchClasses.Add(pitchClass);
            }
        }

        return pitchClasses.OrderBy(value => value).ToArray();
    }

    private static bool TryParseChordRoot(
        string value,
        out int rootPitchClass,
        out string suffix)
    {
        rootPitchClass = 0;
        suffix = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var natural = char.ToUpperInvariant(text[0]) switch
        {
            'C' => 0,
            'D' => 2,
            'E' => 4,
            'F' => 5,
            'G' => 7,
            'A' => 9,
            'B' => 11,
            _ => -1
        };
        if (natural < 0)
        {
            return false;
        }

        var index = 1;
        var alter = 0;
        while (index < text.Length && text[index] is '#' or 'b')
        {
            alter += text[index] == '#' ? 1 : -1;
            index++;
        }

        rootPitchClass = PositiveModulo(natural + alter, 12);
        suffix = text[index..];
        return true;
    }

    private static string InferModeForKeySignature(
        MusicScore score,
        int fifths)
    {
        var majorRoot = FifthsToMajorPitchClass(fifths);
        var minorRoot = PositiveModulo(majorRoot + 9, 12);
        var weights = BuildPitchClassWeights(score);
        var majorScore = CalculateProfileScore(
            weights,
            majorRoot,
            MajorProfile);
        var minorScore = CalculateProfileScore(
            weights,
            minorRoot,
            MinorProfile);
        return minorScore > majorScore ? "minor" : "major";
    }

    private static double[] BuildPitchClassWeights(MusicScore score)
    {
        var weights = new double[12];
        foreach (var note in score.Notes)
        {
            weights[PositiveModulo(note.MidiNote, 12)] += Math.Max(
                note.DurationBeat,
                ScoreTiming.EventBeatTolerance);
        }

        return weights;
    }

    private static double CalculateProfileScore(
        IReadOnlyList<double> weights,
        int rootPitchClass,
        IReadOnlyList<double> profile)
    {
        var score = 0d;
        for (var pitchClass = 0; pitchClass < 12; pitchClass++)
        {
            var relative = PositiveModulo(
                pitchClass - rootPitchClass,
                12);
            score += weights[pitchClass] * profile[relative];
        }

        return score;
    }

    private static int FifthsToMajorPitchClass(int fifths)
        => PositiveModulo(fifths * 7, 12);

    private static string? NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return null;
        }

        var normalized = mode.Trim().ToLowerInvariant();
        return normalized switch
        {
            "major" => "major",
            "minor" => "minor",
            "ionian" => "ionian",
            "dorian" => "dorian",
            "phrygian" => "phrygian",
            "lydian" => "lydian",
            "mixolydian" => "mixolydian",
            "aeolian" => "aeolian",
            "locrian" => "locrian",
            "none" => "none",
            _ => normalized
        };
    }

    private static string GetPitchName(
        int pitchClass,
        bool preferFlats)
    {
        var normalized = PositiveModulo(pitchClass, 12);
        return preferFlats
            ? FlatPitchNames[normalized]
            : SharpPitchNames[normalized];
    }

    private static int PositiveModulo(int value, int modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private sealed record ChordTemplate(
        string Suffix,
        IReadOnlyList<int> Intervals);

    private sealed record ChordCandidate(
        int RootPitchClass,
        string Suffix,
        double Coverage,
        double Score,
        IReadOnlyList<int> Intervals);

    private readonly record struct InferredChord(
        string Symbol,
        double Coverage,
        IReadOnlyList<int> PitchClasses);
}
