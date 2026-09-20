using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PianoPracticeTool.Core;
using PianoPracticeTool.Services.Practice;
using PianoPracticeTool.Services.Songs;

namespace PianoPracticeTool.Views;

public sealed partial class PracticeSetupView : UserControl
{
    private SongCatalogEntry? _song;
    private KeyboardCompatibility? _compatibility;
    private IReadOnlyList<PracticeHistoryEntry> _history = Array.Empty<PracticeHistoryEntry>();
    private PracticeMode _selectedMode = PracticeMode.WaitForCorrectNotes;
    private PracticeHandMode _selectedHand = PracticeHandMode.Both;
    private bool _preferRecommendedDeviceOctaveShift;
    private bool _deviceOctaveShiftOverrideActive;
    private bool _updatingDeviceOctaveShiftOptions;
    private int _selectedDeviceOctaveShiftSemitones;

    public PracticeSetupView()
    {
        InitializeComponent();
        WaitModeRadio.IsChecked = true;
        BothHandRadio.IsChecked = true;
    }

    public event EventHandler? BackRequested;

    public event EventHandler<PracticeSetupRequestedEventArgs>? StartRequested;

    public void SetSong(
        SongCatalogEntry song,
        KeyboardCompatibility compatibility,
        IReadOnlyList<PracticeHistoryEntry> history,
        bool preferRecommendedDeviceOctaveShift = false,
        int? appliedDeviceOctaveShiftOverride = null,
        bool songOctaveCompressed = false)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentNullException.ThrowIfNull(compatibility);
        ArgumentNullException.ThrowIfNull(history);

        _song = song;
        _compatibility = compatibility;
        _history = history.ToArray();
        _preferRecommendedDeviceOctaveShift = preferRecommendedDeviceOctaveShift
            && compatibility.AvailableOctaveShiftsSemitones.Count > 1;
        _deviceOctaveShiftOverrideActive = appliedDeviceOctaveShiftOverride is int overrideShift
            && compatibility.AvailableOctaveShiftsSemitones.Contains(overrideShift);
        _selectedDeviceOctaveShiftSemitones = _deviceOctaveShiftOverrideActive
            ? appliedDeviceOctaveShiftOverride!.Value
            : 0;

