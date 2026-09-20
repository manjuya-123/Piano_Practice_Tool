using System.IO;
using NAudio.Wave;
using PianoPracticeTool.Core;
using PianoPracticeTool.Core.Editing;
using PianoPracticeTool.Services.Midi;

namespace PianoPracticeTool.Services.Editor;

public sealed class EditorReferenceAudioPositionEventArgs : EventArgs
{
    public EditorReferenceAudioPositionEventArgs(
        double scoreBeat,
        double audioSeconds)
    {
        ScoreBeat = scoreBeat;
        AudioSeconds = audioSeconds;
    }

    public double ScoreBeat { get; }

    public double AudioSeconds { get; }
}

public sealed class EditorReferenceAudioPlayer : IDisposable
{
    private const int TickIntervalMilliseconds = 20;
    private const int PreviewChannel = 15;
    private const int PanController = 10;
    private const int CenterPanMidiValue = 64;

    private readonly object _sync = new();
    private readonly IMidiSoundService _soundService;
    private readonly Timer _timer;
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private EditorStereoPanSampleProvider? _panProvider;
    private ReferenceAudioSynchronizer? _synchronizer;
    private EditorScorePreviewEventTracker? _scorePreviewEventTracker;
    private double _startAudioSeconds;
    private double _endAudioSeconds;
    private bool _loop;
    private bool _includeScore;
    private bool _skipAudioOnlyGaps;
    private bool _isPlaying;
    private bool _isPaused;
    private bool _disposed;
    private int _previewVelocity;
    private int _scoreVolumePercent = 100;
    private int _scorePanPercent;

    public EditorReferenceAudioPlayer(
        IMidiSoundService soundService,
        int previewVelocity)
    {
        _soundService = soundService
            ?? throw new ArgumentNullException(
                nameof(soundService));
        _previewVelocity = ValidateVelocity(
            previewVelocity);
        _timer = new Timer(
            TimerTick,
            null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    public event EventHandler<EditorReferenceAudioPositionEventArgs>? PositionChanged;

    public event EventHandler? PlaybackStopped;

    public bool IsLoaded
    {
        get
        {
            lock (_sync)
            {
                return _reader is not null;
            }
        }
    }

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

    public double DurationSeconds
    {
        get
        {
            lock (_sync)
            {
                return _reader?.TotalTime.TotalSeconds
                    ?? 0d;
            }
        }
    }

    public double CurrentSeconds
    {
        get
        {
            lock (_sync)
            {
                return _reader?.CurrentTime.TotalSeconds
                    ?? 0d;
            }
        }
    }

    public void SetVelocity(int velocity)
    {
        lock (_sync)
        {
            _previewVelocity = ValidateVelocity(
                velocity);
        }
    }

    public void SetScoreVolumePercent(
        int volumePercent)
    {
        lock (_sync)
        {
            _scoreVolumePercent = Math.Clamp(
                volumePercent,
                0,
                100);
            if (_isPlaying && _includeScore)
            {
                _soundService.ControlChange(
                    7,
                    (int)Math.Round(
                        _scoreVolumePercent
                        * 127d / 100d),
                    PreviewChannel);
            }
        }
    }

    public void SetScorePanPercent(
        int panPercent)
    {
        lock (_sync)
        {
            _scorePanPercent = Math.Clamp(
                panPercent,
                -100,
                100);
            if ((_isPlaying || _isPaused)
                && _includeScore)
            {
                _soundService.ControlChange(
                    PanController,
                    PanPercentToMidiValue(
                        _scorePanPercent),
                    PreviewChannel);
            }
        }
    }

    public void SetPanPercent(
        int panPercent)
    {
        lock (_sync)
        {
            if (_panProvider is null)
            {
                return;
            }

            _panProvider.Pan =
                Math.Clamp(
                    panPercent,
                    -100,
                    100) / 100f;
        }
    }

    public void SetVolumePercent(int volumePercent)
    {
        lock (_sync)
        {
            if (_reader is null)
            {
                return;
            }

            _reader.Volume = Math.Clamp(
                volumePercent,
                0,
                100) / 100f;
        }
    }

    public void Load(
        string filePath,
        int volumePercent,
        int panPercent = 0)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException(
                "元音源ファイルを指定してください。",
                nameof(filePath));
        }

        var fullPath = Path.GetFullPath(
            filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "元音源ファイルが見つかりません。",
                fullPath);
        }

