using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PianoPracticeTool.Core;
using PianoPracticeTool.Services.Practice;
using PianoPracticeTool.Services.Settings;
using PianoPracticeTool.Services.Songs;

namespace PianoPracticeTool.Views;

public sealed partial class PerformanceView : UserControl
{
    private readonly DispatcherTimer _metronomePopupCloseTimer;
    private bool _suppressVolumeChanged;
    private bool _suppressTransportSliderChanged;
    private bool _isListenMode;
    private bool _isTransportSeeking;
    private PracticeRunState _practiceState = PracticeRunState.Ready;

    public PerformanceView()
    {
        InitializeComponent();
        _metronomePopupCloseTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(180d)
        };
        _metronomePopupCloseTimer.Tick += MetronomePopupCloseTimer_Tick;
    }

    public event EventHandler? ExitRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler<PerformanceSeekRequestedEventArgs>? SeekRequested;
    public event EventHandler<PerformanceMemorizationSeekRequestedEventArgs>? MemorizationSeekRequested;
    public event EventHandler<PerformanceSpeedStepRequestedEventArgs>? SpeedStepRequested;
    public event EventHandler? MetronomeToggleRequested;
    public event EventHandler<PerformanceMetronomeVolumeRequestedEventArgs>? MetronomeVolumeChanged;
    public event EventHandler? ResultBackRequested;

    public void SetSong(
        SongCatalogEntry song,
        PracticeSetupOptions options,
        AppSettings settings,
        PracticeMidiRange playableMidiRange)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);

        PianoRoll.Score = song.Score;
        PianoRoll.PlayableMidiRange = playableMidiRange;
        PianoRoll.CurrentBeat = null;
        PianoRoll.ExpectedMidiNotes = Array.Empty<int>();
        PianoRoll.ActiveMidiNotes = Array.Empty<int>();
        PianoRoll.HandMode = options.HandMode;
        PianoRoll.VisibleRangeMultiplier = settings.PianoRollVisibleRangePercent / 100d;
        PianoRoll.ShowHandColors = settings.ShowHandColors;
        PianoRoll.ShowFingering =
            settings.ShowHandColors
            && settings.ShowFingering;
        PianoRoll.ShowExpectedKeyboardGuide = settings.ShowExpectedKeyboardGuide;

        TitleText.Text = song.Title;
        var octaveShiftText = options.AppliedDeviceOctaveShiftSemitones == 0
            ? "MIDI shift なし"
            : $"MIDI shift {options.AppliedDeviceOctaveShiftSemitones / 12:+#;-#;0} oct";
        ModeText.Text =
            $"{GetModeText(options.Mode)} / {GetHandText(options.HandMode)} / {octaveShiftText}";
        _isListenMode = options.Mode == PracticeMode.Listen;
        _isTransportSeeking = false;
        _practiceState = PracticeRunState.Ready;
        ListenTransportPanel.Visibility = _isListenMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        _suppressTransportSliderChanged = true;
        try
        {
            PlaybackPositionSlider.Minimum = 0d;
            PlaybackPositionSlider.Maximum = Math.Max(0d, song.Score.LengthBeats);
            PlaybackPositionSlider.Value = 0d;
            PlaybackPositionSlider.IsEnabled = song.Score.LengthBeats > ScoreTiming.EventBeatTolerance;
        }
        finally
        {
            _suppressTransportSliderChanged = false;
        }
        PlaybackPositionText.Text = "00:00 / 00:00";
        PlaybackMeasureText.Text = "小節 —";
        PauseButton.ToolTip = _isListenMode
            ? "再生 / 一時停止 (Space)"
            : "一時停止 / 再開";
        _suppressVolumeChanged = true;
        try
        {
            MetronomeVolumeSlider.Value = settings.MetronomeVolumePercent;
        }
        finally
        {
            _suppressVolumeChanged = false;
        }

        MetronomeVolumeText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0}%",
            settings.MetronomeVolumePercent);
        LiveScoreText.Text = "—";
        LiveDetailText.Text = string.Empty;
        JudgementOverlay.Visibility = Visibility.Collapsed;
        PauseOverlay.Visibility = Visibility.Collapsed;
        ListenPauseIndicator.Visibility = Visibility.Collapsed;
        ResultOverlay.Visibility = Visibility.Collapsed;
        MetronomePopup.IsOpen = false;
    }

    public void SetActiveMidiNotes(IReadOnlyCollection<int> midiNotes)
    {
        ArgumentNullException.ThrowIfNull(midiNotes);
        PianoRoll.ActiveMidiNotes = midiNotes.ToArray();
    }

    public void SetSnapshot(PracticeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _practiceState = snapshot.State;
        PianoRoll.ExpectedMidiNotes = snapshot.ExpectedMidiNotes;
        PianoRoll.HandMode = snapshot.HandMode;

        SpeedText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0:0}%",
            snapshot.SpeedMultiplier * 100d);
        BpmText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0:0} BPM",
            snapshot.CurrentTempoBpm);
        SpeedDownButton.IsEnabled = snapshot.Mode != PracticeMode.OriginalTempo
            && snapshot.SpeedMultiplier > PracticeSpeed.MinimumMultiplier + 0.001d;
        SpeedUpButton.IsEnabled = snapshot.Mode != PracticeMode.OriginalTempo
            && snapshot.SpeedMultiplier < PracticeSpeed.MaximumMultiplier - 0.001d;

        _suppressVolumeChanged = true;
        try
        {
            MetronomeVolumeSlider.Value = snapshot.MetronomeVolumePercent;
        }
        finally
        {
            _suppressVolumeChanged = false;
        }

        MetronomeVolumeText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0}%",
            snapshot.MetronomeVolumePercent);
        MetronomeButton.Opacity = snapshot.MetronomeEnabled ? 1d : 0.52d;
        MetronomeButton.ToolTip = snapshot.MetronomeEnabled
            ? "メトロノーム: 有効"
            : "メトロノーム: 無効";

        PauseButton.Content = snapshot.State == PracticeRunState.Paused ? "▶" : "⏸";
        var isPaused = snapshot.State == PracticeRunState.Paused;
        var useCompactPauseIndicator = isPaused && snapshot.Mode == PracticeMode.Listen;
        PauseOverlay.Visibility = isPaused && !useCompactPauseIndicator
            ? Visibility.Visible
            : Visibility.Collapsed;
        ListenPauseIndicator.Visibility = useCompactPauseIndicator
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdateListenTransport(snapshot);
        UpdateLiveScore(snapshot);
        UpdateJudgement(snapshot);
        UpdateResult(snapshot);
    }

    public void SetAnimationPosition(PracticeRunState state, double currentBeat)
    {
        PianoRoll.CurrentBeat = state == PracticeRunState.Ready
            ? null
            : currentBeat;
    }

    private void UpdateListenTransport(PracticeSnapshot snapshot)
    {
        var isListen = snapshot.Mode == PracticeMode.Listen;
        ListenTransportPanel.Visibility = isListen
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!isListen)
        {
            return;
        }

        _isListenMode = true;
        var maximumBeat = Math.Max(0d, snapshot.LengthBeats);
        _suppressTransportSliderChanged = true;
        try
        {
            PlaybackPositionSlider.Maximum = maximumBeat;
            PlaybackPositionSlider.IsEnabled = maximumBeat > ScoreTiming.EventBeatTolerance;
            if (!_isTransportSeeking)
            {
                PlaybackPositionSlider.Value = Math.Clamp(
                    snapshot.CurrentBeat,
                    PlaybackPositionSlider.Minimum,
                    maximumBeat);
            }
        }
        finally
        {
            _suppressTransportSliderChanged = false;
        }

        PlaybackPositionText.Text =
            $"{FormatPlaybackTime(snapshot.CurrentSeconds)} / {FormatPlaybackTime(snapshot.TotalSeconds)}";
        PlaybackMeasureText.Text = snapshot.CurrentMeasureNumber > 0
            ? $"小節 {snapshot.CurrentMeasureNumber}"
            : "小節 —";

        if (snapshot.State is PracticeRunState.Ready
            or PracticeRunState.Paused
            or PracticeRunState.Completed)
        {
            PauseButton.Content = "▶";
        }
        else
        {
            PauseButton.Content = "⏸";
        }
    }

    private void UpdateLiveScore(PracticeSnapshot snapshot)
    {
        if (snapshot.Mode == PracticeMode.Listen)
        {
            LiveScoreText.Text = string.Format(CultureInfo.CurrentCulture, "{0:P0}", snapshot.ProgressRatio);
            LiveDetailText.Text = "再生進捗";
            return;
        }

        if (snapshot.Mode == PracticeMode.WaitForCorrectNotes)
        {
            var score = PracticeScoring.CreateWaitProgressScore(
                snapshot.TotalTargetNotes,
                snapshot.CorrectCount,
                snapshot.MissCount);
            LiveScoreText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0:0.0}",
                PracticeScoring.ToHundredPointScore(score));
            LiveDetailText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "暫定 / 100点満点 / 誤キー {0}",
                score.WrongKeyCount);
            return;
        }

        LiveScoreText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0:0.0}",
            Math.Clamp(snapshot.AccuracyRatio * 100d, 0d, 100d));
        LiveDetailText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "現在までの演奏精度 / P {0} G {1} Good {2} / MISS {3}",
            snapshot.PerfectCount,
            snapshot.GreatCount,
            snapshot.GoodCount,
            snapshot.MissCount);
    }

    private void UpdateJudgement(PracticeSnapshot snapshot)
    {
        if (snapshot.Mode is not (PracticeMode.PlayAlong or PracticeMode.OriginalTempo)
            || snapshot.LastJudgement is null)
        {
            JudgementOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        JudgementOverlay.Visibility = Visibility.Visible;
        JudgementText.Text = snapshot.LastJudgement.Value.ToString().ToUpperInvariant();
        JudgementText.Foreground = snapshot.LastJudgement.Value switch
        {
            PerformanceJudgement.Perfect => (Brush)FindResource("SuccessBrush"),
            PerformanceJudgement.Great => (Brush)FindResource("AccentTextBrush"),
            PerformanceJudgement.Good => (Brush)FindResource("WarningBrush"),
            _ => (Brush)FindResource("DangerBrush")
        };
        ComboText.Text = snapshot.CurrentCombo > 1
            ? $"{snapshot.CurrentCombo} COMBO"
            : snapshot.MaxCombo > 0
                ? $"MAX {snapshot.MaxCombo} COMBO"
                : string.Empty;
    }

    private void UpdateResult(PracticeSnapshot snapshot)
    {
        if (snapshot.State != PracticeRunState.Completed)
        {
            ResultOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        ResultOverlay.Visibility = Visibility.Visible;
        if (snapshot.LastResult is null)
        {
            ResultScoreText.Text = "再生完了";
            ResultDetailText.Text = "お手本再生が完了しました。";
            return;
        }

        var result = snapshot.LastResult;
        ResultScoreText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0:0.0} / 100 点",
            PracticeScoring.ToHundredPointScore(result.Score));
        var assistText = result.AutoPlayedTargetNoteCount > 0
            ? string.Format(
                CultureInfo.CurrentCulture,
                "\n自動補完 {0:N0}音 / 補助練習（BEST対象外）",
                result.AutoPlayedTargetNoteCount)
            : string.Empty;

        if (result.Mode == PracticeMode.WaitForCorrectNotes)
        {
            var tempoText = result.WaitTempo is null
                ? string.Empty
                : string.Format(
                    CultureInfo.CurrentCulture,
                    "\n平均 {0:0} BPM / 原曲比 {1:0}%",
                    result.WaitTempo.AverageBpm,
                    result.WaitTempo.OriginalTempoPercent);
            ResultDetailText.Text = $"誤キー {result.Score.WrongKeyCount}回{tempoText}{assistText}";
            return;
        }

        ResultDetailText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "PERFECT {0} / GREAT {1} / GOOD {2}\n誤キー {3} / 見逃し {4} / 離鍵MISS {5} / MAX {6} COMBO{7}",
            result.Score.PerfectTargetNoteCount,
            result.Score.GreatTargetNoteCount,
            result.Score.GoodTargetNoteCount,
            result.Score.WrongKeyCount,
            result.Score.MissedTargetNoteCount,
            result.ReleaseMissCount,
            result.MaxCombo,
            assistText);
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
        => ExitRequested?.Invoke(this, EventArgs.Empty);

    private void PauseButton_Click(object sender, RoutedEventArgs e)
        => PauseRequested?.Invoke(this, EventArgs.Empty);

    private void PianoRoll_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (!_isListenMode
            || _practiceState != PracticeRunState.Paused)
        {
            return;
        }

        var modifiers =
            Keyboard.Modifiers;
        if (modifiers
            is not (ModifierKeys.None or ModifierKeys.Shift))
        {
            return;
        }

        if (e.Delta == 0)
        {
            return;
        }

        var direction =
            e.Delta > 0
                ? 1
                : -1;
        MemorizationSeekRequested?.Invoke(
            this,
            new PerformanceMemorizationSeekRequestedEventArgs(
                direction,
                modifiers == ModifierKeys.Shift));
        e.Handled = true;
    }

    private void PlaybackPositionSlider_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_isListenMode)
        {
            _isTransportSeeking = true;
        }
    }

    private void PlaybackPositionSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isListenMode || _suppressTransportSliderChanged)
        {
            return;
        }

        RequestSeek();
    }

    private void PlaybackPositionSlider_LostMouseCapture(
        object sender,
        MouseEventArgs e)
        => _isTransportSeeking = false;

    private void RequestSeek()
    {
        var beat = Math.Clamp(
            PlaybackPositionSlider.Value,
            PlaybackPositionSlider.Minimum,
            PlaybackPositionSlider.Maximum);
        SeekRequested?.Invoke(this, new PerformanceSeekRequestedEventArgs(beat));
    }

    private void SpeedDownButton_Click(object sender, RoutedEventArgs e)
        => SpeedStepRequested?.Invoke(this, new PerformanceSpeedStepRequestedEventArgs(-1));

    private void SpeedUpButton_Click(object sender, RoutedEventArgs e)
        => SpeedStepRequested?.Invoke(this, new PerformanceSpeedStepRequestedEventArgs(1));

    private void MetronomeButton_Click(object sender, RoutedEventArgs e)
        => MetronomeToggleRequested?.Invoke(this, EventArgs.Empty);

    private void MetronomeVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var volumePercent = Math.Clamp((int)Math.Round(e.NewValue), 0, 100);
        if (MetronomeVolumeText is not null)
        {
            MetronomeVolumeText.Text = string.Format(CultureInfo.CurrentCulture, "{0}%", volumePercent);
        }

        if (!_suppressVolumeChanged)
        {
            MetronomeVolumeChanged?.Invoke(
                this,
                new PerformanceMetronomeVolumeRequestedEventArgs(volumePercent));
        }
    }

    private void MetronomePanel_MouseEnter(object sender, MouseEventArgs e)
    {
        _metronomePopupCloseTimer.Stop();
        MetronomePopup.IsOpen = true;
    }

    private void MetronomePanel_MouseLeave(object sender, MouseEventArgs e)
        => ScheduleMetronomePopupClose();

    private void MetronomePopup_MouseEnter(object sender, MouseEventArgs e)
        => _metronomePopupCloseTimer.Stop();

    private void MetronomePopup_MouseLeave(object sender, MouseEventArgs e)
        => ScheduleMetronomePopupClose();

    private void ScheduleMetronomePopupClose()
    {
        _metronomePopupCloseTimer.Stop();
        _metronomePopupCloseTimer.Start();
    }

    private void MetronomePopupCloseTimer_Tick(object? sender, EventArgs e)
    {
        _metronomePopupCloseTimer.Stop();
        MetronomePopup.IsOpen = false;
    }

    private void ResultBackButton_Click(object sender, RoutedEventArgs e)
        => ResultBackRequested?.Invoke(this, EventArgs.Empty);

    private static string FormatPlaybackTime(double seconds)
    {
        var totalSeconds = Math.Max(0, (int)Math.Floor(seconds));
        var minutes = totalSeconds / 60;
        var remainingSeconds = totalSeconds % 60;
        return $"{minutes:00}:{remainingSeconds:00}";
    }

    private static string GetModeText(PracticeMode mode)
        => mode switch
        {
            PracticeMode.WaitForCorrectNotes => "待機練習",
            PracticeMode.PlayAlong => "通常練習",
            PracticeMode.OriginalTempo => "原曲テンポ",
            _ => "お手本再生"
        };

    private static string GetHandText(PracticeHandMode handMode)
        => handMode switch
        {
            PracticeHandMode.Right => "右手",
            PracticeHandMode.Left => "左手",
            _ => "両手"
        };
}

public sealed class PerformanceSeekRequestedEventArgs : EventArgs
{
    public PerformanceSeekRequestedEventArgs(double beat)
    {
        if (double.IsNaN(beat) || double.IsInfinity(beat) || beat < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(beat));
        }

        Beat = beat;
    }

    public double Beat { get; }
}

public sealed class PerformanceMemorizationSeekRequestedEventArgs : EventArgs
{
    public PerformanceMemorizationSeekRequestedEventArgs(
        int direction,
        bool byMeasure)
    {
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction));
        }

        Direction = direction;
        ByMeasure = byMeasure;
    }

    public int Direction { get; }

    public bool ByMeasure { get; }
}

public sealed class PerformanceSpeedStepRequestedEventArgs : EventArgs
{
    public PerformanceSpeedStepRequestedEventArgs(int step)
    {
        if (step is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }

        Step = step;
    }

    public int Step { get; }
}

public sealed class PerformanceMetronomeVolumeRequestedEventArgs : EventArgs
{
    public PerformanceMetronomeVolumeRequestedEventArgs(int volumePercent)
    {
        VolumePercent = Math.Clamp(volumePercent, 0, 100);
    }

    public int VolumePercent { get; }
}
