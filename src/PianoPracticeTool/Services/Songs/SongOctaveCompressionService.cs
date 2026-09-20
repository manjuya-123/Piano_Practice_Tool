using PianoPracticeTool.Core;

namespace PianoPracticeTool.Services.Songs;

public sealed record SongOctaveCompressionAnalysis(
    bool IsNeeded,
    bool CanCompress,
    int ChangedNoteCount,
    int OriginalLowestMidi,
    int OriginalHighestMidi,
    int CompressedLowestMidi,
    int CompressedHighestMidi,
    string Message);

public static class SongOctaveCompressionService
{
    public static SongOctaveCompressionAnalysis Analyze(
        MusicScore score,
        int keyboardLowestMidi,
        int keyboardHighestMidi)
    {
        ArgumentNullException.ThrowIfNull(score);
        ValidateKeyboardRange(keyboardLowestMidi, keyboardHighestMidi);

        if (score.Notes.Count == 0)
        {
            return new SongOctaveCompressionAnalysis(
                IsNeeded: false,
                CanCompress: false,
                ChangedNoteCount: 0,
                OriginalLowestMidi: 60,
                OriginalHighestMidi: 60,
                CompressedLowestMidi: 60,
                CompressedHighestMidi: 60,
                Message: "ノートがないため楽曲圧縮は不要です。");
        }

        var originalLowest = score.MinMidiNote;
        var originalHighest = score.MaxMidiNote;
        var isNeeded =
            originalLowest < keyboardLowestMidi
            || originalHighest > keyboardHighestMidi;
        if (!isNeeded)
        {
            return new SongOctaveCompressionAnalysis(
                IsNeeded: false,
                CanCompress: false,
                ChangedNoteCount: 0,
                OriginalLowestMidi: originalLowest,
                OriginalHighestMidi: originalHighest,
                CompressedLowestMidi: originalLowest,
                CompressedHighestMidi: originalHighest,
                Message: "現在の鍵盤範囲に収まるため楽曲圧縮は不要です。");
        }

        if (!TryBuildCompressedPitches(
                score,
                keyboardLowestMidi,
                keyboardHighestMidi,
                out var compressedPitches))
        {
            return new SongOctaveCompressionAnalysis(
                IsNeeded: true,
                CanCompress: false,
                ChangedNoteCount: 0,
                OriginalLowestMidi: originalLowest,
                OriginalHighestMidi: originalHighest,
                CompressedLowestMidi: originalLowest,
                CompressedHighestMidi: originalHighest,
                Message: "この曲は、和音の配置を保ったまま現在の鍵盤範囲へ圧縮できません。");
        }

        var compressedLowest = compressedPitches.Values.Min();
        var compressedHighest = compressedPitches.Values.Max();
        var changedCount = score.Notes.Count(note =>
            compressedPitches[note.Id] != note.MidiNote);

        return new SongOctaveCompressionAnalysis(
            IsNeeded: true,
            CanCompress: changedCount > 0,
            ChangedNoteCount: changedCount,
            OriginalLowestMidi: originalLowest,
            OriginalHighestMidi: originalHighest,
            CompressedLowestMidi: compressedLowest,
            CompressedHighestMidi: compressedHighest,
            Message: changedCount > 0
                ? $"{changedCount:N0}音をオクターブ単位で移動すると、現在の鍵盤範囲で演奏できます。"
                : "楽曲圧縮による改善はありません。");
    }

    public static MusicScore Compress(
        MusicScore score,
        int keyboardLowestMidi,
        int keyboardHighestMidi)
    {
        ArgumentNullException.ThrowIfNull(score);
        ValidateKeyboardRange(keyboardLowestMidi, keyboardHighestMidi);

        if (!TryBuildCompressedPitches(
                score,
                keyboardLowestMidi,
                keyboardHighestMidi,
                out var compressedPitches))
        {
            throw new InvalidOperationException(
                "この曲は、和音の配置を保ったまま現在の鍵盤範囲へ圧縮できません。");
        }

        var notes = score.Notes
            .Select(note => new ScoreNote
            {
                Id = note.Id,
                MidiNote = compressedPitches[note.Id],
                StartBeat = note.StartBeat,
                DurationBeat = note.DurationBeat,
                Staff = note.Staff,
                Voice = note.Voice,
                Hand = note.Hand,
                Finger = 0
            })
            .ToArray();

        var compressed = new MusicScore
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

        FingeringGenerator.Generate(compressed);
        return compressed;
    }

    private static bool TryBuildCompressedPitches(
        MusicScore score,
        int keyboardLowestMidi,
        int keyboardHighestMidi,
        out IReadOnlyDictionary<int, int> compressedPitches)
    {
        var result = new Dictionary<int, int>();
        foreach (var note in score.Notes)
        {
            var mapped = FindNearestPitchClassInRange(
                note.MidiNote,
                keyboardLowestMidi,
                keyboardHighestMidi);
            if (mapped is null)
            {
                compressedPitches =
                    new Dictionary<int, int>();
                return false;
            }

            result[note.Id] = mapped.Value;
        }

        foreach (var onsetGroup in score.Notes.GroupBy(note =>
                     Math.Round(
                         note.StartBeat
                         / ScoreTiming.BeatGroupingTolerance)))
        {
            var mappedAtOnset = onsetGroup
                .Select(note => result[note.Id])
                .ToArray();
            if (mappedAtOnset.Distinct().Count() != mappedAtOnset.Length)
            {
                compressedPitches =
                    new Dictionary<int, int>();
                return false;
            }

            foreach (var handGroup in onsetGroup.GroupBy(note => note.Hand))
            {
                var ordered = handGroup
                    .OrderBy(note => note.MidiNote)
                    .ToArray();
                for (var index = 1; index < ordered.Length; index++)
                {
                    if (result[ordered[index - 1].Id]
                        >= result[ordered[index].Id])
                    {
                        compressedPitches =
                            new Dictionary<int, int>();
                        return false;
                    }
                }
            }
        }

        compressedPitches = result;
        return true;
    }

    private static int? FindNearestPitchClassInRange(
        int midiNote,
        int keyboardLowestMidi,
        int keyboardHighestMidi)
    {
        if (midiNote >= keyboardLowestMidi
            && midiNote <= keyboardHighestMidi)
        {
            return midiNote;
        }

        var pitchClass = PositiveModulo(midiNote, 12);
        int? best = null;
        var bestDistance = int.MaxValue;
        for (var candidate = keyboardLowestMidi;
             candidate <= keyboardHighestMidi;
             candidate++)
        {
            if (PositiveModulo(candidate, 12) != pitchClass)
            {
                continue;
            }

            var distance = Math.Abs(candidate - midiNote);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static int PositiveModulo(int value, int modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static void ValidateKeyboardRange(
        int keyboardLowestMidi,
        int keyboardHighestMidi)
    {
        if (keyboardLowestMidi is < 0 or > 127
            || keyboardHighestMidi is < 0 or > 127
            || keyboardLowestMidi > keyboardHighestMidi)
        {
            throw new ArgumentOutOfRangeException(
                nameof(keyboardLowestMidi),
                "MIDI鍵盤範囲が不正です。");
        }
    }
}
