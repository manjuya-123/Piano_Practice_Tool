namespace PianoPracticeTool.Core;

public sealed record KeyboardLayout(
    int KeyCount,
    int LowestMidi,
    int HighestMidi,
    string Name);

public sealed record KeyboardRecommendation(
    int PitchSpan,
    int MinimumStandardKeyCount,
    int ComfortableStandardKeyCount,
    int LowestMidi,
    int HighestMidi,
    bool HasWideHandSpanWarning,
    int SuggestedTransposeSemitones,
    bool CanReduceWithOctaveShift,
    string MinimumLayoutName,
    string RecommendedLayoutName);

public static class KeyboardRangeAnalyzer
{
    private const int MaximumComfortableChordSpanSemitones = 12;

    private static readonly IReadOnlyList<KeyboardLayout> CommonLayouts = new[]
    {
        new KeyboardLayout(25, 48, 72, "25鍵 C3–C5"),
        new KeyboardLayout(32, 41, 72, "32鍵 F2–C5"),
        new KeyboardLayout(37, 36, 72, "37鍵 C2–C5"),
        new KeyboardLayout(49, 36, 84, "49鍵 C2–C6"),
        new KeyboardLayout(61, 36, 96, "61鍵 C2–C7"),
        new KeyboardLayout(76, 28, 103, "76鍵 E1–G7"),
        new KeyboardLayout(88, 21, 108, "88鍵 A0–C8")
    };

    public static KeyboardRecommendation Analyze(MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);

        if (score.Notes.Count == 0)
        {
            var smallest = CommonLayouts[0];
            return new KeyboardRecommendation(
                0,
                smallest.KeyCount,
                smallest.KeyCount,
                60,
                60,
                false,
                0,
                false,
                smallest.Name,
                smallest.Name);
        }

        var lowestMidi = score.MinMidiNote;
        var highestMidi = score.MaxMidiNote;
        var pitchSpan = highestMidi - lowestMidi + 1;
        var hasWideHandSpan = HasUncomfortableSameHandChord(score);

        var directLayout = CommonLayouts.FirstOrDefault(layout => Fits(layout, lowestMidi, highestMidi))
            ?? CommonLayouts[^1];

        var shiftedLayout = directLayout;
        var suggestedShift = 0;
        if (!hasWideHandSpan)
        {
            shiftedLayout = FindSmallestLayoutWithOctaveShift(lowestMidi, highestMidi, out suggestedShift)
                ?? directLayout;
        }

        var canReduce = !hasWideHandSpan
            && shiftedLayout.KeyCount < directLayout.KeyCount
            && suggestedShift != 0
            && IsShiftSafe(score, shiftedLayout, suggestedShift);

        if (!canReduce)
        {
            shiftedLayout = directLayout;
            suggestedShift = 0;
        }

        return new KeyboardRecommendation(
            pitchSpan,
            shiftedLayout.KeyCount,
            directLayout.KeyCount,
            lowestMidi,
            highestMidi,
            hasWideHandSpan,
            suggestedShift,
            canReduce,
            shiftedLayout.Name,
            directLayout.Name);
    }

    private static bool HasUncomfortableSameHandChord(MusicScore score)
    {
        return score.Notes
            .GroupBy(note => new
            {
                note.Hand,
                Beat = Math.Round(note.StartBeat / ScoreTiming.BeatGroupingTolerance)
            })
            .Any(group => group.Max(note => note.MidiNote) - group.Min(note => note.MidiNote)
                > MaximumComfortableChordSpanSemitones);
    }

    private static bool IsShiftSafe(MusicScore score, KeyboardLayout layout, int shift)
    {
        var shiftedLowest = layout.LowestMidi + shift;
        var shiftedHighest = layout.HighestMidi + shift;
        if (shiftedLowest < 0 || shiftedHighest > 127)
        {
            return false;
        }

        foreach (var note in score.Notes)
        {
            if (note.MidiNote < shiftedLowest || note.MidiNote > shiftedHighest)
            {
                return false;
            }
        }

        // One fixed hardware octave shift moves the whole physical keyboard range.
        // It never increases key count and never switches octaves during the piece.
        return true;
    }

    private static KeyboardLayout? FindSmallestLayoutWithOctaveShift(
        int lowestMidi,
        int highestMidi,
        out int suggestedShift)
    {
        foreach (var layout in CommonLayouts)
        {
            if (highestMidi - lowestMidi > layout.HighestMidi - layout.LowestMidi)
            {
                continue;
            }

            var shift = FindSmallestOctaveShift(layout, lowestMidi, highestMidi);
            if (shift is not null)
            {
                suggestedShift = shift.Value;
                return layout;
            }
        }

        suggestedShift = 0;
        return null;
    }

    private static int? FindSmallestOctaveShift(KeyboardLayout layout, int lowestMidi, int highestMidi)
    {
        int? bestShift = null;
        for (var shift = -120; shift <= 120; shift += 12)
        {
            var shiftedLowest = layout.LowestMidi + shift;
            var shiftedHighest = layout.HighestMidi + shift;
            if (shiftedLowest < 0 || shiftedHighest > 127)
            {
                continue;
            }

            if (!FitsRange(lowestMidi, highestMidi, shiftedLowest, shiftedHighest))
            {
                continue;
            }

            if (bestShift is null || Math.Abs(shift) < Math.Abs(bestShift.Value))
            {
                bestShift = shift;
            }
        }

        return bestShift;
    }

    private static bool Fits(KeyboardLayout layout, int lowestMidi, int highestMidi)
        => FitsRange(lowestMidi, highestMidi, layout.LowestMidi, layout.HighestMidi);

    private static bool FitsRange(int lowestMidi, int highestMidi, int rangeLowest, int rangeHighest)
        => lowestMidi >= rangeLowest && highestMidi <= rangeHighest;
}
