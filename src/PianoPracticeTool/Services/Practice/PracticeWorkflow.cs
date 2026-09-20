using PianoPracticeTool.Core;
using PianoPracticeTool.Services.Midi;

namespace PianoPracticeTool.Services.Practice;

public sealed class PracticeWorkflow
{
    private const int MaximumCountInMeasureCount = 2;
    private const int MinimumCountInMeasureCount = 1;
    private const int DefaultMetronomeVolumePercent = 100;
    private const double MaximumCountInDurationSeconds = 5d;
    private const double MaximumAudibleEventLatenessSeconds = 0.04d;

    private readonly IMidiSoundService _soundService;
    private readonly PracticeTransportClock _transportClock = new();
    private readonly PracticeRunClock _runClock = new();
    private readonly HashSet<int> _automaticNoteIds = new();
    private readonly List<PracticeResult> _practiceRecords = new();

    private MusicScore? _score;
    private ScoreTimeline? _timeline;
    private PracticeSession? _waitSession;
    private WaitPracticeTempoTracker? _waitTempoTracker;
    private PerformanceTracker? _performanceTracker;
    private PerformanceTimingProfile _performanceTimingProfile = PerformanceTimingProfile.Default;
    private PracticeMidiRange _playableMidiRange = PracticeMidiRange.Full;
    private IReadOnlyList<ScheduledMidiEvent> _playbackEvents = Array.Empty<ScheduledMidiEvent>();
    private int _playbackEventIndex;
    private int _autoPlayedTargetNoteCount;
    private PracticeMode _mode = PracticeMode.WaitForCorrectNotes;
    private PracticeHandMode _handMode = PracticeHandMode.Both;
    private PracticeRunState _state = PracticeRunState.Ready;
    private PracticeRunState _resumeState = PracticeRunState.Playing;
    private PracticeLoopRange? _loopRange;
    private double _currentBeat;
    private double _runStartBeat;
    private double _speedMultiplier = 1d;
    private double? _nextMetronomeBeat;
    private bool _metronomeEnabled;
    private bool _resumeWithCountIn;
    private double? _listenReplayStartBeat;
    private int _metronomeVolumePercent = DefaultMetronomeVolumePercent;
    private int _automaticNoteVelocity = 100;
    private int _inputTransposeSemitones;
    private string _statusMessage = "楽曲を選択してください。";

    public PracticeWorkflow(IMidiSoundService soundService)
    {
        _soundService = soundService ?? throw new ArgumentNullException(nameof(soundService));
    }

    public MusicScore? Score => _score;
    public PracticeMode Mode => _mode;
    public PracticeHandMode HandMode => _handMode;
    public PracticeRunState State => _state;
    public double CurrentBeat => _currentBeat;
    public string SoundDeviceName => _soundService.DeviceName ?? "音源なし";
    public bool IsSoundAvailable => _soundService.IsAvailable;
    public int InputTransposeSemitones => _inputTransposeSemitones;
    public PracticeResult? LastResult { get; private set; }

    public PracticeTransportPosition GetTransportPosition()
        => _transportClock.GetPosition();

    public IReadOnlyList<PracticeResult> GetPracticeRecords()
        => _practiceRecords.ToArray();

    public void Prepare(MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);

