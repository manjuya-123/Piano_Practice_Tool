using System.Globalization;
using System.IO;
using System.Xml.Linq;
using PianoPracticeTool.Core;
using PianoPracticeTool.Core.Editing;

namespace PianoPracticeTool.Services.Editor;

internal static class MusicXmlRestVoiceMetadata
{
    private static readonly double BeatTolerance =
        1d / EditableMusicScore.TicksPerQuarter + ScoreTiming.EventBeatTolerance;

    public static MusicScore EnrichScore(string path, MusicScore score)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be null, empty, or whitespace.", nameof(path));
        }

        ArgumentNullException.ThrowIfNull(score);
        if (score.Rests.Count == 0)
        {
            return score;
        }

        var entries = ReadSourceRestVoices(path, score.Measures);
        if (entries.Count == 0)
        {
            return score;
        }

        var voicesByKey = entries
            .GroupBy(entry => new RestKey(entry.StartTick, entry.DurationTick, entry.Staff))
            .ToDictionary(
                group => group.Key,
                group => new Queue<string>(group.Select(entry => NormalizeVoice(entry.Voice))));

        var rests = new List<ScoreRest>(score.Rests.Count);
        foreach (var rest in score.Rests)
        {
            var key = new RestKey(
                EditableMusicScore.BeatToTick(rest.StartBeat),
                Math.Max(1L, EditableMusicScore.BeatToTick(rest.DurationBeat)),
                NormalizeStaff(rest.Staff));
            var voice = voicesByKey.TryGetValue(key, out var queue) && queue.Count > 0
                ? queue.Dequeue()
                : NormalizeVoice(rest.Voice);
            rests.Add(rest with { Voice = voice });
        }

        return new MusicScore
        {
            Title = score.Title,
            Composer = score.Composer,
            TempoBpm = score.TempoBpm,
            Notes = score.Notes,
            Rests = rests,
            TempoEvents = score.TempoEvents,
            KeySignatureEvents = score.KeySignatureEvents,
            HarmonyEvents = score.HarmonyEvents,
            Measures = score.Measures
        };
    }

    public static void ApplyToDocument(XDocument document, MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(score);

        var part = document.Root?
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "part")
            ?? throw new InvalidDataException("保存用MusicXMLにpart要素がありません。");
        var measureElements = part.Elements()
            .Where(element => element.Name.LocalName == "measure")
            .ToArray();
        var measures = GetEffectiveMeasures(score);
        if (measureElements.Length != measures.Count)
        {
            throw new InvalidDataException("休符voice保存時に小節数が一致しません。");
        }

        for (var measureIndex = 0; measureIndex < measures.Count; measureIndex++)
        {
            var expected = BuildExpectedSegments(score, measures[measureIndex]);
            var actual = measureElements[measureIndex]
                .Elements()
                .Where(element =>
                    element.Name.LocalName == "note"
                    && element.Elements().Any(child => child.Name.LocalName == "rest"))
                .ToArray();

            if (expected.Count != actual.Length)
            {
                throw new InvalidDataException("休符voice保存時に休符セグメント数が一致しません。");
            }

            for (var index = 0; index < expected.Count; index++)
            {
                SetVoice(actual[index], expected[index].Voice);
            }
        }
    }

    public static void ValidateRoundTrip(string path, MusicScore original)
    {
        ArgumentNullException.ThrowIfNull(original);
        var reloaded = EnrichScore(path, MusicXmlParser.Load(path));
        var expected = original.Rests
            .OrderBy(rest => rest.StartBeat)
            .ThenBy(rest => NormalizeStaff(rest.Staff))
            .ThenBy(rest => NormalizeVoice(rest.Voice), StringComparer.Ordinal)
            .ThenBy(rest => rest.DurationBeat)
            .ToArray();
        var actual = reloaded.Rests
            .OrderBy(rest => rest.StartBeat)
            .ThenBy(rest => NormalizeStaff(rest.Staff))
            .ThenBy(rest => NormalizeVoice(rest.Voice), StringComparer.Ordinal)
            .ThenBy(rest => rest.DurationBeat)
            .ToArray();

        if (expected.Length != actual.Length)
        {
            throw new InvalidDataException("保存後のMusicXMLを再読込した結果、休符数が一致しません。");
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (Math.Abs(expected[index].StartBeat - actual[index].StartBeat) > BeatTolerance
                || Math.Abs(expected[index].DurationBeat - actual[index].DurationBeat) > BeatTolerance
                || NormalizeStaff(expected[index].Staff) != NormalizeStaff(actual[index].Staff)
                || !string.Equals(
                    NormalizeVoice(expected[index].Voice),
                    NormalizeVoice(actual[index].Voice),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("保存後のMusicXMLを再読込した結果、休符voice情報が一致しません。");
            }
        }
    }

    private static IReadOnlyList<RestVoiceEntry> ReadSourceRestVoices(
        string path,
        IReadOnlyList<ScoreMeasure> parsedMeasures)
    {
        var document = XDocument.Load(path, LoadOptions.None);
        var part = document.Root?
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "part");
        if (part is null)
        {
            return Array.Empty<RestVoiceEntry>();
        }

        var measureElements = part.Elements()
            .Where(element => element.Name.LocalName == "measure")
            .ToArray();
        var result = new List<RestVoiceEntry>();
        var divisions = 1d;
        var fallbackMeasureStart = 0d;

        for (var measureIndex = 0; measureIndex < measureElements.Length; measureIndex++)
        {
            var measureStart = measureIndex < parsedMeasures.Count
                ? parsedMeasures[measureIndex].StartBeat
                : fallbackMeasureStart;
            var cursorBeat = 0d;
            var maxCursorBeat = 0d;

            foreach (var element in measureElements[measureIndex].Elements())
            {
                switch (element.Name.LocalName)
                {
                    case "attributes":
                        {
                            var divisionsText = DirectValue(element, "divisions");
                            if (double.TryParse(
                                    divisionsText,
                                    NumberStyles.Float,
                                    CultureInfo.InvariantCulture,
                                    out var nextDivisions)
                                && nextDivisions > 0d)
                            {
                                divisions = nextDivisions;
                            }

                            break;
                        }

                    case "backup":
                        cursorBeat = Math.Max(0d, cursorBeat - DurationBeat(element, divisions));
                        break;

                    case "forward":
                        cursorBeat += DurationBeat(element, divisions);
                        maxCursorBeat = Math.Max(maxCursorBeat, cursorBeat);
                        break;

                    case "note":
                        {
                            var durationBeat = DurationBeat(element, divisions);
                            var isChord = element.Elements().Any(child => child.Name.LocalName == "chord");
                            if (element.Elements().Any(child => child.Name.LocalName == "rest")
                                && durationBeat > 0d)
                            {
                                result.Add(new RestVoiceEntry(
                                    EditableMusicScore.BeatToTick(measureStart + cursorBeat),
                                    Math.Max(1L, EditableMusicScore.BeatToTick(durationBeat)),
                                    NormalizeStaff(ParseInt(DirectValue(element, "staff"), 0)),
                                    NormalizeVoice(DirectValue(element, "voice"))));
                            }

                            if (!isChord)
                            {
                                cursorBeat += durationBeat;
                                maxCursorBeat = Math.Max(maxCursorBeat, cursorBeat);
                            }

                            break;
                        }
                }
            }

            fallbackMeasureStart = measureStart + (measureIndex < parsedMeasures.Count
                ? parsedMeasures[measureIndex].DurationBeat
                : maxCursorBeat);
        }

        return result;
    }

    private static IReadOnlyList<RestSegmentVoice> BuildExpectedSegments(
        MusicScore score,
        ScoreMeasure measure)
    {
        var measureStartTick = EditableMusicScore.BeatToTick(measure.StartBeat);
        var measureDurationTick = Math.Max(1L, EditableMusicScore.BeatToTick(measure.DurationBeat));
        var measureEndTick = measureStartTick + measureDurationTick;
        var result = new List<(RestSegmentVoice Segment, int SourceIndex)>();

        for (var sourceIndex = 0; sourceIndex < score.Rests.Count; sourceIndex++)
        {
            var rest = score.Rests[sourceIndex];
            var restStart = EditableMusicScore.BeatToTick(rest.StartBeat);
            var restEnd = restStart + Math.Max(1L, EditableMusicScore.BeatToTick(rest.DurationBeat));
            if (restEnd <= measureStartTick || restStart >= measureEndTick)
            {
                continue;
            }

            var segmentStart = Math.Max(restStart, measureStartTick);
            var segmentEnd = Math.Min(restEnd, measureEndTick);
            if (segmentEnd <= segmentStart)
            {
                continue;
            }

            result.Add((
                new RestSegmentVoice(
                    segmentStart - measureStartTick,
                    segmentEnd - segmentStart,
                    NormalizeStaff(rest.Staff),
                    NormalizeVoice(rest.Voice)),
                sourceIndex));
        }

        return result
            .OrderBy(item => item.Segment.LocalStartTick)
            .ThenBy(item => item.Segment.Staff)
            .ThenBy(item => item.Segment.DurationTick)
            .ThenBy(item => item.SourceIndex)
            .Select(item => item.Segment)
            .ToArray();
    }

    private static IReadOnlyList<ScoreMeasure> GetEffectiveMeasures(MusicScore score)
    {
        if (score.Measures.Count > 0)
        {
            return score.Measures.OrderBy(measure => measure.StartBeat).ToArray();
        }

        var length = Math.Max(4d, score.LengthBeats);
        var measures = new List<ScoreMeasure>();
        var number = 1;
        for (var start = 0d; start < length - ScoreTiming.EventBeatTolerance; start += 4d)
        {
            measures.Add(new ScoreMeasure(
                number++,
                start,
                Math.Min(4d, length - start),
                4,
                4));
        }

        return measures;
    }

    private static void SetVoice(XElement note, string voice)
    {
        foreach (var existing in note.Elements()
                     .Where(element => element.Name.LocalName == "voice")
                     .ToArray())
        {
            existing.Remove();
        }

        var voiceElement = new XElement("voice", NormalizeVoice(voice));
        var duration = note.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "duration");
        if (duration is not null)
        {
            duration.AddAfterSelf(voiceElement);
            return;
        }

        var rest = note.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "rest");
        if (rest is not null)
        {
            rest.AddAfterSelf(voiceElement);
        }
        else
        {
            note.AddFirst(voiceElement);
        }
    }

    private static double DurationBeat(XElement element, double divisions)
    {
        if (divisions <= 0d
            || !double.TryParse(
                DirectValue(element, "duration"),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var duration))
        {
            return 0d;
        }

        return duration / divisions;
    }

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static string? DirectValue(XElement element, string localName)
        => element.Elements().FirstOrDefault(child => child.Name.LocalName == localName)?.Value;

    private static int NormalizeStaff(int staff)
        => staff is 1 or 2 ? staff : 1;

    private static string NormalizeVoice(string? voice)
        => string.IsNullOrWhiteSpace(voice) ? "1" : voice.Trim();

    private readonly record struct RestKey(long StartTick, long DurationTick, int Staff);

    private readonly record struct RestVoiceEntry(
        long StartTick,
        long DurationTick,
        int Staff,
        string Voice);

    private readonly record struct RestSegmentVoice(
        long LocalStartTick,
        long DurationTick,
        int Staff,
        string Voice);
}