        TitleText.Text = song.Title;
        SongInfoText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0} / {1} / {2} / {3}",
            song.DifficultyText,
            song.TempoText,
            song.DurationText,
            song.RangeText);
        SongCompressionBadge.Visibility = songOctaveCompressed
            ? Visibility.Visible
            : Visibility.Collapsed;
        KeyboardStatusText.Text = compatibility.Message;
        KeyboardStatusText.Foreground = compatibility.Level switch
        {
            KeyboardCompatibilityLevel.Full => (System.Windows.Media.Brush)FindResource("SuccessBrush"),
            KeyboardCompatibilityLevel.OneHandOnly => (System.Windows.Media.Brush)FindResource("WarningBrush"),
            _ => (System.Windows.Media.Brush)FindResource("DangerBrush")
        };

        var measures = BuildMeasureChoices(song);
        LoopStartComboBox.ItemsSource = measures;
        LoopEndComboBox.ItemsSource = measures;
        LoopStartComboBox.SelectedIndex = measures.Count > 0 ? 0 : -1;
        LoopEndComboBox.SelectedIndex = measures.Count > 0 ? measures.Count - 1 : -1;
        LoopEnabledCheckBox.IsChecked = false;
        LoopPanel.IsEnabled = false;

        _selectedMode = PracticeMode.WaitForCorrectNotes;
        _selectedHand = PracticeHandMode.Both;
        WaitModeRadio.IsChecked = true;
        BothHandRadio.IsChecked = true;
        ApplyAutomaticDeviceOctaveShift();
        UpdateModeUi();
        UpdateKeyboardPlan();
        RefreshHistory();
    }

    private static IReadOnlyList<MeasureChoice> BuildMeasureChoices(SongCatalogEntry song)
    {
        if (song.Score.Measures.Count == 0)
        {
            return new[]
            {
                new MeasureChoice(1, 0d, Math.Max(1d, song.Score.LengthBeats), "全曲")
            };
        }

        return song.Score.Measures
            .Select(measure => new MeasureChoice(
                measure.Number,
                measure.StartBeat,
                measure.EndBeat,
                $"小節 {measure.Number}"))
            .ToArray();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
        => BackRequested?.Invoke(this, EventArgs.Empty);

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_song is null)
        {
            return;
        }

        var handMode = GetEffectiveHandMode();
        StartRequested?.Invoke(
            this,
            new PracticeSetupRequestedEventArgs(
                new PracticeSetupOptions(
                    _selectedMode,
                    handMode,
                    GetLoopRange(),
                    1d,
                    _selectedDeviceOctaveShiftSemitones,
                    FollowRecommendedDeviceOctaveShift: _preferRecommendedDeviceOctaveShift
                        && !_deviceOctaveShiftOverrideActive)));
    }

    private void ListenButton_Click(object sender, RoutedEventArgs e)
    {
        if (_song is null)
        {
            return;
        }

        StartRequested?.Invoke(
            this,
            new PracticeSetupRequestedEventArgs(
                new PracticeSetupOptions(
                    PracticeMode.Listen,
                    PracticeHandMode.Both,
                    null,
                    1d,
                    _selectedDeviceOctaveShiftSemitones,
                    FollowRecommendedDeviceOctaveShift: false)));
    }

    private PracticeLoopRange? GetLoopRange()
    {
        if (LoopEnabledCheckBox.IsChecked != true
            || LoopStartComboBox.SelectedItem is not MeasureChoice start
            || LoopEndComboBox.SelectedItem is not MeasureChoice end
            || end.EndBeat <= start.StartBeat)
        {
            return null;
        }

        return new PracticeLoopRange(start.StartBeat, end.EndBeat);
    }

    private void PracticeMode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radioButton)
        {
            return;
        }

        _selectedMode = radioButton.Name switch
        {
            nameof(PlayAlongModeRadio) => PracticeMode.PlayAlong,
            nameof(OriginalTempoModeRadio) => PracticeMode.OriginalTempo,
            _ => PracticeMode.WaitForCorrectNotes
        };

        ApplyAutomaticDeviceOctaveShift();
        UpdateModeUi();
        UpdateKeyboardPlan();
        RefreshHistory();
    }

    private void HandMode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radioButton)
        {
            return;
        }

        _selectedHand = radioButton.Name switch
        {
            nameof(RightHandRadio) => PracticeHandMode.Right,
            nameof(LeftHandRadio) => PracticeHandMode.Left,
            _ => PracticeHandMode.Both
        };

        ApplyAutomaticDeviceOctaveShift();
        UpdateKeyboardPlan();
        RefreshHistory();
    }

    private void LoopEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (LoopPanel is not null)
        {
            LoopPanel.IsEnabled = LoopEnabledCheckBox.IsChecked == true;
        }
    }

    private void DeviceOctaveShiftOption_Checked(object sender, RoutedEventArgs e)
    {
        if (_updatingDeviceOctaveShiftOptions
            || _compatibility is null
            || sender is not RadioButton { Tag: int shift })
        {
            return;
        }

        var recommendedShift = _compatibility
            .ForHand(GetEffectiveHandMode())
            .RecommendedOctaveShiftSemitones;
        _selectedDeviceOctaveShiftSemitones = shift;
        _preferRecommendedDeviceOctaveShift = shift == recommendedShift;
        _deviceOctaveShiftOverrideActive = shift != recommendedShift;
        UpdateKeyboardPlan();
    }

    private void ApplyAutomaticDeviceOctaveShift()
    {
        if (_compatibility is null || _deviceOctaveShiftOverrideActive)
        {
            return;
        }

        var plan = _compatibility.ForHand(GetEffectiveHandMode());
        _selectedDeviceOctaveShiftSemitones = _preferRecommendedDeviceOctaveShift
            ? plan.RecommendedOctaveShiftSemitones
            : 0;
    }

    private void UpdateModeUi()
    {
        if (HandSelectionPanel is null || StartPracticeButton is null || SetupHintText is null)
        {
            return;
        }

        var supportsHandSelection = _selectedMode is PracticeMode.WaitForCorrectNotes or PracticeMode.PlayAlong;
        HandSelectionPanel.Visibility = supportsHandSelection
            ? Visibility.Visible
            : Visibility.Collapsed;

        switch (_selectedMode)
        {
            case PracticeMode.WaitForCorrectNotes:
                StartPracticeButton.Content = "待機練習を開始";
                SetupHintText.Text = "正しい音を確認しながら、自分のペースで進めます。";
                break;
            case PracticeMode.PlayAlong:
                StartPracticeButton.Content = "通常練習を開始";
                SetupHintText.Text = "テンポ変更可能。タイミング・誤キー・見逃しを採点します。";
                break;
            case PracticeMode.OriginalTempo:
                StartPracticeButton.Content = "原曲テンポで開始";
                SetupHintText.Text = "原曲100%・両手固定で通して演奏します。";
                break;
        }
    }

    private void UpdateKeyboardPlan()
    {
        if (KeyboardPlanText is null
            || HandWarningText is null
            || DeviceOctaveShiftControlsPanel is null
            || DeviceOctaveShiftOptionsPanel is null
            || AppliedDeviceOctaveShiftText is null
            || DeviceOctaveShiftHintText is null
            || _compatibility is null)
        {
            return;
        }

        var handMode = GetEffectiveHandMode();
        var plan = _compatibility.ForHand(handMode);
        var recommendedShift = plan.RecommendedOctaveShiftSemitones;
        var autoPlayedCount = plan.GetAutoPlayedNoteCount(_selectedDeviceOctaveShiftSemitones);

        KeyboardPlanText.Text = _selectedDeviceOctaveShiftSemitones == recommendedShift
            ? $"{GetHandText(handMode)} / MIDIシフト {FormatDeviceOctaveShift(_selectedDeviceOctaveShiftSemitones)}（推奨）"
            : $"{GetHandText(handMode)} / MIDIシフト {FormatDeviceOctaveShift(_selectedDeviceOctaveShiftSemitones)} / 推奨 {FormatDeviceOctaveShift(recommendedShift)}";

        HandWarningText.Text = autoPlayedCount > 0
            ? $"この設定では鍵盤範囲外の {autoPlayedCount:N0} 音を自動演奏します。これらの音は採点対象外となり、BESTスコアには記録されません。"
            : "選択した演奏パートの全音域をMIDIキーボードで演奏できます。";
        HandWarningText.Foreground = autoPlayedCount > 0
            ? (System.Windows.Media.Brush)FindResource("WarningBrush")
            : (System.Windows.Media.Brush)FindResource("SuccessBrush");

        var shifts = _compatibility.AvailableOctaveShiftsSemitones;
        var showShiftControls = shifts.Count > 1;
        DeviceOctaveShiftControlsPanel.Visibility = showShiftControls
            ? Visibility.Visible
            : Visibility.Collapsed;
        DeviceOctaveShiftHintText.Visibility = showShiftControls
            ? Visibility.Visible
            : Visibility.Collapsed;
        AppliedDeviceOctaveShiftText.Text = $"選択: {FormatDeviceOctaveShift(_selectedDeviceOctaveShiftSemitones)}";
        RebuildDeviceOctaveShiftOptions(shifts, recommendedShift);
    }

    private void RebuildDeviceOctaveShiftOptions(
        IReadOnlyList<int> shifts,
        int recommendedShift)
    {
        _updatingDeviceOctaveShiftOptions = true;
        try
        {
            DeviceOctaveShiftOptionsPanel.Children.Clear();
            foreach (var shift in shifts)
            {
                var option = new RadioButton
                {
                    Content = shift == recommendedShift
                        ? $"{FormatDeviceOctaveShift(shift)}  推奨"
                        : FormatDeviceOctaveShift(shift),
                    Tag = shift,
                    GroupName = "OctaveShiftAmount",
                    Style = (Style)FindResource("HandOptionStyle"),
                    IsChecked = shift == _selectedDeviceOctaveShiftSemitones
                };
                option.Checked += DeviceOctaveShiftOption_Checked;
                DeviceOctaveShiftOptionsPanel.Children.Add(option);
            }
        }
        finally
        {
            _updatingDeviceOctaveShiftOptions = false;
        }
    }

    private void RefreshHistory()
    {
        if (HistoryItemsControl is null || HistorySummaryText is null)
        {
            return;
        }

        var handMode = GetEffectiveHandMode();
        var filtered = _history
            .Where(entry => entry.Result.Mode == _selectedMode && entry.Result.HandMode == handMode)
            .Take(100)
            .ToArray();

        HistoryItemsControl.ItemsSource = filtered
            .Select(entry => new HistoryRow(entry.Result))
            .ToArray();
        HistorySummaryText.Text = filtered.Length == 0
            ? "この条件の練習記録はまだありません。"
            : string.Format(
                CultureInfo.CurrentCulture,
                "{0} / {1}: 記録 {2:N0} 回",
                GetModeText(_selectedMode),
                GetHandText(handMode),
                _history.Count(entry =>
                    entry.Result.Mode == _selectedMode
                    && entry.Result.HandMode == handMode));
    }

    private PracticeHandMode GetEffectiveHandMode()
        => _selectedMode == PracticeMode.OriginalTempo
            ? PracticeHandMode.Both
            : _selectedHand;

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

    private static string FormatDeviceOctaveShift(int semitones)
        => semitones == 0
            ? "なし"
            : $"{semitones / 12:+#;-#;0} oct";

    private sealed record MeasureChoice(int Number, double StartBeat, double EndBeat, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed class HistoryRow
    {
        public HistoryRow(PracticeResult result)
        {
            DateText = result.CompletedAtUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.CurrentCulture);
            var assistedSuffix = result.AutoPlayedTargetNoteCount > 0 ? " 補助" : string.Empty;
            ScoreText = string.Format(
                CultureInfo.CurrentCulture,
                "{0:0.0} / 100{1}",
                PracticeScoring.ToHundredPointScore(result.Score),
                assistedSuffix);
            TempoText = result.WaitTempo is null
                ? string.Format(CultureInfo.CurrentCulture, "{0:0}%", result.SpeedMultiplier * 100d)
                : string.Format(
                    CultureInfo.CurrentCulture,
                    "{0:0} BPM / {1:0}%",
                    result.WaitTempo.AverageBpm,
                    result.WaitTempo.OriginalTempoPercent);
        }

        public string DateText { get; }

        public string ScoreText { get; }

        public string TempoText { get; }
    }
}

public sealed class PracticeSetupRequestedEventArgs : EventArgs
{
    public PracticeSetupRequestedEventArgs(PracticeSetupOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public PracticeSetupOptions Options { get; }
}