        lock (_sync)
        {
            ThrowIfDisposed();
            StopPlaybackCore();
            DisposeAudioCore();

            var reader = new AudioFileReader(
                fullPath)
            {
                Volume = Math.Clamp(
                    volumePercent,
                    0,
                    100) / 100f
            };
            var panProvider =
                new EditorStereoPanSampleProvider(
                    reader)
                {
                    Pan = Math.Clamp(
                        panPercent,
                        -100,
                        100) / 100f
                };
            var output = new WaveOutEvent
            {
                DesiredLatency = 80,
                NumberOfBuffers = 3
            };

            try
            {
                output.Init(panProvider);
                _reader = reader;
                _panProvider = panProvider;
                _output = output;
            }
            catch
            {
                output.Dispose();
                reader.Dispose();
                throw;
            }
        }
    }

    public void Unload()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            StopPlaybackCore();
            DisposeAudioCore();
        }
    }

    public void Play(
        MusicScore score,
        ReferenceAudioSynchronizer synchronizer,
        double startBeat,
        double endBeat,
        bool loop,
        bool includeScore,
        double? startAudioSecondsOverride = null,
        double? endAudioSecondsOverride = null)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(
            synchronizer);

        lock (_sync)
        {
            ThrowIfDisposed();
            if (_reader is null
                || _output is null)
            {
                throw new InvalidOperationException(
                    "元音源が読み込まれていません。");
            }

            var boundedStart = Math.Clamp(
                startBeat,
                0d,
                score.LengthBeats);
            var boundedEnd = Math.Clamp(
                endBeat,
                0d,
                score.LengthBeats);
            var hasExplicitAudioRange =
                !includeScore
                && startAudioSecondsOverride.HasValue
                && endAudioSecondsOverride.HasValue;
            if (!hasExplicitAudioRange
                && boundedEnd
                    <= boundedStart
                    + ScoreTiming.EventBeatTolerance)
            {
                throw new ArgumentException(
                    "試聴終了位置は開始位置より後にしてください。");
            }

            var startAudio = Math.Clamp(
                startAudioSecondsOverride
                    ?? synchronizer.ScoreBeatToAudioSeconds(
                        boundedStart),
                0d,
                _reader.TotalTime.TotalSeconds);
            var endAudio = Math.Clamp(
                endAudioSecondsOverride
                    ?? synchronizer.ScoreBeatToAudioSecondsForRangeEnd(
                        boundedEnd),
                0d,
                _reader.TotalTime.TotalSeconds);
            if (endAudio
                <= startAudio
                + 0.001d)
            {
                throw new InvalidOperationException(
                    "同期ポイントから再生できる原音源区間を特定できません。");
            }

            StopPlaybackCore();
            _soundService.ProgramChange(
                0,
                PreviewChannel);
            _soundService.ControlChange(
                7,
                (int)Math.Round(
                    _scoreVolumePercent
                    * 127d / 100d),
                PreviewChannel);
            if (includeScore)
            {
                _soundService.ControlChange(
                    PanController,
                    PanPercentToMidiValue(
                        _scorePanPercent),
                    PreviewChannel);
            }

            _synchronizer = synchronizer;
            _scorePreviewEventTracker =
                includeScore
                    ? new EditorScorePreviewEventTracker(
                        score)
                    : null;
            _startAudioSeconds = startAudio;
            _endAudioSeconds = endAudio;
            _loop = loop;
            _includeScore = includeScore;
            _skipAudioOnlyGaps =
                !hasExplicitAudioRange;
            _reader.CurrentTime = TimeSpan.FromSeconds(
                startAudio);
            _isPaused = false;
            _isPlaying = true;
            _output.Play();
            _timer.Change(
                0,
                TickIntervalMilliseconds);
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (_disposed
                || !_isPlaying
                || _output is null)
            {
                return;
            }

            _timer.Change(
                Timeout.Infinite,
                Timeout.Infinite);
            _output.Pause();
            AllPreviewNotesOff();
            _isPlaying = false;
            _isPaused = true;
        }
    }

    public void Resume()
    {
        lock (_sync)
        {
            if (_disposed
                || !_isPaused
                || _output is null)
            {
                return;
            }

            _isPaused = false;
            _isPlaying = true;
            _output.Play();
            _timer.Change(
                0,
                TickIntervalMilliseconds);
        }
    }

    public void Stop()
    {
        var raiseStopped = false;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            raiseStopped =
                _isPlaying || _isPaused;
            StopPlaybackCore();
        }

        if (raiseStopped)
        {
            PlaybackStopped?.Invoke(
                this,
                EventArgs.Empty);
        }
    }

    public void Seek(double audioSeconds)
    {
        EditorReferenceAudioPositionEventArgs? position = null;
        lock (_sync)
        {
            if (_reader is null)
            {
                return;
            }

            var seconds = Math.Clamp(
                audioSeconds,
                0d,
                _reader.TotalTime.TotalSeconds);
            _reader.CurrentTime =
                TimeSpan.FromSeconds(seconds);
            AllPreviewNotesOff();

            if (_synchronizer is not null)
            {
                position =
                    new EditorReferenceAudioPositionEventArgs(
                        _synchronizer
                            .AudioSecondsToScoreBeat(
                                seconds),
                        seconds);
            }
        }

        if (position is not null)
        {
            PositionChanged?.Invoke(
                this,
                position);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            StopPlaybackCore();
            DisposeAudioCore();
            _disposed = true;
        }

        _timer.Dispose();
        GC.SuppressFinalize(this);
    }

    private void TimerTick(object? state)
    {
        EditorReferenceAudioPositionEventArgs? position = null;
        var stopped = false;

        lock (_sync)
        {
            if (_disposed
                || !_isPlaying
                || _reader is null
                || _synchronizer is null)
            {
                return;
            }

            var audioSeconds =
                _reader.CurrentTime.TotalSeconds;
            if (_skipAudioOnlyGaps)
            {
                var resumedAudioSeconds =
                    _synchronizer
                        .SkipAudioOnlyGaps(
                            audioSeconds);
                if (resumedAudioSeconds
                    > audioSeconds
                    + ScoreTiming.EventBeatTolerance)
                {
                    _output?.Stop();
                    _reader.CurrentTime =
                        TimeSpan.FromSeconds(
                            resumedAudioSeconds);
                    _output?.Play();
                    audioSeconds =
                        resumedAudioSeconds;
                }
            }

            if (audioSeconds
                >= _endAudioSeconds - 0.002d)
            {
                if (_loop)
                {
                    _reader.CurrentTime =
                        TimeSpan.FromSeconds(
                            _startAudioSeconds);
                    AllPreviewNotesOff();
                    audioSeconds =
                        _startAudioSeconds;
                }
                else
                {
                    _timer.Change(
                        Timeout.Infinite,
                        Timeout.Infinite);
                    _output?.Pause();
                    AllPreviewNotesOff();
                    _isPlaying = false;
                    _isPaused = false;
                    audioSeconds =
                        _endAudioSeconds;
                    stopped = true;
                }
            }

            var beat = _synchronizer
                .AudioSecondsToScoreBeat(
                    audioSeconds);
            if (_includeScore
                && !_synchronizer
                    .IsAudioOnlyGap(
                        audioSeconds)
                && _scorePreviewEventTracker is not null)
            {
                UpdateScoreNotes(
                    beat);
            }
            else
            {
                AllPreviewNotesOff();
            }

            position =
                new EditorReferenceAudioPositionEventArgs(
                    beat,
                    audioSeconds);
        }

        if (position is not null)
        {
            PositionChanged?.Invoke(
                this,
                position);
        }

        if (stopped)
        {
            PlaybackStopped?.Invoke(
                this,
                EventArgs.Empty);
        }
    }

    private void UpdateScoreNotes(
        double beat)
    {
        if (_scorePreviewEventTracker is null)
        {
            return;
        }

        foreach (var midiEvent in
                 _scorePreviewEventTracker.AdvanceTo(
                     beat))
        {
            if (midiEvent.EventType
                == EditorScorePreviewMidiEventType.NoteOn)
            {
                _soundService.NoteOn(
                    midiEvent.MidiNote,
                    _previewVelocity,
                    PreviewChannel);
            }
            else
            {
                _soundService.NoteOff(
                    midiEvent.MidiNote,
                    channel: PreviewChannel);
            }
        }
    }

    private void StopPlaybackCore()
    {
        var includedScore =
            _includeScore;
        _timer.Change(
            Timeout.Infinite,
            Timeout.Infinite);
        _output?.Pause();
        AllPreviewNotesOff();
        if (includedScore)
        {
            _soundService.ControlChange(
                PanController,
                CenterPanMidiValue,
                PreviewChannel);
        }

        _synchronizer = null;
        _scorePreviewEventTracker = null;
        _includeScore = false;
        _skipAudioOnlyGaps = false;
        _isPlaying = false;
        _isPaused = false;
    }

    private void DisposeAudioCore()
    {
        _output?.Stop();
        _output?.Dispose();
        _output = null;
        _reader?.Dispose();
        _reader = null;
        _panProvider = null;
    }

    private void AllPreviewNotesOff()
    {
        if (_scorePreviewEventTracker is null)
        {
            return;
        }

        foreach (var midiEvent in
                 _scorePreviewEventTracker.Reset())
        {
            _soundService.NoteOff(
                midiEvent.MidiNote,
                channel: PreviewChannel);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(EditorReferenceAudioPlayer));
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
            throw new ArgumentOutOfRangeException(
                nameof(velocity));
        }

        return velocity;
    }
}
