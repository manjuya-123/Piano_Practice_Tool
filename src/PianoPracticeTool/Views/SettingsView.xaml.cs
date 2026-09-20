using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PianoPracticeTool.Core;
using PianoPracticeTool.Services.Midi;
using PianoPracticeTool.Services.Settings;

namespace PianoPracticeTool.Views;

public sealed partial class SettingsView : UserControl
{
    private const double TimingBandWidth = 520d;
    private const double AttackTimingDisplayHalfRangeSeconds = 0.35d;
    private const double ReleaseTimingDisplayHalfRangeSeconds = 0.70d;

    private static readonly TimelineDragModeChoice[] TimelineDragModeChoices =
    {
        new(EditorTimelineDragMode.TouchScroll, "タッチスクロール（惰性あり）"),
        new(EditorTimelineDragMode.DirectFollow, "直接追従（離した位置で停止）")
    };

    private readonly List<string> _songFolders = new();
    private readonly List<SoundFontPresetInfo> _soundFontPresets = new();
    private AppSettings _baseSettings = new();
    private bool _updatingKeyboardRange;
    private bool _keyboardRangeCaptureActive;
    private int? _capturedFirstMidiNote;

    public SettingsView()
    {
        InitializeComponent();
        EditorTimelineDragModeComboBox.ItemsSource = TimelineDragModeChoices;
    }

    public event EventHandler? RefreshMidiRequested;

    public event EventHandler<SoundFontConfigurationEventArgs>? BuiltInSoundFontPreviewChanged;

    public event EventHandler<SoundFontConfigurationEventArgs>? BuiltInSoundFontTestRequested;

    public event EventHandler<SettingsSaveRequestedEventArgs>? SaveRequested;

    public bool IsKeyboardRangeCaptureActive
        => Volatile.Read(ref _keyboardRangeCaptureActive);

    public void SetState(
        AppSettings settings,
        IReadOnlyList<string> midiDeviceNames,
        IReadOnlyList<string> midiOutputDeviceNames,
        string midiStatus)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(midiDeviceNames);
        ArgumentNullException.ThrowIfNull(midiOutputDeviceNames);

        _baseSettings = settings.Clone();
        MidiDeviceComboBox.ItemsSource = midiDeviceNames;
        MidiDeviceComboBox.SelectedIndex = FindDeviceIndex(midiDeviceNames, settings.PreferredMidiDeviceName);
        MidiOutputDeviceComboBox.ItemsSource = midiOutputDeviceNames;
        MidiOutputDeviceComboBox.SelectedIndex = FindNamedDeviceIndex(
            midiOutputDeviceNames,
            settings.PreferredMidiOutputDeviceName);
        AutoConnectMidiCheckBox.IsChecked = settings.AutoConnectMidi;
        SetSoundFontState(settings);
        SetKeyboardRange(settings.KeyboardLowestMidi, settings.KeyboardHighestMidi);
        HasOctaveShiftCheckBox.IsChecked = settings.HasOctaveShift;

        _songFolders.Clear();
        _songFolders.AddRange(settings.SongFolders);
        RefreshSongFolderList();

