using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PianoPracticeTool.Core;
using PianoPracticeTool.Services.Practice;
using PianoPracticeTool.Services.Songs;

namespace PianoPracticeTool.Views;

public sealed partial class PracticeView : UserControl
{
    private static readonly IReadOnlyList<PracticeModeChoice> PracticeModeChoices = new[]
    {
        new PracticeModeChoice(PracticeMode.WaitForCorrectNotes, "待機練習"),
        new PracticeModeChoice(PracticeMode.PlayAlong, "通常練習"),
        new PracticeModeChoice(PracticeMode.OriginalTempo, "原曲テンポ固定"),
        new PracticeModeChoice(PracticeMode.Listen, "お手本再生")
    };

    private static readonly IReadOnlyList<PracticeHandModeChoice> HandModeChoices = new[]
    {
        new PracticeHandModeChoice(PracticeHandMode.Both, "両手"),
        new PracticeHandModeChoice(PracticeHandMode.Right, "右手"),
        new PracticeHandModeChoice(PracticeHandMode.Left, "左手")
    };

    private bool _suppressSettingsChanged;
    private int _suggestedTransposeSemitones;

    public PracticeView()
    {
        _suppressSettingsChanged = true;
        InitializeComponent();

        try
        {
            PracticeModeComboBox.ItemsSource = PracticeModeChoices;
            PracticeModeComboBox.SelectedIndex = 0;
            HandModeComboBox.ItemsSource = HandModeChoices;
            HandModeComboBox.SelectedIndex = 0;
            SpeedSlider.Value = 100d;
            PianoRollVisibleRangeSlider.Value = 100d;
            MetronomeVolumeSlider.Value = 100d;
            PianoRoll.VisibleRangeMultiplier = 1d;
            FingeringDisplayCheckBox.IsChecked = true;
            PianoRoll.ShowFingering = true;
        }
        finally
        {
            _suppressSettingsChanged = false;
        }

        SpeedValueText.Text = "100%";
        CurrentBpmText.Text = "— BPM";
        PianoRollVisibleRangeValueText.Text = "8 beat";
        MetronomeVolumeValueText.Text = "100%";
    }

    public event EventHandler? BackRequested;
    public event EventHandler? PlayPauseRequested;
    public event EventHandler? ResetRequested;
    public event EventHandler<PracticeModeChangedEventArgs>? PracticeModeChanged;
    public event EventHandler<PracticeHandModeChangedEventArgs>? HandModeChanged;
    public event EventHandler<PracticeSpeedChangedEventArgs>? SpeedChanged;
    public event EventHandler<PracticeMetronomeChangedEventArgs>? MetronomeChanged;
    public event EventHandler<PracticeMetronomeVolumeChangedEventArgs>? MetronomeVolumeChanged;
    public event EventHandler<PracticeLoopChangedEventArgs>? LoopChanged;
    public event EventHandler<PracticeSeekRequestedEventArgs>? SeekRequested;
    public event EventHandler<PracticeTransposeChangedEventArgs>? InputTransposeChanged;
    public event EventHandler? FingeringChanged;

