using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace PianoPracticeTool.Core;

public static class MusicXmlParser
{
    private const double DefaultTempoBpm = 120d;
    private const double MinimumNoteDurationBeat = 0.0625d;
    private const int ScaleMetadataTicksPerQuarter = 960;

    public static MusicScore Load(string path)
    {
        using var stream = File.OpenRead(path);
        var document = XDocument.Load(stream, LoadOptions.None);
        var root = document.Root ?? throw new InvalidDataException("MusicXML root element was not found.");

        var title = ValueOf(root, "work", "work-title")
            ?? ValueOf(root, "movement-title")
            ?? Path.GetFileNameWithoutExtension(path);
        var composer = root
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "creator"
                && string.Equals(
                    element.Attribute("type")?.Value,
                    "composer",
                    StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim()
            ?? string.Empty;
        var scaleTypesByTick = ParseScaleTypeMetadata(root);

        var notes = new List<ScoreNote>();
        var rests = new List<ScoreRest>();
        var tempoEvents = new List<ScoreTempoEvent>();
        var keySignatureEvents = new List<ScoreKeySignatureEvent>();
        var harmonyEvents = new List<ScoreHarmonyEvent>();
        var measures = new List<ScoreMeasure>();
        var openTies = new Dictionary<TieKey, ScoreNote>();
        var noteId = 0;
        var scoreBeat = 0d;
        var divisions = 1d;
        var beatsPerMeasure = 4;
        var beatType = 4;
        var measureIndex = 0;

        var parts = root.Elements().Where(element => element.Name.LocalName == "part").ToList();
        if (parts.Count == 0)
        {
            throw new InvalidDataException("MusicXML does not contain a part element.");
        }

        // Yamaha Piano Sheet Converter emits a single grand-staff piano part.
        // Keep the parser deterministic by treating the first part as the practice part.
        var partMeasures = parts[0].Elements()
            .Where(element => element.Name.LocalName == "measure")
            .ToArray();
        var useSourceMeasureNumbers = HasUsableMeasureNumbers(partMeasures);

        foreach (var measure in partMeasures)
        {
            measureIndex++;

            var cursorBeat = 0d;
            var maxCursorBeat = 0d;
            var previousNoteStartByVoice = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach (var element in measure.Elements())
            {
                switch (element.Name.LocalName)
                {
                    case "attributes":
                        ParseAttributes(element, ref divisions, ref beatsPerMeasure, ref beatType);
                        if (TryGetKeySignature(element, out var keyFifths, out var keyMode))
                        {
                            var keyBeat = scoreBeat + cursorBeat;
                            scaleTypesByTick.TryGetValue(
                                BeatToScaleMetadataTick(keyBeat),
                                out var keyScaleType);
                            AddKeySignatureEvent(
                                keySignatureEvents,
                                keyBeat,
                                keyFifths,
                                keyMode,
                                keyScaleType);
                        }

                        break;

                    case "harmony":
                        if (TryGetHarmonySymbol(element, divisions, out var harmonyOffsetBeat, out var harmonySymbol))
                        {
                            AddHarmonyEvent(
                                harmonyEvents,
                                scoreBeat + cursorBeat + harmonyOffsetBeat,
                                harmonySymbol);
                        }

                        break;

                    case "direction":
                        if (TryGetTempo(element, out var directionTempo))
                        {
                            var offsetBeat = DirectionOffsetInBeats(element, divisions);
                            AddTempoEvent(tempoEvents, scoreBeat + cursorBeat + offsetBeat, directionTempo);
                        }

                        break;

                    case "sound":
                        if (TryGetTempo(element, out var soundTempo))
                        {
                            AddTempoEvent(tempoEvents, scoreBeat + cursorBeat, soundTempo);
                        }

                        break;

                    case "backup":
                        cursorBeat = Math.Max(0d, cursorBeat - DurationInBeats(element, divisions));
                        break;

                    case "forward":
                        cursorBeat += DurationInBeats(element, divisions);
                        maxCursorBeat = Math.Max(maxCursorBeat, cursorBeat);
                        break;

                    case "note":
                        ParseNote(
                            element,
                            divisions,
                            scoreBeat,
                            ref cursorBeat,
                            ref maxCursorBeat,
                            ref noteId,
                            notes,
                            rests,
                            openTies,
                            previousNoteStartByVoice);
                        break;
                }
            }

            var expectedMeasureDuration = beatsPerMeasure > 0 && beatType > 0
                ? beatsPerMeasure * 4d / beatType
                : 0d;
            var measureDurationBeat = maxCursorBeat > ScoreTiming.EventBeatTolerance
                ? maxCursorBeat
                : expectedMeasureDuration;
            var displayMeasureNumber = useSourceMeasureNumbers
                ? ParseMeasureNumber(measure, measureIndex)
                : measureIndex;

            measures.Add(new ScoreMeasure(
                displayMeasureNumber,
                scoreBeat,
                measureDurationBeat,
                beatsPerMeasure,
                beatType));
            scoreBeat += measureDurationBeat;
        }

        var orderedNotes = notes
            .OrderBy(note => note.StartBeat)
            .ThenBy(note => note.MidiNote)
            .ToList();
        var orderedRests = rests
            .OrderBy(rest => rest.StartBeat)
            .ThenBy(rest => rest.Staff)
            .ToList();
        var orderedTempoEvents = tempoEvents
            .OrderBy(item => item.Beat)
            .ToList();
        var orderedKeySignatureEvents = keySignatureEvents
            .OrderBy(item => item.Beat)
            .ToList();
        var orderedHarmonyEvents = harmonyEvents
            .OrderBy(item => item.Beat)
            .ToList();
        var initialTempo = orderedTempoEvents
            .LastOrDefault(item => item.Beat <= ScoreTiming.EventBeatTolerance)?
            .BeatsPerMinute
            ?? DefaultTempoBpm;

        return new MusicScore
        {
            Title = title.Trim(),
            Composer = composer,
            TempoBpm = initialTempo,
            Notes = orderedNotes,
            Rests = orderedRests,
            TempoEvents = orderedTempoEvents,
            KeySignatureEvents = orderedKeySignatureEvents,
            HarmonyEvents = orderedHarmonyEvents,
            Measures = measures
        };
    }

    private static void ParseNote(
        XElement element,
        double divisions,
        double scoreBeat,
        ref double cursorBeat,
        ref double maxCursorBeat,
        ref int noteId,
        List<ScoreNote> notes,
        List<ScoreRest> rests,
        Dictionary<TieKey, ScoreNote> openTies,
        Dictionary<string, double> previousNoteStartByVoice)
    {
        var durationBeat = DurationInBeats(element, divisions);
        var staff = ParseInt(ValueOfDirect(element, "staff"), 0);
        var sourceVoice = ValueOfDirect(element, "voice")?.Trim();
        var voice = string.IsNullOrWhiteSpace(sourceVoice)
            ? "1"
            : sourceVoice;

        var voiceKey = $"{staff}:{voice}";
        var isChordTone = element.Elements().Any(child => child.Name.LocalName == "chord");
        var noteStartBeat = cursorBeat;
        if (isChordTone && previousNoteStartByVoice.TryGetValue(voiceKey, out var previousStartBeat))
        {
            noteStartBeat = previousStartBeat;
        }
        else if (!isChordTone)
        {
            previousNoteStartByVoice[voiceKey] = noteStartBeat;
        }

        var absoluteStartBeat = scoreBeat + noteStartBeat;
        var isRest = element.Elements().Any(child => child.Name.LocalName == "rest");

        if (isRest)
        {
            if (durationBeat > 0d)
            {
                rests.Add(new ScoreRest(
                    absoluteStartBeat,
                    durationBeat,
                    staff,
                    voice));
            }
        }
        else if (TryGetMidiNote(element, out var midiNote))
        {
            var hand = staff switch
            {
                1 => Hand.Right,
                2 => Hand.Left,
                _ => midiNote < 60 ? Hand.Left : Hand.Right
            };
            var finger = ParseFingering(element);
            var tieTypes = GetTieTypes(element);
            var hasTieStop = tieTypes.Contains("stop", StringComparer.OrdinalIgnoreCase);
            var hasTieStart = tieTypes.Contains("start", StringComparer.OrdinalIgnoreCase);
            var tieKey = new TieKey(midiNote, staff, voice);
            var segmentDuration = Math.Max(durationBeat, MinimumNoteDurationBeat);

            if (hasTieStop && openTies.TryGetValue(tieKey, out var tiedNote))
            {
                var tiedEndBeat = absoluteStartBeat + segmentDuration;
                tiedNote.DurationBeat = Math.Max(tiedNote.DurationBeat, tiedEndBeat - tiedNote.StartBeat);
                if (tiedNote.Finger == 0 && finger is >= 1 and <= 5)
                {
                    tiedNote.Finger = finger;
                }

                if (!hasTieStart)
                {
                    openTies.Remove(tieKey);
                }
            }
            else
            {
                var scoreNote = new ScoreNote
                {
                    Id = noteId++,
                    MidiNote = midiNote,
                    StartBeat = absoluteStartBeat,
                    DurationBeat = segmentDuration,
                    Staff = staff,
                    Voice = voice,
                    Hand = hand,
                    Finger = finger
                };
                notes.Add(scoreNote);

                if (hasTieStart)
                {
                    openTies[tieKey] = scoreNote;
                }
            }
        }

        maxCursorBeat = Math.Max(maxCursorBeat, noteStartBeat + durationBeat);
        if (!isChordTone)
        {
            cursorBeat += durationBeat;
            maxCursorBeat = Math.Max(maxCursorBeat, cursorBeat);
        }
    }

    private static void ParseAttributes(
        XElement attributes,
        ref double divisions,
        ref int beatsPerMeasure,
        ref int beatType)
    {
        var divisionsText = ValueOfDirect(attributes, "divisions");
        var hasDivisions = double.TryParse(
            divisionsText,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var nextDivisions);

        if (hasDivisions && nextDivisions > 0d)
        {
            divisions = nextDivisions;
        }

        var time = attributes.Elements().FirstOrDefault(child => child.Name.LocalName == "time");
        if (time is null)
        {
            return;
        }

        var nextBeats = ParseInt(ValueOfDirect(time, "beats"), beatsPerMeasure);
        var nextBeatType = ParseInt(ValueOfDirect(time, "beat-type"), beatType);

        if (nextBeats > 0)
        {
            beatsPerMeasure = nextBeats;
        }

        if (nextBeatType > 0)
        {
            beatType = nextBeatType;
        }
    }

    private static IReadOnlyDictionary<long, string> ParseScaleTypeMetadata(
        XElement root)
    {
        var field = root
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "miscellaneous-field"
                && string.Equals(
                    element.Attribute("name")?.Value,
                    MusicTheoryAnalyzer.ScaleEventsMetadataFieldName,
                    StringComparison.OrdinalIgnoreCase));
        if (field is null || string.IsNullOrWhiteSpace(field.Value))
        {
            return new Dictionary<long, string>();
        }

        var result = new Dictionary<long, string>();
        foreach (var entry in field.Value.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries
                         | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = entry.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex >= entry.Length - 1)
            {
                continue;
            }

            if (!long.TryParse(
                    entry[..separatorIndex],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var tick))
            {
                continue;
            }

            var scaleType = entry[(separatorIndex + 1)..].Trim().ToLowerInvariant();
            if (tick >= 0L
                && MusicTheoryAnalyzer.GetScaleDefinition(scaleType) is not null)
            {
                result[tick] = scaleType;
            }
        }

