using System.Diagnostics;
using PianoPracticeTool.Core;
using PianoPracticeTool.Services.Midi;

namespace PianoPracticeTool.Services.Editor;

public sealed record EditorPreviewPositionEventArgs(double Beat);

public sealed class EditorPreviewPlayer : IDisposable
{
    private const int TickIntervalMilliseconds = 10;
    private const int PreviewChannel = 16;
    private const int PanController = 10;
    private const int CenterPanMidiValue = 64;

    private readonly object _sync = new();
    private readonly IMidiSoundService _soundService;
    private readonly Stopwatch _clock = new();
    private readonly Timer _timer;
    private readonly Dictionary<int, int> _activeMidiCounts = new();

    private int _previewVelocity;
    private int _volumePercent = 100;
    private int _panPercent;
    private ScoreTimeline? _timeline;
    private IReadOnlyList<PreviewMidiEvent> _events = Array.Empty<PreviewMidiEvent>();
    private int _eventIndex;
    private double _startSeconds;
    private double _endSeconds;
    private double _elapsedBeforePauseSeconds;
    private bool _loop;
    private bool _isPlaying;
    private bool _isPaused;
    private bool _disposed;

    public EditorPreviewPlayer(IMidiSoundService soundService, int previewVelocity)
    {
        _soundService = soundService ?? throw new ArgumentNullException(nameof(soundService));
        _previewVelocity = ValidateVelocity(previewVelocity);
        _timer = new Timer(TimerTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void SetVelocity(int velocity)
    {
        lock (_sync)
        {
            _previewVelocity = ValidateVelocity(velocity);
        }
    }

    public void SetVolumePercent(
        int volumePercent)
    {
        lock (_sync)
        {
            _volumePercent = Math.Clamp(
                volumePercent,
                0,
                100);
            if (_isPlaying)
            {
                _soundService.ControlChange(
                    7,
                    (int)Math.Round(
                        _volumePercent
                        * 127d / 100d),
                    PreviewChannel);
            }
        }
    }

    public void SetPanPercent(
        int panPercent)
    {
        lock (_sync)
        {
            _panPercent = Math.Clamp(
                panPercent,
                -100,
                100);
            if (_isPlaying || _isPaused)
            {
                _soundService.ControlChange(
                    PanController,
                    PanPercentToMidiValue(
                        _panPercent),
                    PreviewChannel);
            }
        }
    }

    public event EventHandler<EditorPreviewPositionEventArgs>? PositionChanged;

    public event EventHandler? PlaybackStopped;

    public bool IsPlaying
    {
        get
        {
            lock (_sync)
            {
                return _isPlaying;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return _isPaused;
            }
        }
    }

    public void Play(MusicScore score, double startBeat, double endBeat, bool loop)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EditorPreviewPlayer));
        }

        var timeline = new ScoreTimeline(score);
        var boundedStart = Math.Clamp(startBeat, 0d, score.LengthBeats);
        var boundedEnd = Math.Clamp(endBeat, 0d, score.LengthBeats);
        if (boundedEnd <= boundedStart + ScoreTiming.EventBeatTolerance)
        {
            throw new ArgumentException("試聴終了位置は開始位置より後にしてください。");
        }

        lock (_sync)
        {
            StopCore();
            _soundService.ProgramChange(0, PreviewChannel);
            _soundService.ControlChange(
                7,
                (int)Math.Round(
                    _volumePercent
                    * 127d / 100d),
                PreviewChannel);
            _soundService.ControlChange(
                PanController,
                PanPercentToMidiValue(
                    _panPercent),
                PreviewChannel);
            _timeline = timeline;
            _startSeconds = timeline.BeatToSeconds(boundedStart);
            _endSeconds = timeline.BeatToSeconds(boundedEnd);
            _events = BuildEvents(score, timeline, boundedStart, boundedEnd);
            _eventIndex = 0;
            _loop = loop;
            _elapsedBeforePauseSeconds = 0d;
            _isPaused = false;
            _isPlaying = true;
            _clock.Restart();
            _timer.Change(0, TickIntervalMilliseconds);
        }
    }

    public void Pause()
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            if (!_isPlaying)
            {
                return;
            }

            _elapsedBeforePauseSeconds += _clock.Elapsed.TotalSeconds;
            _clock.Reset();
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            AllPreviewNotesOff();
            _isPlaying = false;
            _isPaused = true;
        }
    }

    public void Resume()
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            if (!_isPaused || _timeline is null)
            {
                return;
            }

            _isPaused = false;
            _isPlaying = true;
            _clock.Restart();
            _timer.Change(0, TickIntervalMilliseconds);
        }
    }

    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        var raiseStopped = false;
        lock (_sync)
        {
            raiseStopped = _isPlaying || _isPaused;
            StopCore();
        }

        if (raiseStopped)
        {
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            StopCore();
            _disposed = true;
        }

        _timer.Dispose();
        GC.SuppressFinalize(this);
    }

    private void TimerTick(object? state)
    {
        EditorPreviewPositionEventArgs? position = null;
        var stopped = false;

        lock (_sync)
        {
            if (_disposed || !_isPlaying || _timeline is null)
            {
                return;
            }

            var scoreSeconds =
                _startSeconds
                + _elapsedBeforePauseSeconds
                + _clock.Elapsed.TotalSeconds;
            if (scoreSeconds >= _endSeconds)
            {
                ProcessEventsUntil(_endSeconds);
                AllPreviewNotesOff();
                if (_loop)
                {
                    _eventIndex = 0;
                    _elapsedBeforePauseSeconds = 0d;
                    _clock.Restart();
                    scoreSeconds = _startSeconds;
                    ProcessEventsUntil(scoreSeconds);
                    position = new EditorPreviewPositionEventArgs(
                        _timeline.SecondsToBeat(scoreSeconds));
                }
                else
                {
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);
                    _clock.Stop();
                    _isPlaying = false;
                    position = new EditorPreviewPositionEventArgs(
                        _timeline.SecondsToBeat(_endSeconds));
                    stopped = true;
                }
            }
            else
            {
                ProcessEventsUntil(scoreSeconds);
                position = new EditorPreviewPositionEventArgs(
                    _timeline.SecondsToBeat(scoreSeconds));
            }
        }

        if (position is not null)
        {
            PositionChanged?.Invoke(this, position);
        }

        if (stopped)
        {
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ProcessEventsUntil(double scoreSeconds)
    {
        while (_eventIndex < _events.Count)
        {
            var midiEvent = _events[_eventIndex];
            if (midiEvent.ScoreSeconds > scoreSeconds + 0.000001d)
            {
                break;
            }

            _eventIndex++;
            if (midiEvent.IsNoteOn)
            {
                _activeMidiCounts.TryGetValue(midiEvent.MidiNote, out var count);
                _activeMidiCounts[midiEvent.MidiNote] = count + 1;
                if (count == 0)
                {
                    _soundService.NoteOn(
                        midiEvent.MidiNote,
                        _previewVelocity,
                        PreviewChannel);
                }
            }
            else if (_activeMidiCounts.TryGetValue(midiEvent.MidiNote, out var count))
            {
                if (count <= 1)
                {
                    _activeMidiCounts.Remove(midiEvent.MidiNote);
                    _soundService.NoteOff(
                        midiEvent.MidiNote,
                        channel: PreviewChannel);
                }
                else
                {
                    _activeMidiCounts[midiEvent.MidiNote] = count - 1;
                }
            }
        }
    }

    private void StopCore()
    {
        var hadPlayback =
            _isPlaying || _isPaused;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _clock.Reset();
        AllPreviewNotesOff();
        if (hadPlayback)
        {
            _soundService.ControlChange(
                PanController,
                CenterPanMidiValue,
                PreviewChannel);
        }

        _timeline = null;
        _events = Array.Empty<PreviewMidiEvent>();
        _eventIndex = 0;
        _elapsedBeforePauseSeconds = 0d;
        _isPlaying = false;
        _isPaused = false;
    }

    private void AllPreviewNotesOff()
    {
        if (_activeMidiCounts.Count == 0)
        {
            return;
        }

        var activeNotes = _activeMidiCounts.Keys.ToArray();
        _activeMidiCounts.Clear();
        foreach (var midiNote in activeNotes)
        {
            _soundService.NoteOff(midiNote, channel: PreviewChannel);
        }
    }

    private static int PanPercentToMidiValue(
        int panPercent)
        => (int)Math.Round(
            (Math.Clamp(
                panPercent,
                -100,
                100)
             + 100d)
            * 127d / 200d,
            MidpointRounding.AwayFromZero);

    private static int ValidateVelocity(int velocity)
    {
        if (velocity is < 1 or > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(velocity));
        }

        return velocity;
    }

    private static IReadOnlyList<PreviewMidiEvent> BuildEvents(
        MusicScore score,
        ScoreTimeline timeline,
        double startBeat,
        double endBeat)
    {
        var result = new List<PreviewMidiEvent>();
        foreach (var note in score.Notes)
        {
            if (note.EndBeat <= startBeat + ScoreTiming.EventBeatTolerance
                || note.StartBeat >= endBeat - ScoreTiming.EventBeatTolerance)
            {
                continue;
            }

            var audibleStart = Math.Max(note.StartBeat, startBeat);
            var audibleEnd = Math.Min(note.EndBeat, endBeat);
            result.Add(new PreviewMidiEvent(
                timeline.BeatToSeconds(audibleStart),
                note.MidiNote,
                true));
            result.Add(new PreviewMidiEvent(
                timeline.BeatToSeconds(audibleEnd),
                note.MidiNote,
                false));
        }

        return result
            .OrderBy(item => item.ScoreSeconds)
            .ThenBy(item => item.IsNoteOn ? 1 : 0)
            .ThenBy(item => item.MidiNote)
            .ToArray();
    }

    private sealed record PreviewMidiEvent(
        double ScoreSeconds,
        int MidiNote,
        bool IsNoteOn);
}
