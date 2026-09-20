using System.Globalization;
using System.IO;
using System.Xml.Linq;
using PianoPracticeTool.Core;
using PianoPracticeTool.Core.Editing;
using PianoPracticeTool.Services.Songs;

namespace PianoPracticeTool.Services.Editor;

public static class EditorMusicXmlSaveService
{
    private static readonly double BeatTolerance =
        1d / EditableMusicScore.TicksPerQuarter + ScoreTiming.EventBeatTolerance;

    public static void SaveValidated(
        string path,
        MusicScore score,
        SongDifficulty difficulty)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be null, empty, or whitespace.", nameof(path));
        }

        ArgumentNullException.ThrowIfNull(score);
        ValidateKeySignatures(score);

        var destinationPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("保存先フォルダを特定できません。");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.editor.tmp");

        try
        {
            // First run the normalized writer's existing semantic validation.
            MusicXmlWriter.SaveValidated(temporaryPath, score, difficulty);

            var document = XDocument.Load(temporaryPath, LoadOptions.None);
            ApplyKeySignatures(document, score);
            NormalizeChordNotation(document);
            MusicXmlRestVoiceMetadata.ApplyToDocument(document, score);
            document.Save(temporaryPath, SaveOptions.None);

            ValidatePostProcessRoundTrip(temporaryPath, score);
            MusicXmlRestVoiceMetadata.ValidateRoundTrip(temporaryPath, score);
            ReplaceDestination(temporaryPath, destinationPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateKeySignatures(MusicScore score)
    {
        var normalizedTicks = new HashSet<long>();
        var measures = GetEffectiveMeasures(score);
        var scoreEndTick = EditableMusicScore.BeatToTick(measures[^1].EndBeat);

        foreach (var keyEvent in score.KeySignatureEvents)
        {
            if (keyEvent.Beat < -ScoreTiming.EventBeatTolerance
                || keyEvent.Fifths is < -7 or > 7)
            {
                throw new InvalidOperationException("保存できない調号情報が含まれています。");
            }

            var tick = Math.Max(0L, EditableMusicScore.BeatToTick(keyEvent.Beat));
            if (tick >= scoreEndTick)
            {
                throw new InvalidOperationException("曲の終端以降に調号変更を配置できません。");
            }

            if (!normalizedTicks.Add(tick))
            {
                throw new InvalidOperationException("同一位置に複数の調号変更があります。");
            }

            if (FindMeasureIndexAtTick(measures, tick) < 0)
            {
                throw new InvalidOperationException("調号変更位置に対応する小節がありません。");
            }
        }
    }

    private static void ApplyKeySignatures(XDocument document, MusicScore score)
    {
        if (score.KeySignatureEvents.Count == 0)
        {
            return;
        }

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
            throw new InvalidDataException("調号保存時に小節数が一致しません。");
        }

        var indexedEvents = score.KeySignatureEvents
            .OrderBy(item => item.Beat)
            .Select(item =>
            {
                var tick = Math.Max(0L, EditableMusicScore.BeatToTick(item.Beat));
                var measureIndex = FindMeasureIndexAtTick(measures, tick);
                if (measureIndex < 0)
                {
                    throw new InvalidOperationException("調号変更位置に対応する小節が見つかりません。");
                }

                var measureStartTick = EditableMusicScore.BeatToTick(measures[measureIndex].StartBeat);
                return new IndexedKeyEvent(measureIndex, tick - measureStartTick, item.Fifths);
            })
            .GroupBy(item => item.MeasureIndex)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.LocalTick).ToArray());

        foreach (var pair in indexedEvents.OrderBy(item => item.Key))
        {
            var measureElement = measureElements[pair.Key];
            var initialAttributes = measureElement.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "attributes");
            XNode? insertionAnchor = initialAttributes;

            foreach (var keyEvent in pair.Value)
            {
                if (keyEvent.LocalTick == 0L)
                {
                    if (initialAttributes is null)
                    {
                        initialAttributes = new XElement("attributes");
                        measureElement.AddFirst(initialAttributes);
                        insertionAnchor = initialAttributes;
                    }

                    SetKeyInAttributes(initialAttributes, keyEvent.Fifths);
                    continue;
                }

                var forward = new XElement(
                    "forward",
                    new XElement("duration", keyEvent.LocalTick));
                var attributes = new XElement(
                    "attributes",
                    new XElement(
                        "key",
                        new XElement("fifths", keyEvent.Fifths)));
                var backup = new XElement(
                    "backup",
                    new XElement("duration", keyEvent.LocalTick));

                if (insertionAnchor is null)
                {
                    measureElement.AddFirst(forward, attributes, backup);
                }
                else
                {
                    insertionAnchor.AddAfterSelf(forward, attributes, backup);
                }

                insertionAnchor = backup;
            }
        }
    }

    private static void SetKeyInAttributes(XElement attributes, int fifths)
    {
        foreach (var existing in attributes.Elements()
                     .Where(element => element.Name.LocalName == "key")
                     .ToArray())
        {
            existing.Remove();
        }

        var key = new XElement(
            "key",
            new XElement("fifths", fifths));
        var divisions = attributes.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "divisions");
        if (divisions is not null)
        {
            divisions.AddAfterSelf(key);
            return;
        }

        var time = attributes.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "time");
        if (time is not null)
        {
            time.AddBeforeSelf(key);
        }
        else
        {
            attributes.AddFirst(key);
        }
    }

    private static void NormalizeChordNotation(XDocument document)
    {
        var part = document.Root?
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "part");
        if (part is null)
        {
            return;
        }

        foreach (var measure in part.Elements().Where(element => element.Name.LocalName == "measure"))
        {
            var cursorTick = 0L;
            XElement? pendingBackup = null;
            var seenNotes = new HashSet<ChordKey>();

            foreach (var element in measure.Elements().ToArray())
            {
                switch (element.Name.LocalName)
                {
                    case "forward":
                        cursorTick += ReadDurationTicks(element);
                        pendingBackup = null;
                        break;

                    case "backup":
                        cursorTick = Math.Max(0L, cursorTick - ReadDurationTicks(element));
                        pendingBackup = element;
                        break;

                    case "note":
                        {
                            var durationTick = ReadDurationTicks(element);
                            var isRest = element.Elements().Any(child => child.Name.LocalName == "rest");
                            if (!isRest && durationTick > 0L)
                            {
                                var staff = ParseInt(DirectValue(element, "staff"), 0);
                                var voice = NormalizeVoice(DirectValue(element, "voice"));
                                var key = new ChordKey(cursorTick, durationTick, staff, voice);
                                if (pendingBackup is not null && seenNotes.Contains(key))
                                {
                                    element.AddFirst(new XElement("chord"));
                                    pendingBackup.Remove();
                                }
                                else
                                {
                                    seenNotes.Add(key);
                                }
                            }

                            cursorTick += durationTick;
                            pendingBackup = null;
                            break;
                        }

                    default:
                        pendingBackup = null;
                        break;
                }
            }
        }
    }

    private static void ValidatePostProcessRoundTrip(string path, MusicScore original)
    {
        var reloaded = MusicXmlParser.Load(path);
        ValidateNoteRoundTrip(original, reloaded);
        ValidateKeySignatureRoundTrip(original, reloaded);
    }

    private static void ValidateNoteRoundTrip(MusicScore original, MusicScore reloaded)
    {
        var expected = original.Notes
            .OrderBy(note => note.StartBeat)
            .ThenBy(note => note.MidiNote)
            .ThenBy(note => NormalizeStaff(note))
            .ThenBy(note => NormalizeVoice(note.Voice), StringComparer.Ordinal)
            .ThenBy(note => note.DurationBeat)
            .ToArray();
        var actual = reloaded.Notes
            .OrderBy(note => note.StartBeat)
            .ThenBy(note => note.MidiNote)
            .ThenBy(note => NormalizeStaff(note))
            .ThenBy(note => NormalizeVoice(note.Voice), StringComparer.Ordinal)
            .ThenBy(note => note.DurationBeat)
            .ToArray();

        if (expected.Length != actual.Length)
        {
            throw new InvalidDataException("MusicXML後処理後のノート数が一致しません。");
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (expected[index].MidiNote != actual[index].MidiNote
                || Math.Abs(expected[index].StartBeat - actual[index].StartBeat) > BeatTolerance
                || Math.Abs(expected[index].DurationBeat - actual[index].DurationBeat) > BeatTolerance
                || NormalizeStaff(expected[index]) != NormalizeStaff(actual[index])
                || !string.Equals(NormalizeVoice(expected[index].Voice), NormalizeVoice(actual[index].Voice), StringComparison.Ordinal)
                || NormalizeFinger(expected[index].Finger) != NormalizeFinger(actual[index].Finger))
            {
                throw new InvalidDataException("MusicXML後処理後のノート情報が一致しません。");
            }
        }
    }

    private static void ValidateKeySignatureRoundTrip(MusicScore original, MusicScore reloaded)
    {
        var expected = original.KeySignatureEvents
            .OrderBy(item => item.Beat)
            .ToArray();
        var actual = reloaded.KeySignatureEvents
            .OrderBy(item => item.Beat)
            .ToArray();

        if (expected.Length != actual.Length)
        {
            throw new InvalidDataException("保存後のMusicXMLを再読込した結果、調号変更数が一致しません。");
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (Math.Abs(expected[index].Beat - actual[index].Beat) > BeatTolerance
                || expected[index].Fifths != actual[index].Fifths)
            {
                throw new InvalidDataException("保存後のMusicXMLを再読込した結果、調号情報が一致しません。");
            }
        }
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

    private static int FindMeasureIndexAtTick(
        IReadOnlyList<ScoreMeasure> measures,
        long tick)
    {
        for (var index = 0; index < measures.Count; index++)
        {
            var startTick = EditableMusicScore.BeatToTick(measures[index].StartBeat);
            if (tick == startTick)
            {
                return index;
            }
        }

        for (var index = 0; index < measures.Count; index++)
        {
            var startTick = EditableMusicScore.BeatToTick(measures[index].StartBeat);
            var endTick = EditableMusicScore.BeatToTick(measures[index].EndBeat);
            if (tick > startTick && tick < endTick)
            {
                return index;
            }
        }

        return -1;
    }

    private static long ReadDurationTicks(XElement element)
        => long.TryParse(
            DirectValue(element, "duration"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var duration)
            ? Math.Max(0L, duration)
            : 0L;

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static string? DirectValue(XElement element, string localName)
        => element.Elements().FirstOrDefault(child => child.Name.LocalName == localName)?.Value;

    private static string NormalizeVoice(string? voice)
        => string.IsNullOrWhiteSpace(voice) ? "1" : voice.Trim();

    private static int NormalizeStaff(ScoreNote note)
        => note.Staff is 1 or 2
            ? note.Staff
            : note.Hand == Hand.Right ? 1 : 2;

    private static int NormalizeFinger(int finger)
        => finger is >= 1 and <= 5 ? finger : 0;

    private static void ReplaceDestination(string temporaryPath, string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            File.Move(temporaryPath, destinationPath);
            return;
        }

        try
        {
            File.Replace(temporaryPath, destinationPath, null);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or IOException)
        {
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
    }

    private readonly record struct IndexedKeyEvent(
        int MeasureIndex,
        long LocalTick,
        int Fifths);

    private readonly record struct ChordKey(
        long StartTick,
        long DurationTick,
        int Staff,
        string Voice);
}