        JudgementTimingSlider.Value = settings.JudgementTimingPercent;
        BuiltInSoundFontVolumeSlider.Value = settings.BuiltInSoundFontVolumePercent;
        BuiltInSoundFontReverbSlider.Value = settings.BuiltInSoundFontReverbPercent;
        AutomaticPlaybackVelocitySlider.Value = settings.AutomaticPlaybackVelocity;
        PianoRollVisibleRangeSlider.Value = settings.PianoRollVisibleRangePercent;
        MetronomeVolumeSlider.Value = settings.MetronomeVolumePercent;
        ShowHandColorsCheckBox.IsChecked = settings.ShowHandColors;
        ShowFingeringCheckBox.IsChecked =
            settings.ShowHandColors
            && settings.ShowFingering;
        UpdateFingeringSettingState();
        ShowExpectedKeyboardGuideCheckBox.IsChecked = settings.ShowExpectedKeyboardGuide;
        EditorTimelineDragModeComboBox.SelectedItem =
            TimelineDragModeChoices.First(choice =>
                choice.Mode == settings.EditorTimelineDragMode);
        EditorPanPitchEnabledCheckBox.IsChecked =
            settings.EditorPanPitchEnabled;
        UpdatePercentLabels();
        UpdateTimingVisualization();
        StopKeyboardRangeCapture("最低音と最高音を実機から取得できます。");
        StatusText.Text = midiStatus;
    }

    public void SetStatus(string status)
    {
        StatusText.Text = status ?? string.Empty;
    }

    public void UpdateMidiState(
        IReadOnlyList<string> midiDeviceNames,
        IReadOnlyList<string> midiOutputDeviceNames,
        string midiStatus)
    {
        ArgumentNullException.ThrowIfNull(midiDeviceNames);
        ArgumentNullException.ThrowIfNull(midiOutputDeviceNames);

        var preferredInputName = MidiDeviceComboBox.SelectedItem as string
            ?? _baseSettings.PreferredMidiDeviceName;
        MidiDeviceComboBox.ItemsSource = midiDeviceNames;
        MidiDeviceComboBox.SelectedIndex = FindDeviceIndex(midiDeviceNames, preferredInputName);

        var preferredOutputName = MidiOutputDeviceComboBox.SelectedItem as string
            ?? _baseSettings.PreferredMidiOutputDeviceName;
        MidiOutputDeviceComboBox.ItemsSource = midiOutputDeviceNames;
        MidiOutputDeviceComboBox.SelectedIndex = FindNamedDeviceIndex(
            midiOutputDeviceNames,
            preferredOutputName);
        StatusText.Text = midiStatus;
    }

    public void CaptureKeyboardRangeMidiNote(int midiNote)
    {
        if (!IsKeyboardRangeCaptureActive)
        {
            return;
        }

        var normalizedMidiNote = Math.Clamp(midiNote, 0, 127);
        if (_capturedFirstMidiNote is null)
        {
            _capturedFirstMidiNote = normalizedMidiNote;
            KeyboardRangeCaptureStatusText.Text =
                $"1音目: {MidiPitch.ToName(normalizedMidiNote)}。反対側の端の鍵盤を押してください。";
            return;
        }

        var firstMidiNote = _capturedFirstMidiNote.Value;
        if (firstMidiNote == normalizedMidiNote)
        {
            KeyboardRangeCaptureStatusText.Text =
                $"{MidiPitch.ToName(normalizedMidiNote)} は取得済みです。反対側の端の鍵盤を押してください。";
            return;
        }

        var lowest = Math.Min(firstMidiNote, normalizedMidiNote);
        var highest = Math.Max(firstMidiNote, normalizedMidiNote);
        SetKeyboardRange(lowest, highest);
        StopKeyboardRangeCapture(
            $"検出完了: {MidiPitch.ToName(lowest)} ～ {MidiPitch.ToName(highest)} / {highest - lowest + 1}鍵");
    }

    private static int FindNamedDeviceIndex(IReadOnlyList<string> names, string? preferredName)
    {
        if (string.IsNullOrWhiteSpace(preferredName))
        {
            return -1;
        }

        for (var index = 0; index < names.Count; index++)
        {
            if (string.Equals(names[index], preferredName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindDeviceIndex(IReadOnlyList<string> names, string? preferredName)
    {
        if (names.Count == 0)
        {
            return -1;
        }

        if (string.IsNullOrWhiteSpace(preferredName))
        {
            return 0;
        }

        for (var index = 0; index < names.Count; index++)
        {
            if (string.Equals(names[index], preferredName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }

    private void ShowHandColorsCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
        => UpdateFingeringSettingState();

    private void UpdateFingeringSettingState()
    {
        if (ShowHandColorsCheckBox is null
            || ShowFingeringCheckBox is null)
        {
            return;
        }

        var handColorsEnabled =
            ShowHandColorsCheckBox.IsChecked
            == true;
        ShowFingeringCheckBox.IsEnabled =
            handColorsEnabled;
        if (!handColorsEnabled)
        {
            ShowFingeringCheckBox.IsChecked =
                false;
        }
    }

    private void RefreshMidiButton_Click(object sender, RoutedEventArgs e)
    {
        StopKeyboardRangeCapture("最低音と最高音を実機から取得できます。");
        RefreshMidiRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BuiltInSoundFontTestButton_Click(object sender, RoutedEventArgs e)
    {
        var configuration = CreateCurrentSoundFontConfiguration();
        if (configuration is null)
        {
            StatusText.Text = "SoundFontとPresetを選択してください。";
            return;
        }

        BuiltInSoundFontTestRequested?.Invoke(
            this,
            new SoundFontConfigurationEventArgs(configuration));
    }

    private void SoundFontBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "SoundFontを選択",
            Filter = "SoundFont (*.sf2)|*.sf2|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        var path = Path.GetFullPath(dialog.FileName);
        SoundFontPathTextBox.Text = path;
        RefreshSoundFontPresets(path, preferredBankNumber: -1, preferredPatchNumber: -1);
    }

    private void AddSongFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var path = NewSongFolderTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!_songFolders.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                _songFolders.Add(fullPath);
            }

            NewSongFolderTextBox.Clear();
            RefreshSongFolderList();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            StatusText.Text = $"フォルダパスが不正です: {ex.Message}";
        }
    }

    private void RemoveSongFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (SongFoldersListBox.SelectedItem is string selected)
        {
            _songFolders.RemoveAll(path => string.Equals(path, selected, StringComparison.OrdinalIgnoreCase));
            RefreshSongFolderList();
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        StopKeyboardRangeCapture("最低音と最高音を実機から取得できます。");
        SaveCurrentSettings();
    }

    private void KeyboardRangeCaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsKeyboardRangeCaptureActive)
        {
            StopKeyboardRangeCapture("検出をキャンセルしました。");
            return;
        }

        _capturedFirstMidiNote = null;
        Volatile.Write(ref _keyboardRangeCaptureActive, true);
        KeyboardRangeCaptureButton.Content = "検出をキャンセル";
        KeyboardRangeCaptureStatusText.Text =
            "検出中: 実機の最低音と最高音を1回ずつ押してください。順番はどちらでも構いません。";
    }

    private void KeyboardPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string preset }
            || !TryParseKeyboardPreset(preset, out var lowest, out var highest))
        {
            return;
        }

        SetKeyboardRange(lowest, highest);
        StopKeyboardRangeCapture(
            $"プリセット: {MidiPitch.ToName(lowest)} ～ {MidiPitch.ToName(highest)} / {highest - lowest + 1}鍵");
    }

    private void KeyboardRangeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingKeyboardRange
            || KeyboardLowestSlider is null
            || KeyboardHighestSlider is null)
        {
            return;
        }

        var lowest = GetKeyboardLowestMidi();
        var highest = GetKeyboardHighestMidi();
        if (lowest > highest)
        {
            if (ReferenceEquals(sender, KeyboardLowestSlider))
            {
                highest = lowest;
            }
            else
            {
                lowest = highest;
            }

            SetKeyboardRange(lowest, highest);
            return;
        }

        UpdateKeyboardRangeLabels(lowest, highest);
    }

    private void JudgementTimingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateTimingVisualization();
    }

    private void PianoRollVisibleRangeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        UpdatePercentLabels();
    }

    private void MetronomeVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdatePercentLabels();
    }

    private void BuiltInSoundFontSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdatePercentLabels();
        NotifySoundFontPreviewChanged();
    }

    private void SoundFontPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => NotifySoundFontPreviewChanged();

    private void SaveCurrentSettings()
    {
        var settings = _baseSettings.Clone();
        settings.SongFolders = _songFolders.ToList();
        settings.PreferredMidiDeviceName = MidiDeviceComboBox.SelectedItem as string;
        settings.PreferredMidiOutputDeviceName = MidiOutputDeviceComboBox.SelectedItem as string;
        settings.AutoConnectMidi = AutoConnectMidiCheckBox.IsChecked == true;
        settings.BuiltInSoundFontPath = SoundFontPathTextBox.Text.Trim();
        if (SoundFontPresetComboBox.SelectedItem is SoundFontPresetInfo preset)
        {
            settings.BuiltInSoundFontBankNumber = preset.BankNumber;
            settings.BuiltInSoundFontPatchNumber = preset.PatchNumber;
        }

        settings.BuiltInSoundFontVolumePercent = Math.Clamp((int)Math.Round(BuiltInSoundFontVolumeSlider.Value), 0, 150);
        settings.BuiltInSoundFontReverbPercent = Math.Clamp((int)Math.Round(BuiltInSoundFontReverbSlider.Value), 0, 100);
        settings.AutomaticPlaybackVelocity = Math.Clamp((int)Math.Round(AutomaticPlaybackVelocitySlider.Value), 1, 127);
        settings.JudgementTimingPercent = Math.Clamp((int)Math.Round(JudgementTimingSlider.Value), 50, 150);
        settings.PianoRollVisibleRangePercent =
            (int)Math.Round(PianoRollVisibleRangeSlider.Value);
        settings.MetronomeVolumePercent = (int)Math.Round(MetronomeVolumeSlider.Value);
        settings.ShowHandColors =
            ShowHandColorsCheckBox.IsChecked
            == true;
        settings.ShowFingering =
            settings.ShowHandColors
            && ShowFingeringCheckBox.IsChecked
                == true;
        settings.ShowExpectedKeyboardGuide = ShowExpectedKeyboardGuideCheckBox.IsChecked == true;
        settings.EditorTimelineDragMode =
            EditorTimelineDragModeComboBox.SelectedItem is TimelineDragModeChoice timelineChoice
                ? timelineChoice.Mode
                : EditorTimelineDragMode.TouchScroll;
        settings.EditorPanPitchEnabled =
            EditorPanPitchEnabledCheckBox.IsChecked == true;
        settings.KeyboardLowestMidi = GetKeyboardLowestMidi();
        settings.KeyboardHighestMidi = GetKeyboardHighestMidi();
        settings.HasOctaveShift = HasOctaveShiftCheckBox.IsChecked == true;

        _baseSettings = settings.Clone();
        SaveRequested?.Invoke(this, new SettingsSaveRequestedEventArgs(settings));
    }

    private void SetSoundFontState(AppSettings settings)
    {
        SoundFontPathTextBox.Text = settings.BuiltInSoundFontPath;
        RefreshSoundFontPresets(
            settings.BuiltInSoundFontPath,
            settings.BuiltInSoundFontBankNumber,
            settings.BuiltInSoundFontPatchNumber);
    }

    private void RefreshSoundFontPresets(
        string soundFontPath,
        int preferredBankNumber,
        int preferredPatchNumber)
    {
        _soundFontPresets.Clear();
        SoundFontPresetComboBox.ItemsSource = null;
        SoundFontPresetStatusText.Text = string.Empty;

        if (string.IsNullOrWhiteSpace(soundFontPath))
        {
            SoundFontPresetStatusText.Text = "SoundFontを選択してください。";
            return;
        }

        try
        {
            var allPresets = SoundFontCatalogService.LoadPresets(soundFontPath);
            _soundFontPresets.AddRange(allPresets.Where(preset => preset.IsSupported));
            SoundFontPresetComboBox.ItemsSource = _soundFontPresets;

            if (_soundFontPresets.Count == 0)
            {
                SoundFontPresetStatusText.Text =
                    "選択可能なPresetがありません。対応範囲はBank 0〜127、打楽器Bank 128 / Program 0〜127です。";
                return;
            }

            var selectedIndex = _soundFontPresets.FindIndex(preset =>
                preset.BankNumber == preferredBankNumber
                && preset.PatchNumber == preferredPatchNumber);
            SoundFontPresetComboBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;

            var unsupportedCount = allPresets.Count - _soundFontPresets.Count;
            SoundFontPresetStatusText.Text = unsupportedCount > 0
                ? $"{_soundFontPresets.Count}個のPresetを使用できます。対応範囲外の{unsupportedCount}個は非表示です。"
                : $"{_soundFontPresets.Count}個のPresetを検出しました。";
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException
                or NotSupportedException)
        {
            SoundFontPresetStatusText.Text = $"SoundFontを読み込めません: {ex.Message}";
        }
    }

    private SoundFontConfiguration? CreateCurrentSoundFontConfiguration()
    {
        if (SoundFontPathTextBox is null
            || SoundFontPresetComboBox is null
            || BuiltInSoundFontVolumeSlider is null
            || BuiltInSoundFontReverbSlider is null
            || string.IsNullOrWhiteSpace(SoundFontPathTextBox.Text)
            || SoundFontPresetComboBox.SelectedItem is not SoundFontPresetInfo preset)
        {
            return null;
        }

        return new SoundFontConfiguration(
            SoundFontPathTextBox.Text.Trim(),
            preset.BankNumber,
            preset.PatchNumber,
            Math.Clamp((int)Math.Round(BuiltInSoundFontVolumeSlider.Value), 0, 150),
            Math.Clamp((int)Math.Round(BuiltInSoundFontReverbSlider.Value), 0, 100));
    }

    private void NotifySoundFontPreviewChanged()
    {
        var configuration = CreateCurrentSoundFontConfiguration();
        if (configuration is null)
        {
            return;
        }

        BuiltInSoundFontPreviewChanged?.Invoke(
            this,
            new SoundFontConfigurationEventArgs(configuration));
    }

    private void StopKeyboardRangeCapture(string statusText)
    {
        Volatile.Write(ref _keyboardRangeCaptureActive, false);
        _capturedFirstMidiNote = null;
        if (KeyboardRangeCaptureButton is not null)
        {
            KeyboardRangeCaptureButton.Content = "MIDIから音域を検出";
        }

        if (KeyboardRangeCaptureStatusText is not null)
        {
            KeyboardRangeCaptureStatusText.Text = statusText;
        }
    }

    private void SetKeyboardRange(int lowest, int highest)
    {
        lowest = Math.Clamp(lowest, 0, 127);
        highest = Math.Clamp(highest, 0, 127);
        if (highest < lowest)
        {
            (lowest, highest) = (highest, lowest);
        }

        _updatingKeyboardRange = true;
        try
        {
            KeyboardLowestSlider.Value = lowest;
            KeyboardHighestSlider.Value = highest;
        }
        finally
        {
            _updatingKeyboardRange = false;
        }

        UpdateKeyboardRangeLabels(lowest, highest);
    }

    private void UpdateKeyboardRangeLabels(int lowest, int highest)
    {
        if (KeyboardLowestNoteText is null
            || KeyboardHighestNoteText is null
            || KeyboardCountText is null)
        {
            return;
        }

        KeyboardLowestNoteText.Text = $"{MidiPitch.ToName(lowest)} ({lowest})";
        KeyboardHighestNoteText.Text = $"{MidiPitch.ToName(highest)} ({highest})";
        KeyboardCountText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0}鍵 / {1} ～ {2}",
            highest - lowest + 1,
            MidiPitch.ToName(lowest),
            MidiPitch.ToName(highest));
    }

    private int GetKeyboardLowestMidi()
        => Math.Clamp((int)Math.Round(KeyboardLowestSlider.Value), 0, 127);

    private int GetKeyboardHighestMidi()
        => Math.Clamp((int)Math.Round(KeyboardHighestSlider.Value), 0, 127);

    private static bool TryParseKeyboardPreset(string preset, out int lowest, out int highest)
    {
        var values = preset.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (values.Length == 2
            && int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out lowest)
            && int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out highest)
            && lowest is >= 0 and <= 127
            && highest is >= 0 and <= 127)
        {
            return true;
        }

        lowest = 0;
        highest = 0;
        return false;
    }

    private void RefreshSongFolderList()
    {
        SongFoldersListBox.ItemsSource = null;
        SongFoldersListBox.ItemsSource = _songFolders.ToArray();
    }

    private void UpdatePercentLabels()
    {
        if (PianoRollVisibleRangeText is not null)
        {
            var visibleBeats = 8d * PianoRollVisibleRangeSlider.Value / 100d;
            PianoRollVisibleRangeText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0:0.#} beat",
                visibleBeats);
        }

        if (BuiltInSoundFontVolumeText is not null)
        {
            BuiltInSoundFontVolumeText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0:0}%",
                BuiltInSoundFontVolumeSlider.Value);
        }

        if (BuiltInSoundFontReverbText is not null)
        {
            BuiltInSoundFontReverbText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0:0}%",
                BuiltInSoundFontReverbSlider.Value);
        }

        if (AutomaticPlaybackVelocityText is not null)
        {
            AutomaticPlaybackVelocityText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0:0}",
                AutomaticPlaybackVelocitySlider.Value);
        }

        if (MetronomeVolumeText is not null)
        {
            MetronomeVolumeText.Text = string.Format(CultureInfo.CurrentCulture, "{0:0}%", MetronomeVolumeSlider.Value);
        }
    }

    private void UpdateTimingVisualization()
    {
        if (JudgementTimingSlider is null
            || JudgementTimingText is null
            || AttackTimingText is null
            || ReleaseTimingText is null
            || AttackGoodTimingBand is null
            || AttackGreatTimingBand is null
            || AttackPerfectTimingBand is null
            || ReleaseGoodTimingBand is null
            || ReleaseGreatTimingBand is null
            || ReleasePerfectTimingBand is null)
        {
            return;
        }

        var scale = Math.Clamp(JudgementTimingSlider.Value / 100d, 0.50d, 1.50d);
        var timing = PerformanceTimingProfile.FromScale(scale);
        JudgementTimingText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0:0}%",
            scale * 100d);

        SetTimingBandWidth(AttackGoodTimingBand, timing.GoodAttackSeconds, AttackTimingDisplayHalfRangeSeconds);
        SetTimingBandWidth(AttackGreatTimingBand, timing.GreatAttackSeconds, AttackTimingDisplayHalfRangeSeconds);
        SetTimingBandWidth(AttackPerfectTimingBand, timing.PerfectAttackSeconds, AttackTimingDisplayHalfRangeSeconds);
        SetTimingBandWidth(ReleaseGoodTimingBand, timing.GoodReleaseSeconds, ReleaseTimingDisplayHalfRangeSeconds);
        SetTimingBandWidth(ReleaseGreatTimingBand, timing.GreatReleaseSeconds, ReleaseTimingDisplayHalfRangeSeconds);
        SetTimingBandWidth(ReleasePerfectTimingBand, timing.PerfectReleaseSeconds, ReleaseTimingDisplayHalfRangeSeconds);

        AttackTimingText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "PERFECT ±{0:0}ms / GREAT ±{1:0}ms / GOOD ±{2:0}ms（早い・遅いを同じ幅で判定）",
            timing.PerfectAttackSeconds * 1000d,
            timing.GreatAttackSeconds * 1000d,
            timing.GoodAttackSeconds * 1000d);
        ReleaseTimingText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "離鍵 PERFECT ±{0:0}ms / GREAT ±{1:0}ms / GOOD ±{2:0}ms。0.5秒以内の同音連打は早離し側に +{3:0}ms の猶予。",
            timing.PerfectReleaseSeconds * 1000d,
            timing.GreatReleaseSeconds * 1000d,
            timing.GoodReleaseSeconds * 1000d,
            timing.RepeatedKeyEarlyReleaseGraceSeconds * 1000d);
    }

    private static void SetTimingBandWidth(FrameworkElement element, double seconds, double displayHalfRangeSeconds)
    {
        element.Width = Math.Clamp(
            seconds / displayHalfRangeSeconds * TimingBandWidth,
            1d,
            TimingBandWidth);
    }

    private sealed record TimelineDragModeChoice(
        EditorTimelineDragMode Mode,
        string Label);
}

public sealed class SoundFontConfigurationEventArgs : EventArgs
{
    public SoundFontConfigurationEventArgs(SoundFontConfiguration configuration)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public SoundFontConfiguration Configuration { get; }
}

public sealed class SettingsSaveRequestedEventArgs : EventArgs
{
    public SettingsSaveRequestedEventArgs(AppSettings settings)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public AppSettings Settings { get; }
}
