using System.IO;
using PianoPracticeTool.Core;

namespace PianoPracticeTool.Services.Editor;

public enum EditorScorePreviewMidiEventType
{
    NoteOff,
    NoteOn
}

public readonly record struct EditorScorePreviewMidiEvent(
    int MidiNote,
    EditorScorePreviewMidiEventType EventType);

public sealed class EditorScorePreviewEventTracker
{
    private readonly MusicScore _score;
    private readonly int[] _activeMidiCounts =
        new int[128];
    private double? _previousBeat;

    public EditorScorePreviewEventTracker(
        MusicScore score)
    {
        _score = score
            ?? throw new ArgumentNullException(
                nameof(score));
    }

    public IReadOnlyList<EditorScorePreviewMidiEvent> AdvanceTo(
        double beat)
    {
        if (!double.IsFinite(
                beat))
        {
            throw new ArgumentOutOfRangeException(
                nameof(beat));
        }

        var boundedBeat =
            Math.Clamp(
                beat,
                0d,
                _score.LengthBeats);
        var events =
            new List<EditorScorePreviewMidiEvent>();

        if (!_previousBeat.HasValue
            || boundedBeat
                < _previousBeat.Value)
        {
            ReconcileAtBeat(
                boundedBeat,
                events);
        }
        else
        {
            ProcessCrossedBoundaries(
                _previousBeat.Value,
                boundedBeat,
                events);
            ReconcileAtBeat(
                boundedBeat,
                events);
        }

        _previousBeat =
            boundedBeat;
        return events;
    }

    public IReadOnlyList<EditorScorePreviewMidiEvent> Reset()
    {
        var events =
            new List<EditorScorePreviewMidiEvent>();

        for (var midiNote = 0;
             midiNote < _activeMidiCounts.Length;
             midiNote++)
        {
            if (_activeMidiCounts[midiNote]
                <= 0)
            {
                continue;
            }

            events.Add(
                new EditorScorePreviewMidiEvent(
                    midiNote,
                    EditorScorePreviewMidiEventType.NoteOff));
            _activeMidiCounts[midiNote] = 0;
        }

        _previousBeat = null;
        return events;
    }

    private void ProcessCrossedBoundaries(
        double previousBeat,
        double currentBeat,
        ICollection<EditorScorePreviewMidiEvent> output)
    {
        if (currentBeat
            <= previousBeat)
        {
            return;
        }

        var boundaries =
            new List<ScoreBoundaryEvent>();

        foreach (var note in _score.Notes)
        {
            if (IsCrossed(
                    note.EndBeat,
                    previousBeat,
                    currentBeat))
            {
                boundaries.Add(
                    new ScoreBoundaryEvent(
                        note.EndBeat,
                        note.MidiNote,
                        EditorScorePreviewMidiEventType.NoteOff));
            }

            if (IsCrossed(
                    note.StartBeat,
                    previousBeat,
                    currentBeat))
            {
                boundaries.Add(
                    new ScoreBoundaryEvent(
                        note.StartBeat,
                        note.MidiNote,
                        EditorScorePreviewMidiEventType.NoteOn));
            }
        }

        foreach (var boundary in boundaries
                     .OrderBy(item =>
                         item.Beat)
                     .ThenBy(item =>
                         item.EventType
                         == EditorScorePreviewMidiEventType.NoteOff
                             ? 0
                             : 1)
                     .ThenBy(item =>
                         item.MidiNote))
        {
            ApplyBoundary(
                boundary,
                output);
        }
    }

    private void ApplyBoundary(
        ScoreBoundaryEvent boundary,
        ICollection<EditorScorePreviewMidiEvent> output)
    {
        var midiNote =
            boundary.MidiNote;
        if (midiNote is < 0 or > 127)
        {
            throw new InvalidDataException(
                $"MIDIノート番号が範囲外です: {midiNote}");
        }

        var activeCount =
            _activeMidiCounts[midiNote];
        if (boundary.EventType
            == EditorScorePreviewMidiEventType.NoteOn)
        {
            if (activeCount == 0)
            {
                output.Add(
                    new EditorScorePreviewMidiEvent(
                        midiNote,
                        EditorScorePreviewMidiEventType.NoteOn));
            }

            _activeMidiCounts[midiNote] =
                activeCount + 1;
            return;
        }

        if (activeCount <= 0)
        {
            return;
        }

        if (activeCount == 1)
        {
            _activeMidiCounts[midiNote] = 0;
            output.Add(
                new EditorScorePreviewMidiEvent(
                    midiNote,
                    EditorScorePreviewMidiEventType.NoteOff));
            return;
        }

        _activeMidiCounts[midiNote] =
            activeCount - 1;
    }

    private void ReconcileAtBeat(
        double beat,
        ICollection<EditorScorePreviewMidiEvent> output)
    {
        var desiredMidiCounts =
            new int[128];

        foreach (var note in _score.Notes)
        {
            if (note.MidiNote is < 0 or > 127)
            {
                throw new InvalidDataException(
                    $"MIDIノート番号が範囲外です: {note.MidiNote}");
            }

            if (note.StartBeat
                    <= beat
                    + ScoreTiming.EventBeatTolerance
                && note.EndBeat
                    > beat
                    + ScoreTiming.EventBeatTolerance)
            {
                desiredMidiCounts[note.MidiNote]++;
            }
        }

        for (var midiNote = 0;
             midiNote < desiredMidiCounts.Length;
             midiNote++)
        {
            var wasActive =
                _activeMidiCounts[midiNote] > 0;
            var shouldBeActive =
                desiredMidiCounts[midiNote] > 0;

            if (!wasActive
                && shouldBeActive)
            {
                output.Add(
                    new EditorScorePreviewMidiEvent(
                        midiNote,
                        EditorScorePreviewMidiEventType.NoteOn));
            }
            else if (wasActive
                     && !shouldBeActive)
            {
                output.Add(
                    new EditorScorePreviewMidiEvent(
                        midiNote,
                        EditorScorePreviewMidiEventType.NoteOff));
            }

            _activeMidiCounts[midiNote] =
                desiredMidiCounts[midiNote];
        }
    }

    private static bool IsCrossed(
        double boundaryBeat,
        double previousBeat,
        double currentBeat)
        => boundaryBeat
               > previousBeat
           && boundaryBeat
               <= currentBeat;

    private sealed record ScoreBoundaryEvent(
        double Beat,
        int MidiNote,
        EditorScorePreviewMidiEventType EventType);
}