    public void SetSong(SongCatalogEntry song)
    {
        ArgumentNullException.ThrowIfNull(song);

        PianoRoll.Score = song.Score;
        PianoRoll.CurrentBeat = null;
        PianoRoll.ExpectedMidiNotes = Array.Empty<int>();
        PianoRoll.ActiveMidiNotes = Array.Empty<int>();
        PianoRoll.HandMode = PracticeHandMode.Both;
        PianoRoll.VisibleRangeMultiplier = 1d;
        NoteGrid.ItemsSource = song.Score.Notes;

        TitleText.Text = song.Title;
        SongSummaryText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0}  •  {1}  •  {2}  •  音域 {3}",
            song.TempoText,
            song.DurationText,
            song.NoteCountText,
            song.RangeText);
        KeyboardText.Text = song.RecommendedKeyboardText;
        KeyboardMinimumText.Text = song.MinimumKeyboardText;
        HandSpanWarningText.Text = song.HasWideHandSpanWarning
            ? "1オクターブ超の同時発音あり"
            : string.Empty;
        PathText.Text = song.FilePath;

        _suppressSettingsChanged = true;
        try
        {
            PracticeModeComboBox.SelectedIndex = 0;
            HandModeComboBox.SelectedIndex = 0;
            SpeedSlider.Value = 100d;
            SpeedSlider.IsEnabled = true;
            PianoRollVisibleRangeSlider.Value = 100d;
            MetronomeVolumeSlider.Value = 100d;
            MetronomeCheckBox.IsChecked = false;
            LoopEnabledCheckBox.IsChecked = false;

            var measures = BuildMeasureChoices(song.Score);
            LoopStartComboBox.ItemsSource = measures;
            LoopEndComboBox.ItemsSource = measures;
            LoopStartComboBox.SelectedIndex = measures.Count > 0 ? 0 : -1;
            LoopEndComboBox.SelectedIndex = measures.Count > 0 ? measures.Count - 1 : -1;

            _suggestedTransposeSemitones = song.Keyboard.CanReduceWithOctaveShift
                ? song.Keyboard.SuggestedTransposeSemitones
                : 0;
            TransposePanel.Visibility = song.Keyboard.CanReduceWithOctaveShift
                ? Visibility.Visible
                : Visibility.Collapsed;
            InputTransposeCheckBox.IsChecked = false;
            InputTransposeCheckBox.Content = song.Keyboard.CanReduceWithOctaveShift
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    "{0:+#;-#;0}半音 / {1}鍵",
                    song.Keyboard.SuggestedTransposeSemitones,
                    song.Keyboard.MinimumStandardKeyCount)
                : string.Empty;
        }
        finally
        {
            _suppressSettingsChanged = false;
        }

        SpeedValueText.Text = "100%";
        CurrentBpmText.Text = string.Format(CultureInfo.CurrentCulture, "{0:0} BPM", song.Score.GetTempoAt(0d));
        PianoRollVisibleRangeValueText.Text = "8 beat";
        MetronomeVolumeValueText.Text = "100%";
        TimelineSlider.Maximum = Math.Max(1d, song.Score.LengthBeats);
        TimelineSlider.Value = 0d;
        PracticeStatusText.Text = "準備完了。開始するとテンポに応じた短いカウントイン後に練習します。";
        ExpectedNotesText.Text = "—";
        PracticeProgressText.Text = "0 / 0";
        AccuracyText.Text = string.Empty;
        PracticeProgressBar.Value = 0d;
        CurrentTimeText.Text = "0:00";
        TotalTimeText.Text = song.DurationText;
        PositionText.Text = song.Score.Measures.Count > 0 ? $"小節 {song.Score.Measures[0].Number}" : string.Empty;
        PlayPauseButton.Content = "開始";
        ResetPracticeButton.IsEnabled = false;
        JudgementOverlay.Visibility = Visibility.Collapsed;
    }

    public void SetActiveMidiNotes(IReadOnlyCollection<int> midiNotes)
    {
        ArgumentNullException.ThrowIfNull(midiNotes);
        PianoRoll.ActiveMidiNotes = midiNotes.ToArray();
    }

    public void SetSoundStatus(bool isAvailable, string deviceName)
    {
        SoundStatusText.Text = isAvailable
            ? $"音源: {deviceName}"
            : "音源: 利用不可（MIDI入力・採点は使用可能）";
        SoundStatusText.Foreground = isAvailable
            ? (Brush)FindResource("TextMutedBrush")
            : (Brush)FindResource("WarningBrush");
    }

    public void SetPracticeSnapshot(PracticeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        PracticeStatusText.Text = snapshot.StatusMessage;
        ExpectedNotesText.Text = snapshot.ExpectedMidiNotes.Count == 0
            ? "—"
            : string.Join(" + ", snapshot.ExpectedMidiNotes.Select(MidiPitch.ToName));

        if (IsPerformanceMode(snapshot.Mode))
        {
            PracticeProgressText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0} HIT / {1} MISS / {2}",
                snapshot.CorrectCount,
                snapshot.MissCount,
                snapshot.TotalTargetNotes);
            AccuracyText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "PERFECT {0}  GREAT {1}  GOOD {2}  MISS {3}  |  MAX {4} COMBO  |  精度 {5:P0}",
                snapshot.PerfectCount,
                snapshot.GreatCount,
                snapshot.GoodCount,
                snapshot.MissCount,
                snapshot.MaxCombo,
                snapshot.AccuracyRatio);
        }
        else if (snapshot.Mode == PracticeMode.WaitForCorrectNotes)
        {
            PracticeProgressText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0} / {1}",
                snapshot.CorrectCount,
                snapshot.TotalTargetNotes);
            AccuracyText.Text = string.Empty;
        }
        else
        {
            PracticeProgressText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0:P0}",
                snapshot.ProgressRatio);
            AccuracyText.Text = string.Empty;
        }

        UpdateJudgementOverlay(snapshot);

        PracticeProgressBar.Value = Math.Clamp(snapshot.ProgressRatio * 100d, 0d, 100d);
        PlayPauseButton.Content = snapshot.State switch
        {
            PracticeRunState.Paused => "再開",
            PracticeRunState.CountingIn or PracticeRunState.Playing or PracticeRunState.WaitingForInput => "一時停止",
            _ => "開始"
        };
        ResetPracticeButton.IsEnabled = snapshot.State != PracticeRunState.Ready;

        // PianoRoll.CurrentBeat is intentionally not updated from a sampled snapshot.
        // The roll position has a single owner: the render path reading PracticeTransportClock.
        PianoRoll.ExpectedMidiNotes = snapshot.ExpectedMidiNotes;
        PianoRoll.HandMode = snapshot.HandMode;

        CurrentTimeText.Text = FormatTime(snapshot.CurrentSeconds);
        TotalTimeText.Text = FormatTime(snapshot.TotalSeconds);
        PositionText.Text = snapshot.State == PracticeRunState.CountingIn
            ? "カウントイン"
            : snapshot.CurrentMeasureNumber > 0
                ? $"小節 {snapshot.CurrentMeasureNumber}"
                : string.Empty;

        _suppressSettingsChanged = true;
        try
        {
            SpeedSlider.Value = Math.Clamp(snapshot.SpeedMultiplier * 100d, SpeedSlider.Minimum, SpeedSlider.Maximum);
            SpeedSlider.IsEnabled = snapshot.Mode != PracticeMode.OriginalTempo;
            MetronomeVolumeSlider.Value = snapshot.MetronomeVolumePercent;
            TimelineSlider.Maximum = Math.Max(1d, snapshot.LengthBeats);
            TimelineSlider.Value = Math.Clamp(snapshot.CurrentBeat, 0d, TimelineSlider.Maximum);
        }
        finally
        {
            _suppressSettingsChanged = false;
        }

        SpeedValueText.Text = string.Format(CultureInfo.CurrentCulture, "{0:0}%", snapshot.SpeedMultiplier * 100d);
        CurrentBpmText.Text = string.Format(CultureInfo.CurrentCulture, "{0:0} BPM", snapshot.CurrentTempoBpm);
        MetronomeVolumeValueText.Text = string.Format(CultureInfo.CurrentCulture, "{0}%", snapshot.MetronomeVolumePercent);
    }

    private void UpdateJudgementOverlay(PracticeSnapshot snapshot)
    {
        if (!IsPerformanceMode(snapshot.Mode) || snapshot.LastJudgement is null)
        {
            JudgementOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        JudgementOverlay.Visibility = Visibility.Visible;
        JudgementActionText.Text = snapshot.LastJudgementWasRelease ? "KEY UP / 離鍵" : "KEY DOWN / 押鍵";
        JudgementText.Text = snapshot.LastJudgement.Value.ToString().ToUpperInvariant();
        ComboOverlayText.Text = snapshot.CurrentCombo > 1
            ? $"{snapshot.CurrentCombo} COMBO"
            : snapshot.MaxCombo > 0
                ? $"MAX {snapshot.MaxCombo} COMBO"
                : string.Empty;
        JudgementText.Foreground = snapshot.LastJudgement.Value switch
        {
            PerformanceJudgement.Perfect => (Brush)FindResource("SuccessBrush"),
            PerformanceJudgement.Great => (Brush)FindResource("AccentTextBrush"),
            PerformanceJudgement.Good => (Brush)FindResource("WarningBrush"),
            _ => (Brush)FindResource("DangerBrush")
        };
    }

    private static bool IsPerformanceMode(PracticeMode mode)
        => mode is PracticeMode.PlayAlong or PracticeMode.OriginalTempo;

    private static IReadOnlyList<MeasureChoice> BuildMeasureChoices(MusicScore score)
    {
        if (score.Measures.Count == 0)
        {
            return new[]
            {
                new MeasureChoice(1, 0d, Math.Max(score.LengthBeats, 1d), "全曲")
            };
        }

        return score.Measures
            .Select(measure => new MeasureChoice(
                measure.Number,
                measure.StartBeat,
                measure.EndBeat,
                $"小節 {measure.Number}"))
            .ToArray();
    }

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        return time.TotalHours >= 1d
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private static int SnapPercentage(double value, int minimum, int maximum)
    {
        var snapped = (int)Math.Round(value / 10d) * 10;
        return Math.Clamp(snapped, minimum, maximum);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        PlayPauseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        ResetRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PracticeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsChanged || PracticeModeComboBox.SelectedItem is not PracticeModeChoice choice)
        {
            return;
        }

        PracticeModeChanged?.Invoke(this, new PracticeModeChangedEventArgs(choice.Mode));
    }

    private void HandModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsChanged || HandModeComboBox.SelectedItem is not PracticeHandModeChoice choice)
        {
            return;
        }

        HandModeChanged?.Invoke(this, new PracticeHandModeChangedEventArgs(choice.Mode));
    }

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var percentage = SnapPercentage(e.NewValue, 50, 200);
        if (SpeedValueText is not null)
        {
            SpeedValueText.Text = string.Format(CultureInfo.CurrentCulture, "{0}%", percentage);
        }

        if (_suppressSettingsChanged)
        {
            return;
        }

        SpeedChanged?.Invoke(this, new PracticeSpeedChangedEventArgs(percentage / 100d));
    }

    private void PianoRollVisibleRangeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        var percentage = SnapPercentage(e.NewValue, 50, 200);
        if (PianoRollVisibleRangeValueText is not null)
        {
            var visibleBeats = 8d * percentage / 100d;
            PianoRollVisibleRangeValueText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0:0.#} beat",
                visibleBeats);
        }

        if (PianoRoll is not null)
        {
            PianoRoll.VisibleRangeMultiplier = percentage / 100d;
        }
    }

    private void MetronomeVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var percentage = SnapPercentage(e.NewValue, 0, 100);
        if (MetronomeVolumeValueText is not null)
        {
            MetronomeVolumeValueText.Text = string.Format(CultureInfo.CurrentCulture, "{0}%", percentage);
        }

        if (_suppressSettingsChanged)
        {
            return;
        }

        MetronomeVolumeChanged?.Invoke(this, new PracticeMetronomeVolumeChangedEventArgs(percentage));
    }

    private void MetronomeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsChanged)
        {
            return;
        }

        MetronomeChanged?.Invoke(
            this,
            new PracticeMetronomeChangedEventArgs(MetronomeCheckBox.IsChecked == true));
    }

    private void FingeringDisplayCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (PianoRoll is not null)
        {
            PianoRoll.ShowFingering = FingeringDisplayCheckBox.IsChecked == true;
        }
    }

    private void LoopSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsChanged)
        {
            return;
        }

        if (LoopEnabledCheckBox.IsChecked != true)
        {
            LoopChanged?.Invoke(this, new PracticeLoopChangedEventArgs(null));
            return;
        }

        if (LoopStartComboBox.SelectedItem is not MeasureChoice start
            || LoopEndComboBox.SelectedItem is not MeasureChoice end
            || end.EndBeat <= start.StartBeat + ScoreTiming.EventBeatTolerance)
        {
            return;
        }

        LoopChanged?.Invoke(
            this,
            new PracticeLoopChangedEventArgs(new PracticeLoopRange(start.StartBeat, end.EndBeat)));
    }

    private void InputTransposeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsChanged)
        {
            return;
        }

        var semitones = InputTransposeCheckBox.IsChecked == true
            ? _suggestedTransposeSemitones
            : 0;
        InputTransposeChanged?.Invoke(this, new PracticeTransposeChangedEventArgs(semitones));
    }

    private void TimelineSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_suppressSettingsChanged)
        {
            return;
        }

        SeekRequested?.Invoke(this, new PracticeSeekRequestedEventArgs(TimelineSlider.Value));
    }

    private void NoteGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (e.Row.Item is ScoreNote note)
            {
                note.Finger = Math.Clamp(note.Finger, 1, 5);
                FingeringChanged?.Invoke(this, EventArgs.Empty);
            }

            PianoRoll.InvalidateScoreDrawing();
        });
    }

    private sealed record PracticeModeChoice(PracticeMode Mode, string Label);
    private sealed record PracticeHandModeChoice(PracticeHandMode Mode, string Label);
    private sealed record MeasureChoice(int Number, double StartBeat, double EndBeat, string Label);
}

