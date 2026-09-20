using System.Globalization;
using System.IO;
using System.Xml.Linq;
using PianoPracticeTool.Core;
using PianoPracticeTool.Core.Editing;

namespace PianoPracticeTool.Services.Songs;

public static class MusicXmlWriter
{
    public const string FormatVersionFieldName = "piano-practice-tool:format-version";
    public const string GeneratorFieldName = "piano-practice-tool:generator";
    public const string DifficultyFieldName = "piano-practice-tool:difficulty";
    public const string CurrentFormatVersion = "1";

    private const int Divisions = EditableMusicScore.TicksPerQuarter;
    private const double DefaultTempoBpm = 120d;

    private static readonly NotatedDuration[] BaseNoteDurations =
    {
        new("whole", Divisions * 4L),
        new("half", Divisions * 2L),
        new("quarter", Divisions),
        new("eighth", Divisions / 2L),
        new("16th", Divisions / 4L),
        new("32nd", Divisions / 8L),
        new("64th", Divisions / 16L)
    };

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
        ValidateScore(score);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("保存先フォルダを特定できません。");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var document = BuildDocument(score, difficulty);
            document.Save(temporaryPath, SaveOptions.None);
            ValidateRoundTrip(temporaryPath, score, difficulty);
            ReplaceDestination(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static XDocument BuildDocument(
        MusicScore score,
        SongDifficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(score);
        ValidateScore(score);

        var measures = GetMeasures(score);
        var tempoEvents = GetTempoEventsForOutput(score);
        var root = new XElement(
            "score-partwise",
            new XAttribute("version", "3.1"),
            new XElement(
                "work",
                new XElement("work-title", score.Title)),
            new XElement(
                "identification",
                string.IsNullOrWhiteSpace(score.Composer)
                    ? null
                    : new XElement(
                        "creator",
                        new XAttribute("type", "composer"),
                        score.Composer.Trim()),
                new XElement(
                    "encoding",
                    new XElement("software", "PianoPracticeTool")),
                new XElement(
                    "miscellaneous",
                    new XElement(
                        "miscellaneous-field",
                        new XAttribute("name", FormatVersionFieldName),
                        CurrentFormatVersion),
                    new XElement(
                        "miscellaneous-field",
                        new XAttribute("name", GeneratorFieldName),
                        "PianoPracticeTool"),
                    new XElement(
                        "miscellaneous-field",
                        new XAttribute("name", DifficultyFieldName),
                        ToDifficultyCode(difficulty)),
                    CreateScaleMetadataField(score))),
            new XElement(
                "part-list",
                new XElement(
                    "score-part",
                    new XAttribute("id", "P1"),
                    new XElement("part-name", "Piano"))));

        var part = new XElement("part", new XAttribute("id", "P1"));
        var previousBeats = -1;
        var previousBeatType = -1;
        ScoreKeySignatureEvent? previousKeySignature = null;

        for (var index = 0; index < measures.Count; index++)
        {
            var measure = measures[index];
            var measureElement = new XElement(
                "measure",
                new XAttribute("number", measure.Number));

            var keySignature = score.KeySignatureEvents
                .Where(item => item.Beat <= measure.StartBeat + ScoreTiming.EventBeatTolerance)
                .OrderByDescending(item => item.Beat)
                .FirstOrDefault();
            var keyChanged = !Equals(previousKeySignature, keySignature);
            if (index == 0
                || measure.Beats != previousBeats
                || measure.BeatType != previousBeatType
                || keyChanged)
            {
                var keySignatureForAttributes =
                    index == 0 || keyChanged
                        ? keySignature
                        : null;
                measureElement.Add(BuildAttributes(
                    measure,
                    includeClefs: index == 0,
                    keySignatureForAttributes));
                previousBeats = measure.Beats;
                previousBeatType = measure.BeatType;
                previousKeySignature = keySignature;
            }

            AddTempoDirections(measureElement, tempoEvents, measure);
            AddMidMeasureKeySignatureChanges(measureElement, score, measure);
            previousKeySignature = score.KeySignatureEvents
                .Where(item =>
                    item.Beat < measure.EndBeat - ScoreTiming.EventBeatTolerance)
                .OrderByDescending(item => item.Beat)
                .FirstOrDefault()
                ?? previousKeySignature;
            AddHarmonyElements(measureElement, score, measure);
            AddMeasureContent(measureElement, score, measure);
            part.Add(measureElement);
        }

        root.Add(part);
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "no"),
            root);
    }

    private static XElement? CreateScaleMetadataField(MusicScore score)
    {
        var values = score.KeySignatureEvents
            .Where(item => !string.IsNullOrWhiteSpace(item.ScaleType))
            .OrderBy(item => item.Beat)
            .Select(item =>
                $"{EditableMusicScore.BeatToTick(item.Beat).ToString(CultureInfo.InvariantCulture)}={item.ScaleType!.Trim().ToLowerInvariant()}")
            .ToArray();
        if (values.Length == 0)
        {
            return null;
        }

        return new XElement(
            "miscellaneous-field",
            new XAttribute(
                "name",
                MusicTheoryAnalyzer.ScaleEventsMetadataFieldName),
            string.Join(";", values));
    }

    public static string ToDifficultyCode(SongDifficulty difficulty)
        => difficulty switch
        {
            SongDifficulty.Introductory => "introductory",
            SongDifficulty.Beginner => "beginner",
            SongDifficulty.Intermediate => "intermediate",
            SongDifficulty.Advanced => "advanced",
            _ => "unknown"
        };

    private static IReadOnlyList<ScoreMeasure> GetMeasures(MusicScore score)
    {
        if (score.Measures.Count > 0)
        {
            return score.Measures.OrderBy(measure => measure.StartBeat).ToArray();
        }

        var length = Math.Max(4d, score.LengthBeats);
        var result = new List<ScoreMeasure>();
        var number = 1;
        for (var start = 0d; start < length - ScoreTiming.EventBeatTolerance; start += 4d)
        {
            result.Add(new ScoreMeasure(
                number++,
                start,
                Math.Min(4d, length - start),
                4,
                4));
        }

        return result;
    }

    private static IReadOnlyList<ScoreTempoEvent> GetTempoEventsForOutput(MusicScore score)
    {
        var byTick = new SortedDictionary<long, ScoreTempoEvent>();
        foreach (var tempoEvent in score.TempoEvents)
        {
            if (tempoEvent.BeatsPerMinute <= 0d
                || tempoEvent.Beat < -ScoreTiming.EventBeatTolerance)
            {
                continue;
            }

            var tick = Math.Max(0L, EditableMusicScore.BeatToTick(tempoEvent.Beat));
            byTick[tick] = new ScoreTempoEvent(
                EditableMusicScore.TickToBeat(tick),
                tempoEvent.BeatsPerMinute);
        }

        if (!byTick.ContainsKey(0L))
        {
            var initialTempo = score.TempoBpm > 0d ? score.TempoBpm : DefaultTempoBpm;
            byTick[0L] = new ScoreTempoEvent(0d, initialTempo);
        }

        return byTick.Values.ToArray();
    }

    private static XElement BuildAttributes(
        ScoreMeasure measure,
        bool includeClefs,
        ScoreKeySignatureEvent? keySignature)
    {
        var attributes = new XElement(
            "attributes",
            new XElement("divisions", Divisions));

        if (keySignature is not null)
        {
            var key = new XElement(
                "key",
                new XElement("fifths", keySignature.Fifths));
            if (!string.IsNullOrWhiteSpace(keySignature.Mode))
            {
                key.Add(new XElement("mode", keySignature.Mode));
            }

            attributes.Add(key);
        }

        attributes.Add(
            new XElement(
                "time",
                new XElement("beats", Math.Max(1, measure.Beats)),
                new XElement("beat-type", Math.Max(1, measure.BeatType))),
            new XElement("staves", 2));

        if (includeClefs)
        {
            attributes.Add(
                new XElement(
                    "clef",
                    new XAttribute("number", "1"),
                    new XElement("sign", "G"),
                    new XElement("line", "2")),
                new XElement(
                    "clef",
                    new XAttribute("number", "2"),
                    new XElement("sign", "F"),
                    new XElement("line", "4")));
        }

        return attributes;
    }

    private static void AddTempoDirections(
        XElement measureElement,
        IReadOnlyList<ScoreTempoEvent> tempoEvents,
        ScoreMeasure measure)
    {
        var measureStartTick = EditableMusicScore.BeatToTick(measure.StartBeat);
        var measureEndTick = EditableMusicScore.BeatToTick(measure.EndBeat);
        foreach (var tempoEvent in tempoEvents)
        {
            var eventTick = EditableMusicScore.BeatToTick(tempoEvent.Beat);
            if (eventTick < measureStartTick || eventTick >= measureEndTick)
            {
                continue;
            }

            var offsetTick = eventTick - measureStartTick;
            var tempoText = tempoEvent.BeatsPerMinute.ToString("0.###", CultureInfo.InvariantCulture);
            var direction = new XElement(
                "direction",
                new XAttribute("placement", "above"),
                new XElement(
                    "direction-type",
                    new XElement(
                        "metronome",
                        new XElement("beat-unit", "quarter"),
                        new XElement("per-minute", tempoText))));

            if (offsetTick > 0L)
            {
                direction.Add(new XElement("offset", offsetTick));
            }

            direction.Add(new XElement("sound", new XAttribute("tempo", tempoText)));
            measureElement.Add(direction);
        }
    }

    private static void AddMidMeasureKeySignatureChanges(
        XElement measureElement,
        MusicScore score,
        ScoreMeasure measure)
    {
        foreach (var keySignature in score.KeySignatureEvents
                     .Where(item =>
                         item.Beat > measure.StartBeat + ScoreTiming.EventBeatTolerance
                         && item.Beat < measure.EndBeat - ScoreTiming.EventBeatTolerance)
                     .OrderBy(item => item.Beat))
        {
            var offsetTick = Math.Max(
                0L,
                EditableMusicScore.BeatToTick(keySignature.Beat - measure.StartBeat));
            if (offsetTick > 0L)
            {
                measureElement.Add(
                    new XElement(
                        "forward",
                        new XElement("duration", offsetTick)));
            }

            var key = new XElement(
                "key",
                new XElement("fifths", keySignature.Fifths));
            if (!string.IsNullOrWhiteSpace(keySignature.Mode))
            {
                key.Add(new XElement("mode", keySignature.Mode));
            }

            measureElement.Add(
                new XElement(
                    "attributes",
                    key));

            if (offsetTick > 0L)
            {
                measureElement.Add(
                    new XElement(
                        "backup",
                        new XElement("duration", offsetTick)));
            }
        }
    }

    private static void AddHarmonyElements(
        XElement measureElement,
        MusicScore score,
        ScoreMeasure measure)
    {
        foreach (var harmony in score.HarmonyEvents
                     .Where(item =>
                         item.Beat >= measure.StartBeat - ScoreTiming.EventBeatTolerance
                         && item.Beat < measure.EndBeat - ScoreTiming.EventBeatTolerance)
                     .OrderBy(item => item.Beat))
        {
            if (!TryParseHarmonySymbolForOutput(
                    harmony.Symbol,
                    out var rootStep,
                    out var rootAlter,
                    out var kindValue,
                    out var kindText,
                    out var bassStep,
                    out var bassAlter))
            {
                continue;
            }

            var element = new XElement(
                "harmony",
                new XElement(
                    "root",
                    new XElement("root-step", rootStep),
                    rootAlter == 0 ? null : new XElement("root-alter", rootAlter)),
                new XElement("kind",
                    string.IsNullOrWhiteSpace(kindText) ? null : new XAttribute("text", kindText),
                    kindValue));

            if (!string.IsNullOrWhiteSpace(bassStep))
            {
                element.Add(
                    new XElement(
                        "bass",
                        new XElement("bass-step", bassStep),
                        bassAlter == 0 ? null : new XElement("bass-alter", bassAlter)));
            }

            var offsetTick = EditableMusicScore.BeatToTick(harmony.Beat - measure.StartBeat);
            if (offsetTick > 0L)
            {
                element.Add(new XElement("offset", offsetTick));
            }

            measureElement.Add(element);
        }
    }

    private static bool TryParseHarmonySymbolForOutput(
        string symbol,
        out string rootStep,
        out int rootAlter,
        out string kindValue,
        out string? kindText,
        out string? bassStep,
        out int bassAlter)
    {
        rootStep = string.Empty;
        rootAlter = 0;
        kindValue = "major";
        kindText = null;
        bassStep = null;
        bassAlter = 0;

        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        var parts = symbol.Trim().Split('/', 2, StringSplitOptions.TrimEntries);
        if (!TryParseHarmonyPitch(parts[0], out rootStep, out rootAlter, out var suffix))
        {
            return false;
        }

        (kindValue, kindText) = suffix switch
        {
            "" => ("major", null),
            "m" => ("minor", null),
            "7" => ("dominant", null),
            "maj7" => ("major-seventh", null),
            "m7" => ("minor-seventh", null),
            "dim" => ("diminished", null),
            "dim7" => ("diminished-seventh", null),
            "aug" => ("augmented", null),
            "aug7" => ("augmented-seventh", null),
            "m7♭5" => ("half-diminished", null),
            "mMaj7" => ("major-minor", null),
            "6" => ("major-sixth", null),
            "m6" => ("minor-sixth", null),
            "9" => ("dominant-ninth", null),
            "maj9" => ("major-ninth", null),
            "m9" => ("minor-ninth", null),
            "11" => ("dominant-11th", null),
            "maj11" => ("major-11th", null),
            "m11" => ("minor-11th", null),
            "13" => ("dominant-13th", null),
            "maj13" => ("major-13th", null),
            "m13" => ("minor-13th", null),
            "sus2" => ("suspended-second", null),
            "sus4" => ("suspended-fourth", null),
            "5" => ("power", null),
            _ => ("other", suffix)
        };

        if (parts.Length == 2
            && TryParseHarmonyPitch(parts[1], out var parsedBassStep, out var parsedBassAlter, out _))
        {
            bassStep = parsedBassStep;
            bassAlter = parsedBassAlter;
        }

        return true;
    }

    private static bool TryParseHarmonyPitch(
        string value,
        out string step,
        out int alter,
        out string suffix)
    {
        step = string.Empty;
        alter = 0;
        suffix = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var first = char.ToUpperInvariant(text[0]);
        if (first is not ('A' or 'B' or 'C' or 'D' or 'E' or 'F' or 'G'))
        {
            return false;
        }

        step = first.ToString();
        var index = 1;
        while (index < text.Length && text[index] is '#' or 'b')
        {
            alter += text[index] == '#' ? 1 : -1;
            index++;
        }

        suffix = text[index..];
        return true;
    }

    private static void AddMeasureContent(
        XElement measureElement,
        MusicScore score,
        ScoreMeasure measure)
    {
        var measureStartTick = EditableMusicScore.BeatToTick(measure.StartBeat);
        var measureDurationTick = Math.Max(1L, EditableMusicScore.BeatToTick(measure.DurationBeat));
        var measureEndTick = measureStartTick + measureDurationTick;
        var noteSegments = new List<NoteSegment>();
        var restSegments = new List<RestSegment>();

        foreach (var note in score.Notes)
        {
            var noteStart = EditableMusicScore.BeatToTick(note.StartBeat);
            var noteEnd = EditableMusicScore.BeatToTick(note.EndBeat);
            if (noteEnd <= measureStartTick || noteStart >= measureEndTick)
            {
                continue;
            }

            var segmentStart = Math.Max(noteStart, measureStartTick);
            var segmentEnd = Math.Min(noteEnd, measureEndTick);
            if (segmentEnd <= segmentStart)
            {
                continue;
            }

            noteSegments.Add(new NoteSegment(
                note,
                segmentStart - measureStartTick,
                segmentEnd - segmentStart,
                noteStart < measureStartTick,
                noteEnd > measureEndTick));
        }

        foreach (var rest in score.Rests)
        {
            var restStart = EditableMusicScore.BeatToTick(rest.StartBeat);
            var restEnd = restStart + EditableMusicScore.BeatToTick(rest.DurationBeat);
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

            restSegments.Add(new RestSegment(
                segmentStart - measureStartTick,
                segmentEnd - segmentStart,
                NormalizeStaff(rest.Staff),
                NormalizeVoice(rest.Voice)));
        }

        var cursorTick = 0L;
        foreach (var segment in noteSegments
                     .OrderBy(item => item.LocalStartTick)
                     .ThenBy(item => NormalizeStaff(item.Note))
                     .ThenBy(item => NormalizeVoice(item.Note.Voice), StringComparer.Ordinal)
                     .ThenBy(item => item.Note.MidiNote))
        {
            AddCursorAdjustment(measureElement, ref cursorTick, segment.LocalStartTick);
            measureElement.Add(BuildNoteElement(segment));
            cursorTick = segment.LocalStartTick + segment.DurationTick;
        }

        foreach (var rest in restSegments
                     .OrderBy(item => item.LocalStartTick)
                     .ThenBy(item => item.Staff)
                     .ThenBy(item => item.DurationTick))
        {
            AddCursorAdjustment(measureElement, ref cursorTick, rest.LocalStartTick);
            measureElement.Add(BuildRestElement(rest));
            cursorTick = rest.LocalStartTick + rest.DurationTick;
        }

        AddCursorAdjustment(measureElement, ref cursorTick, measureDurationTick);
    }

    private static void AddCursorAdjustment(
        XElement measureElement,
        ref long cursorTick,
        long targetTick)
    {
        if (targetTick > cursorTick)
        {
            measureElement.Add(
                new XElement(
                    "forward",
                    new XElement("duration", targetTick - cursorTick)));
        }
        else if (targetTick < cursorTick)
        {
            measureElement.Add(
                new XElement(
                    "backup",
                    new XElement("duration", cursorTick - targetTick)));
        }

        cursorTick = targetTick;
    }

    private static XElement BuildNoteElement(NoteSegment segment)
    {
        var pitch = GetPitch(segment.Note.MidiNote);
        var notation = GetNotatedDuration(segment.DurationTick);
        var note = new XElement(
            "note",
            new XElement(
                "pitch",
                new XElement("step", pitch.Step),
                pitch.Alter == 0 ? null : new XElement("alter", pitch.Alter),
                new XElement("octave", pitch.Octave)),
            new XElement("duration", segment.DurationTick));

        if (segment.HasTieStop)
        {
            note.Add(new XElement("tie", new XAttribute("type", "stop")));
        }

        if (segment.HasTieStart)
        {
            note.Add(new XElement("tie", new XAttribute("type", "start")));
        }

        note.Add(new XElement("voice", NormalizeVoice(segment.Note.Voice)));
        AddNotationDuration(note, notation);
        note.Add(new XElement("staff", NormalizeStaff(segment.Note)));

        if (segment.HasTieStop || segment.HasTieStart || segment.Note.Finger is >= 1 and <= 5)
        {
            var notations = new XElement("notations");
            if (segment.HasTieStop)
            {
                notations.Add(new XElement("tied", new XAttribute("type", "stop")));
            }

            if (segment.HasTieStart)
            {
                notations.Add(new XElement("tied", new XAttribute("type", "start")));
            }

            if (segment.Note.Finger is >= 1 and <= 5)
            {
                notations.Add(
                    new XElement(
                        "technical",
                        new XElement("fingering", segment.Note.Finger)));
            }

            note.Add(notations);
        }

        return note;
    }

    private static XElement BuildRestElement(RestSegment segment)
    {
        var rest = new XElement(
            "note",
            new XElement("rest"),
            new XElement("duration", segment.DurationTick));
        rest.Add(new XElement("voice", NormalizeVoice(segment.Voice)));
        AddNotationDuration(rest, GetNotatedDuration(segment.DurationTick));
        rest.Add(new XElement("staff", segment.Staff));
        return rest;
    }

    private static NotatedDuration? GetNotatedDuration(long durationTick)
    {
        foreach (var candidate in BaseNoteDurations)
        {
            if (durationTick == candidate.Ticks)
            {
                return candidate;
            }

            if (durationTick == candidate.Ticks * 3L / 2L
                && candidate.Ticks % 2L == 0L)
            {
                return candidate with { DotCount = 1 };
            }

            if (durationTick == candidate.Ticks * 7L / 4L
                && candidate.Ticks % 4L == 0L)
            {
                return candidate with { DotCount = 2 };
            }
        }

        foreach (var candidate in BaseNoteDurations)
        {
            if (candidate.Ticks * 2L % 3L == 0L
                && durationTick == candidate.Ticks * 2L / 3L)
            {
                return candidate with
                {
                    ActualNotes = 3,
                    NormalNotes = 2,
                    NormalType = candidate.Type
                };
            }
        }

        return null;
    }

    private static void AddNotationDuration(XElement note, NotatedDuration? notation)
    {
        if (notation is null)
        {
            return;
        }

        note.Add(new XElement("type", notation.Type));
        for (var index = 0; index < notation.DotCount; index++)
        {
            note.Add(new XElement("dot"));
        }

        if (notation.ActualNotes is int actualNotes
            && notation.NormalNotes is int normalNotes
            && !string.IsNullOrWhiteSpace(notation.NormalType))
        {
            note.Add(
                new XElement(
                    "time-modification",
                    new XElement("actual-notes", actualNotes),
                    new XElement("normal-notes", normalNotes),
                    new XElement("normal-type", notation.NormalType)));
        }
    }

    private static int NormalizeStaff(ScoreNote note)
        => note.Staff is 1 or 2
            ? note.Staff
            : note.Hand == Hand.Right ? 1 : 2;

    private static int NormalizeStaff(int staff)
        => staff is 1 or 2 ? staff : 1;

    private static string NormalizeVoice(string? voice)
        => string.IsNullOrWhiteSpace(voice) ? "1" : voice.Trim();

    private static PitchParts GetPitch(int midiNote)
    {
        var normalized = Math.Clamp(midiNote, 0, 127);
        var octave = normalized / 12 - 1;
        return (normalized % 12) switch
        {
            0 => new PitchParts("C", 0, octave),
            1 => new PitchParts("C", 1, octave),
            2 => new PitchParts("D", 0, octave),
            3 => new PitchParts("D", 1, octave),
            4 => new PitchParts("E", 0, octave),
            5 => new PitchParts("F", 0, octave),
            6 => new PitchParts("F", 1, octave),
            7 => new PitchParts("G", 0, octave),
            8 => new PitchParts("G", 1, octave),
            9 => new PitchParts("A", 0, octave),
            10 => new PitchParts("A", 1, octave),
            _ => new PitchParts("B", 0, octave)
        };
    }

    private static void ValidateScore(MusicScore score)
    {
        if (score.Notes.Any(note =>
                note.MidiNote is < 0 or > 127
                || note.StartBeat < -ScoreTiming.EventBeatTolerance
                || note.DurationBeat <= 0d))
        {
            throw new InvalidOperationException("保存できないノート情報が含まれています。");
        }

        if (score.Rests.Any(rest =>
                rest.StartBeat < -ScoreTiming.EventBeatTolerance
                || rest.DurationBeat <= 0d
                || rest.Staff is < 0 or > 2))
        {
            throw new InvalidOperationException("保存できない休符情報が含まれています。");
        }

        if (score.TempoBpm <= 0d
            || score.TempoEvents.Any(item =>
                item.Beat < -ScoreTiming.EventBeatTolerance
                || item.BeatsPerMinute <= 0d))
        {
            throw new InvalidOperationException("保存できないテンポ情報が含まれています。");
        }

        if (score.Measures.Count > 0)
        {
            var orderedMeasures = score.Measures.OrderBy(measure => measure.StartBeat).ToArray();
            var expectedStart = 0d;
            foreach (var measure in orderedMeasures)
            {
                if (measure.DurationBeat <= 0d || measure.Beats <= 0 || measure.BeatType <= 0)
                {
                    throw new InvalidOperationException("保存できない小節情報が含まれています。");
                }

                if (Math.Abs(measure.StartBeat - expectedStart) > 1d / Divisions + ScoreTiming.EventBeatTolerance)
                {
                    throw new InvalidOperationException("小節の時系列が連続していません。");
                }

                expectedStart = measure.EndBeat;
            }

            if (score.Notes.Any(note => note.EndBeat > expectedStart + 1d / Divisions + ScoreTiming.EventBeatTolerance)
                || score.Rests.Any(rest => rest.StartBeat + rest.DurationBeat > expectedStart + 1d / Divisions + ScoreTiming.EventBeatTolerance))
            {
                throw new InvalidOperationException("最終小節より後にノートまたは休符が配置されています。");
            }
        }

        foreach (var group in score.Notes.GroupBy(note => (note.MidiNote, Voice: NormalizeVoice(note.Voice))))
        {
            ScoreNote? previous = null;
            foreach (var note in group.OrderBy(note => note.StartBeat).ThenBy(note => note.EndBeat))
            {
                if (previous is not null
                    && note.StartBeat < previous.EndBeat - ScoreTiming.EventBeatTolerance)
                {
                    throw new InvalidOperationException(
                        $"{MidiPitch.ToName(note.MidiNote)} の同一voiceノートが重複しています。");
                }

                previous = note;
            }
        }
    }

    private static void ValidateRoundTrip(
        string temporaryPath,
        MusicScore original,
        SongDifficulty difficulty)
    {
        var reloaded = MusicXmlParser.Load(temporaryPath);
        var tolerance = 1d / Divisions + ScoreTiming.EventBeatTolerance;

        if (!string.Equals(original.Title.Trim(), reloaded.Title.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException("保存後のMusicXMLを再読込した結果、曲名が一致しません。");
        }

        if (!string.Equals(
                original.Composer.Trim(),
                reloaded.Composer.Trim(),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("保存後のMusicXMLを再読込した結果、作曲者が一致しません。");
        }

        ValidateRoundTripNotes(original, reloaded, tolerance);
        ValidateRoundTripRests(original, reloaded, tolerance);
        ValidateRoundTripMeasures(original, reloaded, tolerance);
        ValidateRoundTripTempo(original, reloaded, tolerance);
        ValidateRoundTripKeySignatures(original, reloaded, tolerance);
        ValidateRoundTripHarmony(original, reloaded, tolerance);
        ValidateToolMetadata(temporaryPath, difficulty);
    }

    private static void ValidateRoundTripNotes(
        MusicScore original,
        MusicScore reloaded,
        double tolerance)
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
            .ThenBy(note => note.Staff)
            .ThenBy(note => NormalizeVoice(note.Voice), StringComparer.Ordinal)
            .ThenBy(note => note.DurationBeat)
            .ToArray();

        if (expected.Length != actual.Length)
        {
            throw new InvalidDataException("保存後のMusicXMLを再読込した結果、ノート数が一致しません。");
        }

        for (var index = 0; index < expected.Length; index++)
        {
            var expectedNote = expected[index];
            var actualNote = actual[index];
            if (expectedNote.MidiNote != actualNote.MidiNote
                || Math.Abs(expectedNote.StartBeat - actualNote.StartBeat) > tolerance
                || Math.Abs(expectedNote.DurationBeat - actualNote.DurationBeat) > tolerance
                || NormalizeStaff(expectedNote) != actualNote.Staff
                || !string.Equals(
                    NormalizeVoice(expectedNote.Voice),
                    NormalizeVoice(actualNote.Voice),
                    StringComparison.Ordinal)
                || NormalizeFinger(expectedNote.Finger) != NormalizeFinger(actualNote.Finger))
            {
                throw new InvalidDataException("保存後のMusicXMLを再読込した結果、ノート情報が一致しません。");
            }
        }
    }

    private static void ValidateRoundTripRests(
        MusicScore original,
        MusicScore reloaded,
        double tolerance)
    {
        var expected = original.Rests
            .OrderBy(rest => rest.StartBeat)
            .ThenBy(rest => NormalizeStaff(rest.Staff))
            .ThenBy(rest => rest.DurationBeat)
            .ToArray();
        var actual = reloaded.Rests
            .OrderBy(rest => rest.StartBeat)
            .ThenBy(rest => NormalizeStaff(rest.Staff))
            .ThenBy(rest => rest.DurationBeat)
            .ToArray();

        if (expected.Length != actual.Length)
        {
            throw new InvalidDataException("保存後のMusicXMLを再読込した結果、休符数が一致しません。");
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (Math.Abs(expected[index].StartBeat - actual[index].StartBeat) > tolerance
                || Math.Abs(expected[index].DurationBeat - actual[index].DurationBeat) > tolerance
                || NormalizeStaff(expected[index].Staff) != NormalizeStaff(actual[index].Staff)
                || !string.Equals(
                    NormalizeVoice(expected[index].Voice),
                    NormalizeVoice(actual[index].Voice),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("保存後のMusicXMLを再読込した結果、休符情報が一致しません。");
            }
        }
    }

    private static void ValidateRoundTripMeasures(
        MusicScore original,
        MusicScore reloaded,
        double tolerance)
    {
        var expected = GetMeasures(original).OrderBy(measure => measure.StartBeat).ToArray();
        var actual = reloaded.Measures.OrderBy(measure => measure.StartBeat).ToArray();
        if (expected.Length != actual.Length)
        {
            throw new InvalidDataException("保存後のMusicXMLを再読込した結果、小節数が一致しません。");
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (expected[index].Number != actual[index].Number
                || Math.Abs(expected[index].StartBeat - actual[index].StartBeat) > tolerance
                || Math.Abs(expected[index].DurationBeat - actual[index].DurationBeat) > tolerance
                || expected[index].Beats != actual[index].Beats
                || expected[index].BeatType != actual[index].BeatType)
            {
                throw new InvalidDataException("保存後のMusicXMLを再読込した結果、小節情報が一致しません。");
            }
        }
    }

    private static void ValidateRoundTripTempo(
        MusicScore original,
        MusicScore reloaded,
        double tolerance)
    {
        var expected = GetTempoEventsForOutput(original).ToArray();
        var actual = reloaded.TempoEvents.OrderBy(item => item.Beat).ToArray();
        if (expected.Length != actual.Length)
        {
            throw new InvalidDataException("保存後のMusicXMLを再読込した結果、テンポイベント数が一致しません。");
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (Math.Abs(expected[index].Beat - actual[index].Beat) > tolerance
                || Math.Abs(expected[index].BeatsPerMinute - actual[index].BeatsPerMinute) > 0.001d)
            {
                throw new InvalidDataException("保存後のMusicXMLを再読込した結果、テンポ情報が一致しません。");
            }
        }
    }

    private static void ValidateRoundTripKeySignatures(
        MusicScore original,
        MusicScore reloaded,
        double tolerance)
    {
        var expected = original.KeySignatureEvents
            .OrderBy(item => item.Beat)
            .ToArray();
        var actual = reloaded.KeySignatureEvents
            .OrderBy(item => item.Beat)
            .ToArray();
        if (expected.Length != actual.Length)
        {
            throw new InvalidDataException(
                "保存後のMusicXMLを再読込した結果、調号イベント数が一致しません。");
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (Math.Abs(expected[index].Beat - actual[index].Beat) > tolerance
                || expected[index].Fifths != actual[index].Fifths
                || !string.Equals(
                    expected[index].Mode?.Trim().ToLowerInvariant(),
                    actual[index].Mode?.Trim().ToLowerInvariant(),
                    StringComparison.Ordinal)
                || !string.Equals(
                    expected[index].ScaleType?.Trim().ToLowerInvariant(),
                    actual[index].ScaleType?.Trim().ToLowerInvariant(),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "保存後のMusicXMLを再読込した結果、調号情報が一致しません。");
            }
        }
    }

    private static void ValidateRoundTripHarmony(
        MusicScore original,
        MusicScore reloaded,
        double tolerance)
    {
        var expected = original.HarmonyEvents
            .OrderBy(item => item.Beat)
            .ThenBy(item => item.Symbol, StringComparer.Ordinal)
            .ToArray();
        var actual = reloaded.HarmonyEvents
            .OrderBy(item => item.Beat)
            .ThenBy(item => item.Symbol, StringComparer.Ordinal)
            .ToArray();
        if (expected.Length != actual.Length)
        {
            throw new InvalidDataException(
                "保存後のMusicXMLを再読込した結果、コードイベント数が一致しません。");
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (Math.Abs(expected[index].Beat - actual[index].Beat) > tolerance
                || !string.Equals(
                    expected[index].Symbol.Trim(),
                    actual[index].Symbol.Trim(),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "保存後のMusicXMLを再読込した結果、コード情報が一致しません。");
            }
        }
    }

    private static void ValidateToolMetadata(string temporaryPath, SongDifficulty difficulty)
    {
        var document = XDocument.Load(temporaryPath, LoadOptions.None);
        string? ReadField(string fieldName)
            => document
                .Descendants()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "miscellaneous-field"
                    && string.Equals(
                        element.Attribute("name")?.Value,
                        fieldName,
                        StringComparison.OrdinalIgnoreCase))
                ?.Value;

        if (!string.Equals(ReadField(FormatVersionFieldName), CurrentFormatVersion, StringComparison.Ordinal)
            || !string.Equals(ReadField(GeneratorFieldName), "PianoPracticeTool", StringComparison.Ordinal)
            || !string.Equals(ReadField(DifficultyFieldName), ToDifficultyCode(difficulty), StringComparison.Ordinal))
        {
            throw new InvalidDataException("保存後のMusicXMLにツール固有メタデータが正しく記録されていません。");
        }
    }

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

    private sealed record NoteSegment(
        ScoreNote Note,
        long LocalStartTick,
        long DurationTick,
        bool HasTieStop,
        bool HasTieStart);

    private sealed record RestSegment(
        long LocalStartTick,
        long DurationTick,
        int Staff,
        string Voice);

    private sealed record NotatedDuration(
        string Type,
        long Ticks,
        int DotCount = 0,
        int? ActualNotes = null,
        int? NormalNotes = null,
        string? NormalType = null);

    private sealed record PitchParts(string Step, int Alter, int Octave);
}