        return result;
    }

    private static long BeatToScaleMetadataTick(double beat)
        => checked((long)Math.Round(
            beat * ScaleMetadataTicksPerQuarter,
            MidpointRounding.AwayFromZero));

    private static bool TryGetKeySignature(
        XElement attributes,
        out int fifths,
        out string? mode)
    {
        fifths = 0;
        mode = null;
        var key = attributes.Elements().FirstOrDefault(child => child.Name.LocalName == "key");
        if (key is null)
        {
            return false;
        }

        var text = ValueOfDirect(key, "fifths");
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out fifths)
            || fifths is < -7 or > 7)
        {
            return false;
        }

        var modeText = ValueOfDirect(key, "mode")?.Trim();
        mode = string.IsNullOrWhiteSpace(modeText)
            ? null
            : modeText.ToLowerInvariant();
        return true;
    }

    private static void AddKeySignatureEvent(
        List<ScoreKeySignatureEvent> keySignatureEvents,
        double beat,
        int fifths,
        string? mode,
        string? scaleType)
    {
        if (fifths is < -7 or > 7)
        {
            return;
        }

        var normalizedBeat = Math.Max(0d, beat);
        var existingIndex = keySignatureEvents.FindIndex(item =>
            Math.Abs(item.Beat - normalizedBeat) <= ScoreTiming.EventBeatTolerance);
        var keyEvent = new ScoreKeySignatureEvent(
            normalizedBeat,
            fifths,
            mode,
            string.IsNullOrWhiteSpace(scaleType)
                ? null
                : scaleType.Trim().ToLowerInvariant());
        if (existingIndex >= 0)
        {
            keySignatureEvents[existingIndex] = keyEvent;
        }
        else
        {
            keySignatureEvents.Add(keyEvent);
        }
    }

    private static bool TryGetHarmonySymbol(
        XElement harmony,
        double divisions,
        out double offsetBeat,
        out string symbol)
    {
        offsetBeat = 0d;
        symbol = string.Empty;

        var root = harmony.Elements().FirstOrDefault(child => child.Name.LocalName == "root");
        var rootStep = root is null ? null : ValueOfDirect(root, "root-step")?.Trim().ToUpperInvariant();
        if (rootStep is not ("A" or "B" or "C" or "D" or "E" or "F" or "G"))
        {
            return false;
        }

        var rootAlter = root is null
            ? 0
            : ParseInt(ValueOfDirect(root, "root-alter"), 0);
        var kind = harmony.Elements().FirstOrDefault(child => child.Name.LocalName == "kind");
        var kindValue = kind?.Value.Trim().ToLowerInvariant();
        var kindText = kind?.Attribute("text")?.Value?.Trim();
        var suffix = !string.IsNullOrWhiteSpace(kindText)
            ? kindText
            : kindValue switch
            {
                "major" => string.Empty,
                "minor" => "m",
                "dominant" => "7",
                "major-seventh" => "maj7",
                "minor-seventh" => "m7",
                "diminished" => "dim",
                "diminished-seventh" => "dim7",
                "augmented" => "aug",
                "augmented-seventh" => "aug7",
                "half-diminished" => "m7♭5",
                "major-minor" => "mMaj7",
                "major-sixth" => "6",
                "minor-sixth" => "m6",
                "dominant-ninth" => "9",
                "major-ninth" => "maj9",
                "minor-ninth" => "m9",
                "dominant-11th" => "11",
                "major-11th" => "maj11",
                "minor-11th" => "m11",
                "dominant-13th" => "13",
                "major-13th" => "maj13",
                "minor-13th" => "m13",
                "suspended-second" => "sus2",
                "suspended-fourth" => "sus4",
                "power" => "5",
                "none" => string.Empty,
                null or "" => string.Empty,
                _ => kindValue ?? string.Empty
            };

        var bass = harmony.Elements().FirstOrDefault(child => child.Name.LocalName == "bass");
        var bassStep = bass is null ? null : ValueOfDirect(bass, "bass-step")?.Trim().ToUpperInvariant();
        var bassAlter = bass is null
            ? 0
            : ParseInt(ValueOfDirect(bass, "bass-alter"), 0);

        var offsetText = ValueOfDirect(harmony, "offset");
        if (double.TryParse(offsetText, NumberStyles.Float, CultureInfo.InvariantCulture, out var offset)
            && divisions > 0d)
        {
            offsetBeat = offset / divisions;
        }

        var rootName = FormatHarmonyPitch(rootStep!, rootAlter);
        var bassName = bassStep is ("A" or "B" or "C" or "D" or "E" or "F" or "G")
            ? FormatHarmonyPitch(bassStep!, bassAlter)
            : null;
        symbol = rootName + suffix;
        if (!string.IsNullOrWhiteSpace(bassName)
            && !string.Equals(rootName, bassName, StringComparison.Ordinal))
        {
            symbol += "/" + bassName;
        }

        return true;
    }

    private static string FormatHarmonyPitch(string step, int alter)
        => alter switch
        {
            <= -2 => step + "bb",
            -1 => step + "b",
            1 => step + "#",
            >= 2 => step + "##",
            _ => step
        };

    private static void AddHarmonyEvent(
        List<ScoreHarmonyEvent> harmonyEvents,
        double beat,
        string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return;
        }

        var normalizedBeat = Math.Max(0d, beat);
        if (harmonyEvents.Any(item =>
                Math.Abs(item.Beat - normalizedBeat) <= ScoreTiming.EventBeatTolerance
                && string.Equals(item.Symbol, symbol, StringComparison.Ordinal)))
        {
            return;
        }

        harmonyEvents.Add(new ScoreHarmonyEvent(normalizedBeat, symbol));
    }

    private static int ParseFingering(XElement note)
    {
        var value = note
            .Descendants()
            .FirstOrDefault(child => child.Name.LocalName == "fingering")
            ?.Value;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var finger)
               && finger is >= 1 and <= 5
            ? finger
            : 0;
    }

    private static IReadOnlyCollection<string> GetTieTypes(XElement note)
    {
        var tieTypes = note.Elements()
            .Where(child => child.Name.LocalName == "tie")
            .Select(child => child.Attribute("type")?.Value)
            .Concat(note.Descendants()
                .Where(child => child.Name.LocalName == "tied")
                .Select(child => child.Attribute("type")?.Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return tieTypes;
    }

    private static void AddTempoEvent(List<ScoreTempoEvent> tempoEvents, double beat, double beatsPerMinute)
    {
        if (beatsPerMinute <= 0d)
        {
            return;
        }

        var normalizedBeat = Math.Max(0d, beat);
        var existingIndex = tempoEvents.FindIndex(item =>
            Math.Abs(item.Beat - normalizedBeat) <= ScoreTiming.EventBeatTolerance);
        var tempoEvent = new ScoreTempoEvent(normalizedBeat, beatsPerMinute);

        if (existingIndex >= 0)
        {
            tempoEvents[existingIndex] = tempoEvent;
        }
        else
        {
            tempoEvents.Add(tempoEvent);
        }
    }

    private static bool TryGetTempo(XElement element, out double beatsPerMinute)
    {
        var sound = element.Name.LocalName == "sound"
            ? element
            : element.Descendants().FirstOrDefault(child => child.Name.LocalName == "sound");
        var tempoText = sound?.Attribute("tempo")?.Value;
        if (TryParsePositiveDouble(tempoText, out beatsPerMinute))
        {
            return true;
        }

        if (element.Name.LocalName != "direction")
        {
            beatsPerMinute = 0d;
            return false;
        }

        var metronome = element.Descendants()
            .FirstOrDefault(child => child.Name.LocalName == "metronome");
        if (metronome is null)
        {
            beatsPerMinute = 0d;
            return false;
        }

        var perMinuteText = ValueOfDirect(metronome, "per-minute");
        if (!TryParsePositiveDouble(perMinuteText, out var perMinute))
        {
            beatsPerMinute = 0d;
            return false;
        }

        var beatUnit = ValueOfDirect(metronome, "beat-unit")?.Trim().ToLowerInvariant();
        var dots = metronome.Elements().Count(child => child.Name.LocalName == "beat-unit-dot");
        var quarterNoteFactor = GetQuarterNoteFactor(beatUnit, dots);
        if (quarterNoteFactor <= 0d)
        {
            beatsPerMinute = 0d;
            return false;
        }

        beatsPerMinute = perMinute * quarterNoteFactor;
        return true;
    }

    private static double GetQuarterNoteFactor(string? beatUnit, int dotCount)
    {
        var baseFactor = beatUnit switch
        {
            "whole" => 4d,
            "half" => 2d,
            "quarter" => 1d,
            "eighth" => 0.5d,
            "16th" => 0.25d,
            "32nd" => 0.125d,
            "64th" => 0.0625d,
            _ => 0d
        };

        if (baseFactor <= 0d)
        {
            return 0d;
        }

        var factor = baseFactor;
        var addition = baseFactor / 2d;
        for (var index = 0; index < dotCount; index++)
        {
            factor += addition;
            addition /= 2d;
        }

        return factor;
    }

    private static bool TryParsePositiveDouble(string? value, out double result)
    {
        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result)
            && result > 0d;
    }

    private static double DirectionOffsetInBeats(XElement direction, double divisions)
    {
        var offsetText = ValueOfDirect(direction, "offset");
        if (!double.TryParse(offsetText, NumberStyles.Float, CultureInfo.InvariantCulture, out var offset))
        {
            return 0d;
        }

        return divisions <= 0d ? 0d : offset / divisions;
    }

    private static bool HasUsableMeasureNumbers(IReadOnlyList<XElement> measures)
    {
        if (measures.Count == 0)
        {
            return false;
        }

        int? previousNumber = null;
        var hasDifferentNumber = false;

        foreach (var measure in measures)
        {
            var numberText = measure.Attribute("number")?.Value;
            if (!int.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            if (previousNumber is not null)
            {
                if (number <= previousNumber.Value)
                {
                    return false;
                }

                hasDifferentNumber = true;
            }

            previousNumber = number;
        }

        return measures.Count == 1 || hasDifferentNumber;
    }

    private static int ParseMeasureNumber(XElement measure, int fallback)
        => ParseInt(measure.Attribute("number")?.Value, fallback);

    private static double DurationInBeats(XElement element, double divisions)
    {
        var durationText = ValueOfDirect(element, "duration");
        if (!double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
        {
            return 0d;
        }

        return divisions <= 0d ? 0d : duration / divisions;
    }

    private static bool TryGetMidiNote(XElement note, out int midiNote)
    {
        midiNote = 0;
        var pitch = note.Elements().FirstOrDefault(element => element.Name.LocalName == "pitch");
        if (pitch is null)
        {
            return false;
        }

        var step = ValueOfDirect(pitch, "step");
        var octaveText = ValueOfDirect(pitch, "octave");
        if (string.IsNullOrWhiteSpace(step)
            || !int.TryParse(octaveText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var octave))
        {
            return false;
        }

        var semitone = step.ToUpperInvariant() switch
        {
            "C" => 0,
            "D" => 2,
            "E" => 4,
            "F" => 5,
            "G" => 7,
            "A" => 9,
            "B" => 11,
            _ => -100
        };
        if (semitone < 0)
        {
            return false;
        }

        var alter = ParseInt(ValueOfDirect(pitch, "alter"), 0);
        midiNote = (octave + 1) * 12 + semitone + alter;
        return midiNote is >= 0 and <= 127;
    }

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static string? ValueOfDirect(XElement element, string localName)
        => element.Elements().FirstOrDefault(child => child.Name.LocalName == localName)?.Value;

    private static string? ValueOf(XElement root, params string[] path)
    {
        XElement? current = root;
        foreach (var segment in path)
        {
            current = current?.Elements().FirstOrDefault(element => element.Name.LocalName == segment);
        }

        return current?.Value;
    }

    private readonly record struct TieKey(int MidiNote, int Staff, string Voice);
}