public sealed class PracticeModeChangedEventArgs : EventArgs
{
    public PracticeModeChangedEventArgs(PracticeMode mode)
    {
        Mode = mode;
    }

    public PracticeMode Mode { get; }
}

public sealed class PracticeHandModeChangedEventArgs : EventArgs
{
    public PracticeHandModeChangedEventArgs(PracticeHandMode handMode)
    {
        HandMode = handMode;
    }

    public PracticeHandMode HandMode { get; }
}

public sealed class PracticeSpeedChangedEventArgs : EventArgs
{
    public PracticeSpeedChangedEventArgs(double multiplier)
    {
        Multiplier = multiplier;
    }

    public double Multiplier { get; }
}

public sealed class PracticeMetronomeChangedEventArgs : EventArgs
{
    public PracticeMetronomeChangedEventArgs(bool isEnabled)
    {
        IsEnabled = isEnabled;
    }

    public bool IsEnabled { get; }
}

public sealed class PracticeMetronomeVolumeChangedEventArgs : EventArgs
{
    public PracticeMetronomeVolumeChangedEventArgs(int volumePercent)
    {
        VolumePercent = volumePercent;
    }

    public int VolumePercent { get; }
}

public sealed class PracticeLoopChangedEventArgs : EventArgs
{
    public PracticeLoopChangedEventArgs(PracticeLoopRange? loopRange)
    {
        LoopRange = loopRange;
    }

    public PracticeLoopRange? LoopRange { get; }
}

public sealed class PracticeSeekRequestedEventArgs : EventArgs
{
    public PracticeSeekRequestedEventArgs(double beat)
    {
        Beat = beat;
    }

    public double Beat { get; }
}

public sealed class PracticeTransposeChangedEventArgs : EventArgs
{
    public PracticeTransposeChangedEventArgs(int semitones)
    {
        Semitones = semitones;
    }

    public int Semitones { get; }
}