        StopAutomaticSound();
        _runClock.Reset();
        _score = score;
        _timeline = new ScoreTimeline(score);
        _mode = PracticeMode.WaitForCorrectNotes;
        _handMode = PracticeHandMode.Both;
        _state = PracticeRunState.Ready;
        _resumeState = PracticeRunState.Playing;
        _loopRange = null;
        _currentBeat = 0d;
        _runStartBeat = 0d;
        _speedMultiplier = 1d;
        _metronomeEnabled = false;
        _resumeWithCountIn = false;
        _listenReplayStartBeat = null;
        _metronomeVolumePercent = DefaultMetronomeVolumePercent;
        _inputTransposeSemitones = 0;
        _playableMidiRange = PracticeMidiRange.Full;
        _autoPlayedTargetNoteCount = 0;
        _waitSession = null;
        _waitTempoTracker = null;
        _performanceTracker = null;
        _playbackEvents = Array.Empty<ScheduledMidiEvent>();
        _playbackEventIndex = 0;
        _nextMetronomeBeat = null;
        LastResult = null;
        _transportClock.SetPosition(_state, _currentBeat, _speedMultiplier, _timeline);
        _statusMessage = "準備完了。練習モードではテンポに応じた短いカウントイン後に開始します。";
    }

    public void Clear()
    {
        StopAutomaticSound();
        _runClock.Reset();
        _score = null;
        _timeline = null;
        _waitSession = null;
        _waitTempoTracker = null;
        _performanceTracker = null;
        _playbackEvents = Array.Empty<ScheduledMidiEvent>();
        _playbackEventIndex = 0;
        _nextMetronomeBeat = null;
        _loopRange = null;
        _currentBeat = 0d;
        _runStartBeat = 0d;
        _state = PracticeRunState.Ready;
        _resumeWithCountIn = false;
        _listenReplayStartBeat = null;
        _playableMidiRange = PracticeMidiRange.Full;
        _autoPlayedTargetNoteCount = 0;
        LastResult = null;
        _transportClock.SetPosition(_state, _currentBeat, 1d, null);
        _statusMessage = "楽曲を選択してください。";
    }

    public PracticeSnapshot TogglePlayback()
    {
        EnsurePrepared();

        if (_state == PracticeRunState.Paused)
        {
            if (_resumeWithCountIn)
            {
                var restartBeat = _currentBeat;
                _resumeWithCountIn = false;
                if (_mode == PracticeMode.Listen)
                {
                    _listenReplayStartBeat = restartBeat;
                }

                StartRun(restartBeat, includeCountIn: true);
                return CreateSnapshot();
            }

            _state = _resumeState;
            if (ShouldMeasurePracticeTime(_state))
            {
                _runClock.Resume();
            }

            SetTransportPositionForCurrentState();
            _statusMessage = _state == PracticeRunState.WaitingForInput
                ? "正しい音を弾くまで待機します。"
                : "練習を再開しました。";
            return CreateSnapshot();
        }

        if (IsRunningState(_state))
        {
            AdvancePlayback();
            _resumeState = _state;
            if (ShouldMeasurePracticeTime(_state))
            {
                _runClock.Pause();
            }

            _state = PracticeRunState.Paused;
            _resumeWithCountIn = false;
            StopAutomaticSound();
            SetTransportPositionForCurrentState();
            _statusMessage = "一時停止しました。";
            return CreateSnapshot();
        }

        var range = GetActiveRange();
        var startBeat = _state == PracticeRunState.Completed
            ? range.StartBeat
            : Math.Clamp(_currentBeat, range.StartBeat, range.EndBeat);
        if (_mode == PracticeMode.Listen)
        {
            _listenReplayStartBeat = startBeat;
        }

        StartRun(startBeat, includeCountIn: true);
        return CreateSnapshot();
    }

    public PracticeSnapshot Reset()
    {
        EnsurePrepared();

        StopAutomaticSound();
        _runClock.Reset();
        var range = GetActiveRange();
        _currentBeat = range.StartBeat;
        _runStartBeat = range.StartBeat;
        _waitSession = null;
        _waitTempoTracker = null;
        _performanceTracker = null;
        _playbackEvents = Array.Empty<ScheduledMidiEvent>();
        _playbackEventIndex = 0;
        _autoPlayedTargetNoteCount = 0;
        _nextMetronomeBeat = null;
        _state = PracticeRunState.Ready;
        _resumeState = PracticeRunState.Playing;
        _resumeWithCountIn = false;
        _listenReplayStartBeat = null;
        LastResult = null;
        SetTransportPositionForCurrentState();
        _statusMessage = "最初からやり直せます。";
        return CreateSnapshot();
    }

    public void AdvancePlayback()
    {
        if (_score is null || _timeline is null)
        {
            return;
        }

        if (_state is not (PracticeRunState.CountingIn or PracticeRunState.Playing))
        {
            return;
        }

        var transport = _transportClock.GetPosition();
        if (transport.State != _state
            || transport.Beat <= _currentBeat + ScoreTiming.EventBeatTolerance)
        {
            return;
        }

        if (_state == PracticeRunState.CountingIn)
        {
            AdvanceCountInTo(transport.Beat);
        }
        else
        {
            AdvancePlayingTo(transport.Beat);
        }
    }

    public PracticeSnapshot Update()
    {
        AdvancePlayback();
        return CreateSnapshot();
    }

    public PracticeSnapshot SetMode(PracticeMode mode)
    {
        if (_mode != mode)
        {
            _mode = mode;
            if (_mode == PracticeMode.OriginalTempo)
            {
                _speedMultiplier = 1d;
            }

            ResetAfterSettingChange("練習モードを変更しました。開始してください。");
        }

        return CreateSnapshot();
    }

    public PracticeSnapshot SetHandMode(PracticeHandMode handMode)
    {
        if (_handMode != handMode)
        {
            _handMode = handMode;
            ResetAfterSettingChange("練習する手を変更しました。開始してください。");
        }

        return CreateSnapshot();
    }

    public PracticeSnapshot SetPerformanceTimingProfile(PerformanceTimingProfile timingProfile)
    {
        ArgumentNullException.ThrowIfNull(timingProfile);
        _performanceTimingProfile = timingProfile;
        ResetAfterSettingChange("演奏判定幅を更新しました。開始してください。");
        return CreateSnapshot();
    }

    public PracticeSnapshot SetPlayableMidiRange(PracticeMidiRange playableMidiRange)
    {
        if (_playableMidiRange == playableMidiRange)
        {
            return CreateSnapshot();
        }

        _playableMidiRange = playableMidiRange;
        ResetAfterSettingChange("演奏可能な鍵盤範囲を更新しました。開始してください。");
        return CreateSnapshot();
    }

    public PracticeSnapshot SetSpeed(double speedMultiplier)
    {
        if (speedMultiplier is < PracticeSpeed.MinimumMultiplier or > PracticeSpeed.MaximumMultiplier)
        {
            throw new ArgumentOutOfRangeException(nameof(speedMultiplier));
        }

        if (_mode == PracticeMode.OriginalTempo)
        {
            _speedMultiplier = 1d;
            SetTransportPositionForCurrentState();
            _statusMessage = "原曲テンポでは演奏速度は100%です。";
            return CreateSnapshot();
        }

        if (_state is PracticeRunState.CountingIn or PracticeRunState.Playing)
        {
            AdvancePlayback();
        }

        _speedMultiplier = speedMultiplier;
        SetTransportPositionForCurrentState();
        _statusMessage = $"演奏速度を {speedMultiplier * 100d:0}% に変更しました。";
        return CreateSnapshot();
    }

    public PracticeSnapshot SetMetronomeEnabled(bool enabled)
    {
        _metronomeEnabled = enabled;
        _statusMessage = enabled
            ? "演奏中のメトロノームを有効にしました。"
            : "演奏中のメトロノームを無効にしました。";
        return CreateSnapshot();
    }

    public PracticeSnapshot SetMetronomeVolumePercent(int volumePercent)
    {
        if (volumePercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(volumePercent));
        }

        _metronomeVolumePercent = volumePercent;
        _statusMessage = $"カウント / メトロノーム音量を {volumePercent}% に変更しました。";
        return CreateSnapshot();
    }

    public PracticeSnapshot SetAutomaticNoteVelocity(int velocity)
    {
        if (velocity is < 1 or > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(velocity));
        }

        _automaticNoteVelocity = velocity;
        _statusMessage = $"お手本 / 自動演奏Velocityを {velocity} に変更しました。";
        return CreateSnapshot();
    }

    public PracticeSnapshot SetLoopRange(PracticeLoopRange? loopRange)
    {
        EnsurePrepared();
        var score = _score!;

        if (loopRange is null)
        {
            _loopRange = null;
            ResetAfterSettingChange("区間ループを解除しました。");
            return CreateSnapshot();
        }

        var startBeat = Math.Clamp(loopRange.StartBeat, 0d, score.LengthBeats);
        var endBeat = Math.Clamp(loopRange.EndBeat, 0d, score.LengthBeats);
        if (endBeat <= startBeat + ScoreTiming.EventBeatTolerance)
        {
            throw new ArgumentException("Loop end must be after loop start.", nameof(loopRange));
        }

        _loopRange = new PracticeLoopRange(startBeat, endBeat);
        _currentBeat = startBeat;
        ResetAfterSettingChange("区間ループを設定しました。開始してください。");
        return CreateSnapshot();
    }

    public PracticeSnapshot Seek(double beat)
    {
        EnsurePrepared();

        var range = GetActiveRange();
        StopAutomaticSound();
        _runClock.Reset();
        _currentBeat = Math.Clamp(beat, range.StartBeat, range.EndBeat);
        _runStartBeat = _currentBeat;
        BuildSessions(_currentBeat, range.EndBeat);
        BuildPlaybackEvents(_currentBeat, range.EndBeat);
        _nextMetronomeBeat = null;
        LastResult = null;

        if (_state != PracticeRunState.Ready)
        {
            _state = PracticeRunState.Paused;
            _resumeState = PracticeRunState.Playing;
        }

        _resumeWithCountIn = _mode == PracticeMode.Listen;
        SetTransportPositionForCurrentState();
        _statusMessage = _mode == PracticeMode.Listen
            ? "再生位置を移動しました。Spaceで1小節カウントイン後に再生します。"
            : "再生位置を移動しました。";
        return CreateSnapshot();
    }

    public PracticeSnapshot SeekListenForMemorization(
        int direction,
        bool byMeasure)
    {
        EnsurePrepared();
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction));
        }

        if (_mode != PracticeMode.Listen
            || _state != PracticeRunState.Paused)
        {
            return CreateSnapshot();
        }

        var range =
            GetActiveRange();
        var targetBeat =
            byMeasure
                ? GetAdjacentMeasureBeat(
                    direction,
                    range)
                : Math.Clamp(
                    _currentBeat + direction,
                    range.StartBeat,
                    range.EndBeat);

        Seek(targetBeat);
        _statusMessage =
            byMeasure
                ? direction > 0
                    ? "次の小節へ移動しました。Spaceで聞き直せます。"
                    : "前の小節へ移動しました。Spaceで聞き直せます。"
                : direction > 0
                    ? "1拍先へ移動しました。Spaceで聞き直せます。"
                    : "1拍前へ移動しました。Spaceで聞き直せます。";
        return CreateSnapshot();
    }

    public PracticeSnapshot ReturnToListenReplayStart()
    {
        EnsurePrepared();
        if (_mode != PracticeMode.Listen
            || _state != PracticeRunState.Paused
            || _listenReplayStartBeat is not double replayStartBeat)
        {
            return CreateSnapshot();
        }

        Seek(replayStartBeat);
        _statusMessage =
            "今回の再生開始位置へ戻りました。Spaceで同じ箇所を聞き直せます。";
        return CreateSnapshot();
    }

    public PracticeSnapshot SetInputTransposeSemitones(int semitones)
    {
        if (semitones is < -48 or > 48)
        {
            throw new ArgumentOutOfRangeException(nameof(semitones));
        }

        StopAutomaticSound();
        Volatile.Write(ref _inputTransposeSemitones, semitones);
        _statusMessage = semitones == 0
            ? "入力シフトを解除しました。"
            : $"入力を {semitones:+#;-#;0} 半音シフトとして判定します。";
        return CreateSnapshot();
    }

    public PracticeSnapshot ProcessNoteOn(int physicalMidiNote)
    {
        var scoreMidiNote = MapInputMidiNote(physicalMidiNote);

        if (_score is null)
        {
            return CreateSnapshot();
        }

        if (_mode == PracticeMode.WaitForCorrectNotes
            && _state == PracticeRunState.WaitingForInput
            && _waitSession is not null)
        {
            var completedGroupBeat = _waitSession.CurrentBeat;
            var result = _waitSession.ProcessNoteOn(scoreMidiNote);
            if (result.IsCorrect)
            {
                _statusMessage = result.GroupCompleted
                    ? "正解。次へ進みます。"
                    : "正解。和音の残りを弾いてください。";
                if (result.GroupCompleted)
                {
                    if (completedGroupBeat is double beat)
                    {
                        _waitTempoTracker?.RecordGroupCompletion(beat, _runClock.ElapsedSeconds);
                    }

                    _state = PracticeRunState.Playing;
                    _nextMetronomeBeat = null;
                    SetTransportPositionForCurrentState();
                }
            }
            else
            {
                _statusMessage = $"{MidiPitch.ToName(scoreMidiNote)} は現在の課題音ではありません。";
            }
        }
        else if (IsPerformanceMode(_mode)
                 && _state == PracticeRunState.Playing
                 && _performanceTracker is not null)
        {
            var inputBeat = GetCurrentInputBeat();
            var hit = _performanceTracker.ProcessNoteOn(scoreMidiNote, inputBeat, _speedMultiplier);
            _statusMessage = hit.IsHit
                ? $"{hit.Judgement.ToString().ToUpperInvariant()}  {MidiPitch.ToName(scoreMidiNote)}"
                : $"MISS  {MidiPitch.ToName(scoreMidiNote)}";
        }

        return CreateSnapshot();
    }

    public PracticeSnapshot ProcessNoteOff(int physicalMidiNote)
    {
        var scoreMidiNote = MapInputMidiNote(physicalMidiNote);

        if (IsPerformanceMode(_mode)
            && _state == PracticeRunState.Playing
            && _performanceTracker is not null)
        {
            var inputBeat = GetCurrentInputBeat();
            var release = _performanceTracker.ProcessNoteOff(scoreMidiNote, inputBeat, _speedMultiplier);
            if (release.IsEvaluated && release.Judgement is not null)
            {
                _statusMessage = $"離鍵 {release.Judgement.Value.ToString().ToUpperInvariant()}  {MidiPitch.ToName(scoreMidiNote)}";
            }
        }

        return CreateSnapshot();
    }

    public int MapInputMidiNote(int physicalMidiNote)
        => Math.Clamp(
            physicalMidiNote - Volatile.Read(ref _inputTransposeSemitones),
            0,
            127);

    public void AllNotesOff()
    {
        StopAutomaticSound();
    }

    public PracticeSnapshot CreateSnapshot()
    {
        var expectedNotes = GetExpectedMidiNotes();
        var range = GetActiveRangeOrDefault();
        var currentSeconds = _timeline is null ? 0d : _timeline.BeatToSeconds(Math.Max(0d, _currentBeat));
        var totalSeconds = _timeline?.TotalSeconds ?? 0d;
        var currentMeasure = _score?.GetMeasureAt(Math.Max(0d, _currentBeat))?.Number ?? 0;
        var currentTempoBpm = GetCurrentTempoBpm();

        var correctCount = 0;
        var missCount = 0;
        var totalTargetNotes = 0;
        var perfectCount = 0;
        var greatCount = 0;
        var goodCount = 0;
        var currentCombo = 0;
        var maxCombo = 0;
        PerformanceJudgement? lastJudgement = null;
        var lastJudgementWasRelease = false;
        var progressRatio = 0d;
        var accuracyRatio = 1d;

        if (_mode == PracticeMode.WaitForCorrectNotes && _waitSession is not null)
        {
            correctCount = _waitSession.MatchedNoteCount;
            missCount = _waitSession.IncorrectNoteCount;
            totalTargetNotes = _waitSession.TotalNoteCount;
            progressRatio = _waitSession.ProgressRatio;
        }
        else if (IsPerformanceMode(_mode) && _performanceTracker is not null)
        {
            correctCount = _performanceTracker.HitCount;
            missCount = _performanceTracker.MissCount;
            totalTargetNotes = _performanceTracker.TotalCount;
            perfectCount = _performanceTracker.PerfectCount;
            greatCount = _performanceTracker.GreatCount;
            goodCount = _performanceTracker.GoodCount;
            currentCombo = _performanceTracker.CurrentCombo;
            maxCombo = _performanceTracker.MaxCombo;
            lastJudgement = _performanceTracker.LastJudgement;
            lastJudgementWasRelease = _performanceTracker.LastJudgementWasRelease;
            progressRatio = _performanceTracker.CompletionRatio;
            accuracyRatio = _performanceTracker.AccuracyRatio;
        }
        else if (range.EndBeat > range.StartBeat)
        {
            progressRatio = Math.Clamp((_currentBeat - range.StartBeat) / (range.EndBeat - range.StartBeat), 0d, 1d);
        }

        return new PracticeSnapshot(
            _state,
            _mode,
            _handMode,
            _currentBeat,
            _timeline?.LengthBeats ?? 0d,
            Math.Max(0d, currentSeconds),
            Math.Max(0d, totalSeconds),
            _speedMultiplier,
            currentTempoBpm,
            _metronomeEnabled,
            _metronomeVolumePercent,
            expectedNotes,
            correctCount,
            missCount,
            totalTargetNotes,
            perfectCount,
            greatCount,
            goodCount,
            currentCombo,
            maxCombo,
            lastJudgement,
            lastJudgementWasRelease,
            progressRatio,
            accuracyRatio,
            currentMeasure,
            _statusMessage,
            _loopRange,
            _inputTransposeSemitones,
            LastResult);
    }

    private void StartRun(double startBeat, bool includeCountIn)
    {
        EnsurePrepared();

        StopAutomaticSound();
        _runClock.Reset();
        _resumeWithCountIn = false;
        LastResult = null;
        var range = GetActiveRange();
        _runStartBeat = Math.Clamp(startBeat, range.StartBeat, range.EndBeat);
        BuildSessions(_runStartBeat, range.EndBeat);
        BuildPlaybackEvents(_runStartBeat, range.EndBeat);

        var countInMeasureCount = includeCountIn
            ? GetCountInMeasureCount(_runStartBeat)
            : 0;
        var countInBeats = countInMeasureCount > 0
            ? GetCountInBeats(_runStartBeat)
            : 0d;
        _currentBeat = _runStartBeat - countInBeats;
        _state = countInBeats > ScoreTiming.EventBeatTolerance
            ? PracticeRunState.CountingIn
            : PracticeRunState.Playing;
        _resumeState = _state;
        _nextMetronomeBeat = null;
        SetTransportPositionForCurrentState();
        _statusMessage = _state == PracticeRunState.CountingIn
            ? $"カウントイン 1 / {countInMeasureCount}"
            : _mode == PracticeMode.Listen
                ? "お手本再生を開始しました。"
                : "練習を開始しました。";

        if (_state == PracticeRunState.CountingIn)
        {
            InitializeMetronomeCursor(countIn: true, includeCurrentBeat: true);
            ProcessMetronomeUntil(_currentBeat, countIn: true);
        }
        else
        {
            _runClock.Restart();
            InitializeMetronomeCursor(countIn: false, includeCurrentBeat: true);
            ProcessPlaybackUntil(_currentBeat);
            ProcessMetronomeUntil(_currentBeat, countIn: false);
        }
    }

    private void AdvanceCountInTo(double targetBeat)
    {
        if (_score is null || _timeline is null)
        {
            return;
        }

        if (targetBeat < _runStartBeat - ScoreTiming.EventBeatTolerance)
        {
            ProcessMetronomeUntil(targetBeat, countIn: true);
            _currentBeat = targetBeat;
            _statusMessage =
                $"カウントイン {GetCountInMeasureNumber()} / {GetCountInMeasureCount(_runStartBeat)}";
            return;
        }

        ProcessMetronomeUntil(_runStartBeat - ScoreTiming.EventBeatTolerance, countIn: true);

        var countInBeatRate = GetCountInBeatRate();
        var countInOvershootBeats = Math.Max(0d, targetBeat - _runStartBeat);
        var overshootSeconds = countInBeatRate <= 0d
            ? 0d
            : countInOvershootBeats / countInBeatRate;
        var playingTargetBeat = _timeline.AdvanceBeat(
            _runStartBeat,
            overshootSeconds,
            _speedMultiplier);

        _currentBeat = _runStartBeat;
        _state = PracticeRunState.Playing;
        _resumeState = _state;
        _nextMetronomeBeat = null;
        _runClock.Restart();
        _transportClock.SetPosition(
            _state,
            playingTargetBeat,
            _speedMultiplier,
            _timeline);
        _statusMessage = _mode == PracticeMode.Listen
            ? "お手本再生を開始しました。"
            : "練習を開始しました。";

        InitializeMetronomeCursor(countIn: false, includeCurrentBeat: true);
        ProcessPlaybackUntil(_runStartBeat);
        ProcessMetronomeUntil(_runStartBeat, countIn: false);

        if (playingTargetBeat > _runStartBeat + ScoreTiming.EventBeatTolerance)
        {
            AdvancePlayingTo(playingTargetBeat);
        }
    }

    private void AdvancePlayingTo(double targetBeat)
    {
        var range = GetActiveRange();

        if (_mode == PracticeMode.WaitForCorrectNotes
            && _waitSession?.CurrentBeat is double waitBeat
            && waitBeat >= _currentBeat - ScoreTiming.EventBeatTolerance
            && waitBeat <= targetBeat + ScoreTiming.EventBeatTolerance)
        {
            ProcessPlaybackUntil(waitBeat);
            ProcessMetronomeUntil(waitBeat, countIn: false);
            _currentBeat = waitBeat;
            _state = PracticeRunState.WaitingForInput;
            _nextMetronomeBeat = null;
            SetTransportPositionForCurrentState();
            _statusMessage = "正しい音を弾くまで待機します。";
            return;
        }

        var boundedTargetBeat = Math.Min(targetBeat, range.EndBeat);
        ProcessPlaybackUntil(boundedTargetBeat);
        ProcessMetronomeUntil(boundedTargetBeat, countIn: false);
        _currentBeat = boundedTargetBeat;
        _performanceTracker?.AdvanceTo(_currentBeat, _speedMultiplier);

        if (targetBeat < range.EndBeat - ScoreTiming.EventBeatTolerance)
        {
            return;
        }

        if (_loopRange is not null)
        {
            StartRun(_loopRange.StartBeat, includeCountIn: false);
            _statusMessage = "区間を繰り返しています。";
            return;
        }

        _runClock.Pause();
        var completedResult = CompletePracticeResult(range);
        _state = PracticeRunState.Completed;
        _nextMetronomeBeat = null;
        SetTransportPositionForCurrentState();
        StopAutomaticSound();
        _statusMessage = completedResult is null
            ? "再生が完了しました。"
            : BuildCompletionStatus(completedResult);
    }

    private void BuildSessions(double startBeat, double endBeat)
    {
        var score = _score!;
        _waitSession = null;
        _waitTempoTracker = null;
        _performanceTracker = null;
        _autoPlayedTargetNoteCount = CountAutoPlayedTargetNotes(startBeat, endBeat);

        if (_mode == PracticeMode.WaitForCorrectNotes)
        {
            _waitSession = new PracticeSession(
                score,
                _handMode,
                startBeat,
                endBeat,
                _playableMidiRange);
            _waitTempoTracker = new WaitPracticeTempoTracker(score);
            return;
        }

        if (IsPerformanceMode(_mode))
        {
            _performanceTracker = new PerformanceTracker(
                score,
                _handMode,
                startBeat,
                endBeat,
                _performanceTimingProfile,
                _playableMidiRange);
        }
    }

    private void BuildPlaybackEvents(double startBeat, double endBeat)
    {
        if (_score is null)
        {
            _playbackEvents = Array.Empty<ScheduledMidiEvent>();
            _playbackEventIndex = 0;
            return;
        }

        var events = new List<ScheduledMidiEvent>();
        foreach (var note in _score.Notes)
        {
            if (!ShouldAutoPlay(note))
            {
                continue;
            }

            if (note.StartBeat >= startBeat - ScoreTiming.EventBeatTolerance
                && note.StartBeat < endBeat + ScoreTiming.EventBeatTolerance)
            {
                events.Add(new ScheduledMidiEvent(note.StartBeat, true, note));
                events.Add(new ScheduledMidiEvent(Math.Min(note.EndBeat, endBeat), false, note));
            }
        }

        _playbackEvents = events
            .OrderBy(item => item.Beat)
            .ThenBy(item => item.IsNoteOn ? 1 : 0)
            .ToArray();
        _playbackEventIndex = 0;
    }

    private void ProcessPlaybackUntil(double targetBeat)
    {
        while (_playbackEventIndex < _playbackEvents.Count)
        {
            var midiEvent = _playbackEvents[_playbackEventIndex];
            if (midiEvent.Beat > targetBeat + ScoreTiming.EventBeatTolerance)
            {
                break;
            }

            _playbackEventIndex++;
            if (midiEvent.IsNoteOn)
            {
                var noteAlreadyEnded = midiEvent.Note.EndBeat
                    <= targetBeat + ScoreTiming.EventBeatTolerance;
                if (noteAlreadyEnded
                    || IsAudibleEventTooLate(midiEvent.Beat, targetBeat, countIn: false))
                {
                    continue;
                }

                if (_automaticNoteIds.Add(midiEvent.Note.Id))
                {
                    _soundService.NoteOn(midiEvent.Note.MidiNote, _automaticNoteVelocity);
                }
            }
            else if (_automaticNoteIds.Remove(midiEvent.Note.Id))
            {
                _soundService.NoteOff(midiEvent.Note.MidiNote);
            }
        }
    }

    private void InitializeMetronomeCursor(bool countIn, bool includeCurrentBeat)
    {
        if (countIn)
        {
            var countInStartBeat = _runStartBeat - GetCountInBeats(_runStartBeat);
            var relativeBeat = Math.Max(0d, _currentBeat - countInStartBeat);
            var roundedRelativeBeat = Math.Round(relativeBeat);
            double nextRelativeBeat;

            if (Math.Abs(relativeBeat - roundedRelativeBeat) <= ScoreTiming.EventBeatTolerance)
            {
                nextRelativeBeat = includeCurrentBeat
                    ? roundedRelativeBeat
                    : roundedRelativeBeat + 1d;
            }
            else
            {
                nextRelativeBeat = Math.Ceiling(relativeBeat);
            }

            _nextMetronomeBeat = countInStartBeat + nextRelativeBeat;
            return;
        }

        var roundedBeat = Math.Round(_currentBeat);
        if (Math.Abs(_currentBeat - roundedBeat) <= ScoreTiming.EventBeatTolerance)
        {
            _nextMetronomeBeat = includeCurrentBeat ? roundedBeat : roundedBeat + 1d;
        }
        else
        {
            _nextMetronomeBeat = Math.Ceiling(_currentBeat);
        }
    }

    private void ProcessMetronomeUntil(double targetBeat, bool countIn)
    {
        if (_nextMetronomeBeat is null)
        {
            InitializeMetronomeCursor(countIn, includeCurrentBeat: false);
        }

        while (_nextMetronomeBeat is double beat
               && beat <= targetBeat + ScoreTiming.EventBeatTolerance)
        {
            if (countIn
                && beat >= _runStartBeat - ScoreTiming.EventBeatTolerance)
            {
                break;
            }

            if ((countIn || _metronomeEnabled)
                && !IsAudibleEventTooLate(beat, targetBeat, countIn))
            {
                var accent = countIn ? IsCountInAccent(beat) : IsMeasureStart(beat);
                PlayMetronomeClick(accent);
            }

            _nextMetronomeBeat = beat + 1d;
        }
    }

    private bool IsAudibleEventTooLate(double eventBeat, double currentBeat, bool countIn)
    {
        if (currentBeat <= eventBeat + ScoreTiming.EventBeatTolerance)
        {
            return false;
        }

        var speed = Math.Max(0.01d, _speedMultiplier);
        double latenessSeconds;
        if (countIn)
        {
            var beatRate = GetCountInBeatRate();
            latenessSeconds = beatRate <= 0d
                ? 0d
                : (currentBeat - eventBeat) / beatRate;
        }
        else if (_timeline is not null)
        {
            var scoreSeconds = _timeline.BeatToSeconds(currentBeat)
                - _timeline.BeatToSeconds(eventBeat);
            latenessSeconds = Math.Max(0d, scoreSeconds) / speed;
        }
        else
        {
            return false;
        }

        return latenessSeconds > MaximumAudibleEventLatenessSeconds;
    }

    private void PlayMetronomeClick(bool accent)
    {
        if (_metronomeVolumePercent <= 0)
        {
            return;
        }

        _soundService.PlayClick(accent, _metronomeVolumePercent);
    }

    private bool IsCountInAccent(double beat)
    {
        var measureLength = GetCountInMeasureLength();
        var countInStart = _runStartBeat - GetCountInBeats(_runStartBeat);
        var relative = beat - countInStart;
        var measureRemainder = relative % measureLength;
        return Math.Abs(measureRemainder) <= ScoreTiming.BeatGroupingTolerance
            || Math.Abs(measureRemainder - measureLength) <= ScoreTiming.BeatGroupingTolerance;
    }

    private int GetCountInMeasureNumber()
    {
        var measureLength = GetCountInMeasureLength();
        var countInStart = _runStartBeat - GetCountInBeats(_runStartBeat);
        var elapsed = Math.Max(0d, _currentBeat - countInStart);
        return Math.Clamp(
            (int)Math.Floor(elapsed / measureLength) + 1,
            MinimumCountInMeasureCount,
            GetCountInMeasureCount(_runStartBeat));
    }

    private double GetCountInMeasureLength()
        => GetMeasureLengthAt(_runStartBeat);

    private double GetCountInBeatRate()
    {
        if (_score is null)
        {
            return 0d;
        }

        var tempo = Math.Max(1d, _score.GetTempoAt(_runStartBeat));
        return tempo * Math.Max(0.01d, _speedMultiplier) / 60d;
    }

    private double GetCurrentTempoBpm()
    {
        if (_score is null)
        {
            return 0d;
        }

        var tempoBeat = _state == PracticeRunState.CountingIn
            ? _runStartBeat
            : Math.Max(0d, _currentBeat);
        return Math.Max(0d, _score.GetTempoAt(tempoBeat) * _speedMultiplier);
    }

    private double GetCurrentInputBeat()
    {
        var transport = _transportClock.GetPosition();
        return transport.State == PracticeRunState.Playing
            ? transport.Beat
            : _currentBeat;
    }

    private bool IsMeasureStart(double beat)
        => _score?.Measures.Any(
            measure => Math.Abs(measure.StartBeat - beat) <= ScoreTiming.BeatGroupingTolerance) == true;

    private IReadOnlyList<int> GetExpectedMidiNotes()
    {
        if (_mode == PracticeMode.WaitForCorrectNotes)
        {
            return _waitSession?.CurrentExpectedMidiNotes ?? Array.Empty<int>();
        }

        return IsPerformanceMode(_mode)
            ? _performanceTracker?.GetNextExpectedMidiNotes(_currentBeat) ?? Array.Empty<int>()
            : Array.Empty<int>();
    }

    private bool ShouldAutoPlay(ScoreNote note)
    {
        if (_mode == PracticeMode.Listen)
        {
            return true;
        }

        if (IsPracticedHand(note)
            && !_playableMidiRange.Contains(note.MidiNote))
        {
            return true;
        }

        return _handMode switch
        {
            PracticeHandMode.Right => note.Hand == Hand.Left,
            PracticeHandMode.Left => note.Hand == Hand.Right,
            _ => false
        };
    }

    private bool IsPracticedHand(ScoreNote note)
        => _handMode == PracticeHandMode.Both
            || (_handMode == PracticeHandMode.Right && note.Hand == Hand.Right)
            || (_handMode == PracticeHandMode.Left && note.Hand == Hand.Left);

    private int CountAutoPlayedTargetNotes(double startBeat, double endBeat)
    {
        if (_score is null || _mode == PracticeMode.Listen)
        {
            return 0;
        }

        return _score.Notes.Count(note =>
            note.StartBeat >= startBeat - ScoreTiming.EventBeatTolerance
            && note.StartBeat < endBeat - ScoreTiming.EventBeatTolerance
            && IsPracticedHand(note)
            && !_playableMidiRange.Contains(note.MidiNote));
    }

    private PracticeResult? CompletePracticeResult(PracticeLoopRange range)
    {
        if (_score is null)
        {
            return null;
        }

        PracticeScore score;
        WaitPracticeTempoSummary? waitTempo = null;
        var targetNoteCount = 0;
        var releaseMissCount = 0;
        var maxCombo = 0;

        if (_mode == PracticeMode.WaitForCorrectNotes && _waitSession is not null)
        {
            targetNoteCount = _waitSession.TotalNoteCount;
            score = PracticeScoring.CreateWaitScore(
                targetNoteCount,
                _waitSession.IncorrectNoteCount);
            waitTempo = _waitTempoTracker?.CreateSummary();
        }
        else if (IsPerformanceMode(_mode) && _performanceTracker is not null)
        {
            targetNoteCount = _performanceTracker.TotalCount;
            score = PracticeScoring.CreatePerformanceScore(
                targetNoteCount,
                _performanceTracker.PerfectTargetNoteCount,
                _performanceTracker.GreatTargetNoteCount,
                _performanceTracker.GoodTargetNoteCount,
                _performanceTracker.MissedTargetNoteCount,
                _performanceTracker.WrongKeyCount);
            releaseMissCount = _performanceTracker.ReleaseMissCount;
            maxCombo = _performanceTracker.MaxCombo;
        }
        else
        {
            return null;
        }

        var result = new PracticeResult(
            DateTimeOffset.UtcNow,
            _score.Title,
            _mode,
            _handMode,
            _runStartBeat,
            range.EndBeat,
            _runClock.ElapsedSeconds,
            _speedMultiplier,
            targetNoteCount,
            score,
            waitTempo,
            releaseMissCount,
            maxCombo,
            _autoPlayedTargetNoteCount);

        _practiceRecords.Add(result);
        LastResult = result;
        return result;
    }

    private static string BuildCompletionStatus(PracticeResult result)
    {
        var scoreText = $"{PracticeScoring.ToHundredPointScore(result.Score):0.0}点";
        var assistText = result.AutoPlayedTargetNoteCount > 0
            ? $" / 自動補完 {result.AutoPlayedTargetNoteCount}音"
            : string.Empty;
        if (result.Mode == PracticeMode.WaitForCorrectNotes)
        {
            var missText = $"誤キー {result.Score.WrongKeyCount}回";
            return result.WaitTempo is null
                ? $"練習完了: {scoreText} / {missText}{assistText}"
                : $"練習完了: {scoreText} / {missText} / 平均 {result.WaitTempo.AverageBpm:0} BPM (原曲比 {result.WaitTempo.OriginalTempoPercent:0}%){assistText}";
        }

        return $"練習完了: {scoreText} / 誤キー {result.Score.WrongKeyCount}回 / 見逃し {result.Score.MissedTargetNoteCount}音{assistText}";
    }

    private void ResetAfterSettingChange(string statusMessage)
    {
        StopAutomaticSound();
        _runClock.Reset();
        _waitSession = null;
        _waitTempoTracker = null;
        _performanceTracker = null;
        _playbackEvents = Array.Empty<ScheduledMidiEvent>();
        _playbackEventIndex = 0;
        _autoPlayedTargetNoteCount = 0;
        _nextMetronomeBeat = null;
        _state = PracticeRunState.Ready;
        _resumeState = PracticeRunState.Playing;
        _listenReplayStartBeat = null;
        var range = GetActiveRangeOrDefault();
        _currentBeat = range.StartBeat;
        _runStartBeat = range.StartBeat;
        LastResult = null;
        SetTransportPositionForCurrentState();
        _statusMessage = statusMessage;
    }

    private void StopAutomaticSound()
    {
        _automaticNoteIds.Clear();
        _nextMetronomeBeat = null;
        _soundService.AllNotesOff();
        _soundService.StopClicks();
    }

    private void SetTransportPositionForCurrentState()
    {
        var countInBeatRate = _state == PracticeRunState.CountingIn
            ? GetCountInBeatRate()
            : 0d;
        _transportClock.SetPosition(
            _state,
            _currentBeat,
            _speedMultiplier,
            _timeline,
            countInBeatRate);
    }

    private double GetAdjacentMeasureBeat(
        int direction,
        PracticeLoopRange range)
    {
        var score =
            _score!;
        var currentBeat =
            Math.Clamp(
                _currentBeat,
                range.StartBeat,
                range.EndBeat);

        if (direction > 0)
        {
            var nextMeasure =
                score.Measures
                    .Where(measure =>
                        measure.StartBeat
                            > currentBeat
                            + ScoreTiming.EventBeatTolerance)
                    .OrderBy(measure =>
                        measure.StartBeat)
                    .FirstOrDefault();
            return Math.Clamp(
                nextMeasure?.StartBeat
                    ?? range.EndBeat,
                range.StartBeat,
                range.EndBeat);
        }

        var currentMeasure =
            score.GetMeasureAt(
                currentBeat);
        if (currentMeasure is not null
            && currentBeat
                > currentMeasure.StartBeat
                + ScoreTiming.EventBeatTolerance)
        {
            return Math.Clamp(
                currentMeasure.StartBeat,
                range.StartBeat,
                range.EndBeat);
        }

        var previousMeasure =
            score.Measures
                .Where(measure =>
                    measure.StartBeat
                        < currentBeat
                        - ScoreTiming.EventBeatTolerance)
                .OrderByDescending(measure =>
                    measure.StartBeat)
                .FirstOrDefault();
        return Math.Clamp(
            previousMeasure?.StartBeat
                ?? range.StartBeat,
            range.StartBeat,
            range.EndBeat);
    }

    private PracticeLoopRange GetActiveRange()
    {
        EnsurePrepared();
        return _loopRange ?? new PracticeLoopRange(0d, _score!.LengthBeats);
    }

    private PracticeLoopRange GetActiveRangeOrDefault()
        => _score is null
            ? new PracticeLoopRange(0d, 0d)
            : _loopRange ?? new PracticeLoopRange(0d, _score.LengthBeats);

    private double GetCountInBeats(double startBeat)
        => GetMeasureLengthAt(startBeat) * GetCountInMeasureCount(startBeat);

    private int GetCountInMeasureCount(double startBeat)
    {
        if (_mode == PracticeMode.Listen)
        {
            return MinimumCountInMeasureCount;
        }

        if (_score is null)
        {
            return MinimumCountInMeasureCount;
        }

        var measureLength = GetMeasureLengthAt(startBeat);
        var effectiveTempo = Math.Max(1d, _score.GetTempoAt(startBeat))
            * Math.Max(0.01d, _speedMultiplier);
        var beatRate = effectiveTempo / 60d;
        var measureDurationSeconds = beatRate <= 0d
            ? MaximumCountInDurationSeconds
            : measureLength / beatRate;
        var durationLimitedCount = measureDurationSeconds <= 0d
            ? MaximumCountInMeasureCount
            : (int)Math.Floor(MaximumCountInDurationSeconds / measureDurationSeconds);

        return Math.Clamp(
            durationLimitedCount,
            MinimumCountInMeasureCount,
            MaximumCountInMeasureCount);
    }

    private double GetMeasureLengthAt(double beat)
    {
        var duration = _score?.GetMeasureAt(beat)?.DurationBeat ?? 4d;
        return duration > ScoreTiming.EventBeatTolerance ? duration : 4d;
    }

    private static bool IsPerformanceMode(PracticeMode mode)
        => mode is PracticeMode.PlayAlong or PracticeMode.OriginalTempo;

    private static bool IsRunningState(PracticeRunState state)
        => state is PracticeRunState.CountingIn
            or PracticeRunState.Playing
            or PracticeRunState.WaitingForInput;

    private static bool ShouldMeasurePracticeTime(PracticeRunState state)
        => state is PracticeRunState.Playing or PracticeRunState.WaitingForInput;

    private void EnsurePrepared()
    {
        if (_score is null || _timeline is null)
        {
            throw new InvalidOperationException("A score must be prepared before practice starts.");
        }
    }

    private sealed record ScheduledMidiEvent(double Beat, bool IsNoteOn, ScoreNote Note);
}
