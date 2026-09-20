using System.Collections.Concurrent;
using System.Threading;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PianoPracticeTool.Core;
using PianoPracticeTool.Services.Midi;
using PianoPracticeTool.Services.Practice;
using PianoPracticeTool.Services.Settings;
using PianoPracticeTool.Services.Songs;
using PianoPracticeTool.Views;

namespace PianoPracticeTool;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly IMidiInputService _midiInputService;
    private readonly IMidiSoundService _midiSoundService;
    private readonly PracticeWorkflow _practiceWorkflow;
    private readonly PracticePlaybackScheduler _playbackScheduler;
    private readonly AppSettingsService _settingsService;
    private readonly PracticeHistoryService _practiceHistoryService;
    private readonly object _practiceWorkflowSync = new();
    private readonly SongLibraryView _songLibraryView = new();
    private readonly SongMetadataView _songMetadataView = new();
    private readonly PracticeSetupView _practiceSetupView = new();
    private readonly PerformanceView _performanceView = new();
    private readonly Timer _practiceSnapshotTimer;
    private readonly ConcurrentDictionary<int, byte> _activeMidiNotes = new();

    private AppSettings _settings;
    private PracticeSnapshot? _pendingPracticeSnapshot;
    private string? _pendingMidiInputText;
    private int _presentationUiVersion;
    private int _appliedPresentationUiVersion;
    private IReadOnlyList<MidiInputDevice> _midiDevices = Array.Empty<MidiInputDevice>();
    private IReadOnlyList<MidiOutputDevice> _midiOutputDevices = Array.Empty<MidiOutputDevice>();
    private SongCatalogEntry? _currentSong;
    private SettingsWindow? _settingsWindow;
    private DateTimeOffset? _lastPersistedResultCompletedAt;
    private bool _currentPreferRecommendedDeviceOctaveShift;
    private int? _currentAppliedDeviceOctaveShiftOverride;
    private bool _currentSongUsesOctaveCompression;
    private bool _disposed;

    public MainWindow()
    {
        _midiInputService = new NAudioMidiInputService();
        _midiSoundService = new NAudioMidiSoundService();
        _practiceWorkflow = new PracticeWorkflow(_midiSoundService);
        _playbackScheduler = new PracticePlaybackScheduler(AdvancePracticePlayback);
        _settingsService = new AppSettingsService();
        _practiceHistoryService = new PracticeHistoryService();
        _settings = _settingsService.Load();
        _practiceSnapshotTimer = new Timer(
            CapturePracticeSnapshot,
            null,
            Timeout.Infinite,
            Timeout.Infinite);

        InitializeComponent();
        WireEvents();
        Loaded += MainWindow_Loaded;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
    }

    private void WireEvents()
    {
        _midiInputService.NoteOn += MidiInputService_NoteOn;
        _midiInputService.NoteOff += MidiInputService_NoteOff;
        _midiInputService.ControlChange += MidiInputService_ControlChange;
        _midiInputService.InputError += MidiInputService_InputError;
        _playbackScheduler.SchedulerError += PlaybackScheduler_SchedulerError;

        _songLibraryView.SongSelected += SongLibraryView_SongSelected;
        _songLibraryView.OpenFileRequested += SongLibraryView_OpenFileRequested;
        _songLibraryView.RefreshRequested += SongLibraryView_RefreshRequested;
        _songLibraryView.MetadataEditRequested += SongLibraryView_MetadataEditRequested;
        _songMetadataView.BackRequested += SongMetadataView_BackRequested;
        _songMetadataView.SaveRequested += SongMetadataView_SaveRequested;
        _practiceSetupView.BackRequested += PracticeSetupView_BackRequested;
        _practiceSetupView.StartRequested += PracticeSetupView_StartRequested;
        _performanceView.ExitRequested += PerformanceView_ExitRequested;
        _performanceView.PauseRequested += PerformanceView_PauseRequested;
        _performanceView.SeekRequested += PerformanceView_SeekRequested;
        _performanceView.MemorizationSeekRequested += PerformanceView_MemorizationSeekRequested;
        _performanceView.SpeedStepRequested += PerformanceView_SpeedStepRequested;
        _performanceView.MetronomeToggleRequested += PerformanceView_MetronomeToggleRequested;
        _performanceView.MetronomeVolumeChanged += PerformanceView_MetronomeVolumeChanged;
        _performanceView.ResultBackRequested += PerformanceView_ResultBackRequested;
    }

    private void UnwireEvents()
    {
        _midiInputService.NoteOn -= MidiInputService_NoteOn;
        _midiInputService.NoteOff -= MidiInputService_NoteOff;
        _midiInputService.ControlChange -= MidiInputService_ControlChange;
        _midiInputService.InputError -= MidiInputService_InputError;
        _playbackScheduler.SchedulerError -= PlaybackScheduler_SchedulerError;

        _songLibraryView.SongSelected -= SongLibraryView_SongSelected;
        _songLibraryView.OpenFileRequested -= SongLibraryView_OpenFileRequested;
        _songLibraryView.RefreshRequested -= SongLibraryView_RefreshRequested;
        _songLibraryView.MetadataEditRequested -= SongLibraryView_MetadataEditRequested;
        _songMetadataView.BackRequested -= SongMetadataView_BackRequested;
        _songMetadataView.SaveRequested -= SongMetadataView_SaveRequested;
        _practiceSetupView.BackRequested -= PracticeSetupView_BackRequested;
        _practiceSetupView.StartRequested -= PracticeSetupView_StartRequested;
        _performanceView.ExitRequested -= PerformanceView_ExitRequested;
        _performanceView.PauseRequested -= PerformanceView_PauseRequested;
        _performanceView.SeekRequested -= PerformanceView_SeekRequested;
        _performanceView.MemorizationSeekRequested -= PerformanceView_MemorizationSeekRequested;
        _performanceView.SpeedStepRequested -= PerformanceView_SpeedStepRequested;
        _performanceView.MetronomeToggleRequested -= PerformanceView_MetronomeToggleRequested;
        _performanceView.MetronomeVolumeChanged -= PerformanceView_MetronomeVolumeChanged;
        _performanceView.ResultBackRequested -= PerformanceView_ResultBackRequested;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _playbackScheduler.Stop();
        _practiceSnapshotTimer.Change(
            Timeout.Infinite,
            Timeout.Infinite);
        Loaded -= MainWindow_Loaded;
        Activated -= MainWindow_Activated;
        Closed -= MainWindow_Closed;
        UnwireEvents();
        SaveSettingsQuietly();
        UsePracticeWorkflow(workflow => workflow.AllNotesOff());
        _playbackScheduler.Dispose();
        _practiceSnapshotTimer.Dispose();
        _midiInputService.Dispose();
        _midiSoundService.Dispose();
        GC.SuppressFinalize(this);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeSound();
        RefreshMidiDevices(showErrorDialog: false);
        RefreshMidiOutputDevices(showErrorDialog: false);
        if (_settings.AutoConnectMidi)
        {
            TryConnectPreferredMidi(showErrorDialog: false);
        }

        TryConnectPreferredMidiOutput(showErrorDialog: false);

        await ReloadSongLibraryAsync();
        ShowSongLibrary();
        _playbackScheduler.Start();
        _practiceSnapshotTimer.Change(
            dueTime: PracticeSnapshotIntervalMilliseconds,
            period: PracticeSnapshotIntervalMilliseconds);
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        if (!IsLoaded || _disposed || _midiInputService.IsConnected)
        {
            return;
        }

        RefreshMidiDevices(showErrorDialog: false);
        if (_settings.AutoConnectMidi)
        {
            TryConnectPreferredMidi(showErrorDialog: false);
        }

        TryConnectPreferredMidiOutput(showErrorDialog: false);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        Dispose();
    }

    private void InitializeSound()
    {
        try
        {
            _midiSoundService.Initialize();
            ApplyBuiltInSoundFontSettings();
        }
        catch (Exception ex)
        {
            MidiStatusText.Text = $"音源初期化失敗: {ex.Message}";
        }
    }

    private void ApplyBuiltInSoundFontSettings()
    {
        _midiSoundService.ConfigureBuiltInSoundFont(
            SoundFontSettings.CreateConfiguration(_settings));
    }

    private void ShowSongLibrary()
    {
        SetPerformancePresentationActive(
            active: false);
        UsePracticeWorkflow(workflow => workflow.Clear());
        ClearActiveMidiNotes();
        _currentSong = null;
        _currentPreferRecommendedDeviceOctaveShift = false;
        _currentAppliedDeviceOctaveShiftOverride = null;
        _currentSongUsesOctaveCompression = false;
        _lastPersistedResultCompletedAt = null;
        _songLibraryView.SetBestScores(_practiceHistoryService.GetBestHundredPointScoresBySong());
        PageHost.Content = _songLibraryView;
        SettingsButton.IsEnabled = true;
    }

    private async Task ReloadSongLibraryAsync()
    {
        try
        {
            var settingsSnapshot = _settings.Clone();
            var catalog = await SongCatalogService.LoadDirectoriesAsync(
                settingsSnapshot.SongFolders,
                settingsSnapshot.SongDifficultyOverrides);
            if (_disposed)
            {
                return;
            }

            _songLibraryView.SetSongs(catalog.Songs, catalog.Errors, settingsSnapshot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _songLibraryView.SetSongs(
                Array.Empty<SongCatalogEntry>(),
                new[] { $"楽曲探索に失敗しました: {ex.Message}" },
                _settings);
        }
    }

    private void ShowPracticeSetup(
        SongCatalogEntry song,
        bool prepareWorkflow,
        bool preferRecommendedDeviceOctaveShift,
        int? appliedDeviceOctaveShiftOverride = null,
        bool usesSongOctaveCompression = false)
    {
        ArgumentNullException.ThrowIfNull(song);
        SetPerformancePresentationActive(
            active: false);
        _currentSong = song;
        _currentSongUsesOctaveCompression = usesSongOctaveCompression;
        ClearActiveMidiNotes();
        _lastPersistedResultCompletedAt = null;
        if (prepareWorkflow)
        {
            UsePracticeWorkflow(workflow => workflow.Prepare(song.Score));
        }

        var compatibility = KeyboardCompatibilityService.Analyze(song.Score, _settings);
        _currentPreferRecommendedDeviceOctaveShift =
            preferRecommendedDeviceOctaveShift
            && _settings.HasOctaveShift;
        _currentAppliedDeviceOctaveShiftOverride =
            appliedDeviceOctaveShiftOverride is int shift
            && compatibility.AvailableOctaveShiftsSemitones.Contains(shift)
                ? shift
                : null;
        var history = _practiceHistoryService.GetForSong(song.FilePath);
        _practiceSetupView.SetSong(
            song,
            compatibility,
            history,
            _currentPreferRecommendedDeviceOctaveShift,
            _currentAppliedDeviceOctaveShiftOverride,
            _currentSongUsesOctaveCompression);
        PageHost.Content = _practiceSetupView;
        SettingsButton.IsEnabled = true;
    }

    private void StartPractice(PracticeSetupOptions options)
    {
        if (_currentSong is null)
        {
            return;
        }

        _currentPreferRecommendedDeviceOctaveShift = options.FollowRecommendedDeviceOctaveShift;
        _currentAppliedDeviceOctaveShiftOverride = options.FollowRecommendedDeviceOctaveShift
            ? null
            : options.AppliedDeviceOctaveShiftSemitones;
        var keyboardPlan = KeyboardCompatibilityService.CreatePracticePlan(
            _currentSong.Score,
            _settings,
            options.HandMode,
            options.AppliedDeviceOctaveShiftSemitones);
        var playableMidiRange = new PracticeMidiRange(
            keyboardPlan.PlayableLowestMidi,
            keyboardPlan.PlayableHighestMidi);

        ClearActiveMidiNotes();
        _lastPersistedResultCompletedAt = null;
        lock (_practiceWorkflowSync)
        {
            _practiceWorkflow.Prepare(_currentSong.Score);
            _practiceWorkflow.SetMode(options.Mode);
            _practiceWorkflow.SetHandMode(options.HandMode);
            _practiceWorkflow.SetLoopRange(options.LoopRange);
            _practiceWorkflow.SetPlayableMidiRange(playableMidiRange);
            _practiceWorkflow.SetSpeed(options.Mode == PracticeMode.OriginalTempo ? 1d : options.SpeedMultiplier);
            _practiceWorkflow.SetMetronomeVolumePercent(_settings.MetronomeVolumePercent);
            _practiceWorkflow.SetAutomaticNoteVelocity(_settings.AutomaticPlaybackVelocity);
            _practiceWorkflow.SetMetronomeEnabled(false);
        }

        _performanceView.SetSong(_currentSong, options, _settings, playableMidiRange);
        PageHost.Content = _performanceView;
        SettingsButton.IsEnabled = false;
        SetPerformancePresentationActive(
            active: true);
        QueuePracticeSnapshot(UsePracticeWorkflow(workflow => workflow.TogglePlayback()));
    }

    private void ReturnToPracticeSetup()
    {
        SetPerformancePresentationActive(
            active: false);

        if (_currentSong is null)
        {
            ShowSongLibrary();
            return;
        }

        UsePracticeWorkflow(workflow =>
        {
            workflow.AllNotesOff();
            workflow.Prepare(_currentSong.Score);
        });
        ClearActiveMidiNotes();
        SaveSettingsQuietly();
        ShowPracticeSetup(
            _currentSong,
            prepareWorkflow: false,
            preferRecommendedDeviceOctaveShift:
                _currentPreferRecommendedDeviceOctaveShift,
            appliedDeviceOctaveShiftOverride:
                _currentAppliedDeviceOctaveShiftOverride,
            usesSongOctaveCompression:
                _currentSongUsesOctaveCompression);
    }

    private async void OpenExternalSong()
    {
        var dialog = new OpenFileDialog
        {
            Title = "MusicXMLを選択",
            Filter = "MusicXML (*.xml;*.musicxml)|*.xml;*.musicxml|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var song = await SongCatalogService.LoadSongAsync(
                dialog.FileName,
                _settings.SongDifficultyOverrides);
            if (!_disposed)
            {
                ShowPracticeSetup(
                    song,
                    prepareWorkflow: true,
                    preferRecommendedDeviceOctaveShift: _settings.HasOctaveShift,
                    usesSongOctaveCompression: false);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "MusicXML読込エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SongLibraryView_SongSelected(
        object? sender,
        SongSelectedEventArgs e)
    {
        var practiceSong = e.UseOctaveCompression
            ? CreateOctaveCompressedSong(e.Song)
            : e.Song;
        if (e.UseScoreCorrection)
        {
            practiceSong =
                CreatePhysicallyCorrectedSong(
                    practiceSong);
        }

        ShowPracticeSetup(
            practiceSong,
            prepareWorkflow: true,
            preferRecommendedDeviceOctaveShift: _settings.HasOctaveShift,
            usesSongOctaveCompression: e.UseOctaveCompression);
    }

    private void SongLibraryView_MetadataEditRequested(
        object? sender,
        SongMetadataEditRequestedEventArgs e)
    {
        _songMetadataView.SetSong(e.Song);
        PageHost.Content = _songMetadataView;
        SettingsButton.IsEnabled = true;
    }

    private void SongMetadataView_BackRequested(
        object? sender,
        EventArgs e)
        => ShowSongLibrary();

    private async void SongMetadataView_SaveRequested(
        object? sender,
        SongMetadataSaveRequestedEventArgs e)
    {
        try
        {
            _songMetadataView.SetStatus("保存中…");
            await Task.Run(() =>
                SongMetadataService.Save(
                    e.Song,
                    e.Update));

            _settings.SongDifficultyOverrides.Remove(
                Path.GetFullPath(e.Song.FilePath));
            SaveSettingsQuietly();
            await ReloadSongLibraryAsync();
            ShowSongLibrary();
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException)
        {
            _songMetadataView.SetStatus(
                $"保存に失敗しました: {ex.Message}");
        }
    }

    private void SongLibraryView_OpenFileRequested(
        object? sender,
        EventArgs e)
        => OpenExternalSong();

    private async void SongLibraryView_RefreshRequested(
        object? sender,
        EventArgs e)
        => await ReloadSongLibraryAsync();

    private SongCatalogEntry CreateOctaveCompressedSong(
        SongCatalogEntry song)
    {
        var compressedScore =
            SongOctaveCompressionService.Compress(
                song.Score,
                _settings.KeyboardLowestMidi,
                _settings.KeyboardHighestMidi);
        return song with
        {
            Score = compressedScore,
            Keyboard =
                KeyboardRangeAnalyzer.Analyze(
                    compressedScore)
        };
    }

    private static SongCatalogEntry CreatePhysicallyCorrectedSong(
        SongCatalogEntry song)
    {
        var correctedScore =
            PracticeScorePreparation
                .ApplyPhysicalCorrections(
                    song.Score);
        return song with
        {
            Score = correctedScore,
            Consistency =
                PracticeScorePreparation
                    .AnalyzeConsistency(
                        correctedScore)
        };
    }

    private void PracticeSetupView_BackRequested(object? sender, EventArgs e)
        => ShowSongLibrary();

    private void PracticeSetupView_StartRequested(object? sender, PracticeSetupRequestedEventArgs e)
        => StartPractice(e.Options);

    private void PerformanceView_ExitRequested(object? sender, EventArgs e)
        => ReturnToPracticeSetup();

    private void PerformanceView_PauseRequested(object? sender, EventArgs e)
        => TogglePerformancePlayback();

    private void PerformanceView_SeekRequested(
        object? sender,
        PerformanceSeekRequestedEventArgs e)
    {
        var snapshot = UsePracticeWorkflow(workflow => workflow.CreateSnapshot());
        if (snapshot.Mode != PracticeMode.Listen)
        {
            return;
        }

        QueuePracticeSnapshot(UsePracticeWorkflow(workflow => workflow.Seek(e.Beat)));
    }

    private void PerformanceView_MemorizationSeekRequested(
        object? sender,
        PerformanceMemorizationSeekRequestedEventArgs e)
    {
        QueuePracticeSnapshot(
            UsePracticeWorkflow(workflow =>
                workflow.SeekListenForMemorization(
                    e.Direction,
                    e.ByMeasure)));
    }

    private void TogglePerformancePlayback()
        => QueuePracticeSnapshot(
            UsePracticeWorkflow(workflow => workflow.TogglePlayback()));

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled
            || e.IsRepeat
            || !ReferenceEquals(
                PageHost.Content,
                _performanceView))
        {
            return;
        }

        var snapshot =
            UsePracticeWorkflow(workflow =>
                workflow.CreateSnapshot());
        if (snapshot.Mode != PracticeMode.Listen)
        {
            return;
        }

        if (e.Key == Key.Space)
        {
            e.Handled = true;
            TogglePerformancePlayback();
            return;
        }

        if (e.Key == Key.R
            && Keyboard.Modifiers == ModifierKeys.None
            && snapshot.State == PracticeRunState.Paused)
        {
            e.Handled = true;
            QueuePracticeSnapshot(
                UsePracticeWorkflow(workflow =>
                    workflow.ReturnToListenReplayStart()));
        }
    }

    private void PerformanceView_SpeedStepRequested(object? sender, PerformanceSpeedStepRequestedEventArgs e)
    {
        var snapshot = UsePracticeWorkflow(workflow => workflow.CreateSnapshot());
        if (snapshot.Mode == PracticeMode.OriginalTempo)
        {
            return;
        }

        var nextSpeed = PracticeSpeed.Step(snapshot.SpeedMultiplier, e.Step);
        QueuePracticeSnapshot(UsePracticeWorkflow(workflow => workflow.SetSpeed(nextSpeed)));
    }

    private void PerformanceView_MetronomeToggleRequested(object? sender, EventArgs e)
    {
        var snapshot = UsePracticeWorkflow(workflow => workflow.CreateSnapshot());
        QueuePracticeSnapshot(UsePracticeWorkflow(
            workflow => workflow.SetMetronomeEnabled(!snapshot.MetronomeEnabled)));
    }

    private void PerformanceView_MetronomeVolumeChanged(
        object? sender,
        PerformanceMetronomeVolumeRequestedEventArgs e)
    {
        _settings.MetronomeVolumePercent = e.VolumePercent;
        QueuePracticeSnapshot(UsePracticeWorkflow(
            workflow => workflow.SetMetronomeVolumePercent(e.VolumePercent)));
    }

    private void PerformanceView_ResultBackRequested(object? sender, EventArgs e)
        => ReturnToPracticeSetup();

    private void ModeSelectionButton_Click(
        object sender,
        RoutedEventArgs e)
        => Close();

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(PageHost.Content, _performanceView)
            || _settingsWindow is not null)
        {
            return;
        }

        var window = new SettingsWindow(
            _settings,
            _midiDevices.Select(device => device.Name).ToArray(),
            _midiOutputDevices.Select(device => device.Name).ToArray(),
            GetMidiStatusText())
        {
            Owner = this
        };

        _settingsWindow = window;
        window.View.RefreshMidiRequested += SettingsView_RefreshMidiRequested;
        window.View.BuiltInSoundFontPreviewChanged += SettingsView_BuiltInSoundFontPreviewChanged;
        window.View.BuiltInSoundFontTestRequested += SettingsView_BuiltInSoundFontTestRequested;
        window.View.SaveRequested += SettingsView_SaveRequested;

        try
        {
            window.ShowDialog();
        }
        finally
        {
            window.View.RefreshMidiRequested -= SettingsView_RefreshMidiRequested;
            window.View.BuiltInSoundFontPreviewChanged -= SettingsView_BuiltInSoundFontPreviewChanged;
            window.View.BuiltInSoundFontTestRequested -= SettingsView_BuiltInSoundFontTestRequested;
            window.View.SaveRequested -= SettingsView_SaveRequested;
            _settingsWindow = null;

            try
            {
                ApplyBuiltInSoundFontSettings();
            }
            catch (Exception ex)
            {
                MidiStatusText.Text = $"SoundFont設定の復元失敗: {ex.Message}";
            }
        }
    }

    private void SettingsView_RefreshMidiRequested(object? sender, EventArgs e)
    {
        RefreshMidiDevices(showErrorDialog: true);
        RefreshMidiOutputDevices(showErrorDialog: true);
        TryConnectPreferredMidiOutput(showErrorDialog: false);
        _settingsWindow?.View.UpdateMidiState(
            _midiDevices.Select(device => device.Name).ToArray(),
            _midiOutputDevices.Select(device => device.Name).ToArray(),
            GetMidiStatusText());
    }

    private void SettingsView_BuiltInSoundFontPreviewChanged(
        object? sender,
        SoundFontConfigurationEventArgs e)
    {
        try
        {
            _midiSoundService.ConfigureBuiltInSoundFont(e.Configuration);
        }
        catch (Exception ex)
        {
            _settingsWindow?.View.SetStatus($"SoundFontプレビュー更新失敗: {ex.Message}");
        }
    }

    private async void SettingsView_BuiltInSoundFontTestRequested(
        object? sender,
        SoundFontConfigurationEventArgs e)
    {
        if (!string.Equals(
                _midiSoundService.DeviceName,
                MidiOutputDevice.BuiltInSoundFont.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            _settingsWindow?.View.SetStatus(
                "発音先に「Built-in SoundFont Synthesizer」を選択してください。");
            return;
        }

        try
        {
            _midiSoundService.AllNotesOff();
            _midiSoundService.ConfigureBuiltInSoundFont(e.Configuration);
            _midiSoundService.NoteOn(60, 127, 1);
            await Task.Delay(700);
            _midiSoundService.NoteOff(60, 0, 1);
            await Task.Delay(900);
            _settingsWindow?.View.SetStatus("現在のSoundFont・Preset・リバーブ設定で試聴しました。");
        }
        catch (Exception ex)
        {
            ApplyBuiltInSoundFontSettings();
            _settingsWindow?.View.SetStatus($"SoundFont音源の試聴に失敗しました: {ex.Message}");
        }
    }

    private async void SettingsView_SaveRequested(object? sender, SettingsSaveRequestedEventArgs e)
    {
        _settings = e.Settings.Clone();
        ApplyBuiltInSoundFontSettings();
        ApplyPerformanceTimingProfile(_settings);
        SaveSettingsQuietly();

        if (_midiInputService.IsConnected
            && (!string.Equals(
                    _midiInputService.ConnectedDevice?.Name,
                    _settings.PreferredMidiDeviceName,
                    StringComparison.OrdinalIgnoreCase)
                || !_settings.AutoConnectMidi))
        {
            DisconnectMidi();
        }

        RefreshMidiDevices(showErrorDialog: false);
        RefreshMidiOutputDevices(showErrorDialog: false);
        if (_settings.AutoConnectMidi)
        {
            TryConnectPreferredMidi(showErrorDialog: true);
        }

        TryConnectPreferredMidiOutput(showErrorDialog: true);

        await ReloadSongLibraryAsync();
        if (ReferenceEquals(PageHost.Content, _practiceSetupView) && _currentSong is not null)
        {
            var compatibility = KeyboardCompatibilityService.Analyze(_currentSong.Score, _settings);
            if (!_settings.HasOctaveShift)
            {
                _currentPreferRecommendedDeviceOctaveShift = false;
                _currentAppliedDeviceOctaveShiftOverride = 0;
            }

            _practiceSetupView.SetSong(
                _currentSong,
                compatibility,
                _practiceHistoryService.GetForSong(_currentSong.FilePath),
                _currentPreferRecommendedDeviceOctaveShift,
                _currentAppliedDeviceOctaveShiftOverride,
                _currentSongUsesOctaveCompression);
        }

        _settingsWindow?.View.SetState(
            _settings,
            _midiDevices.Select(device => device.Name).ToArray(),
            _midiOutputDevices.Select(device => device.Name).ToArray(),
            $"保存しました / {GetMidiStatusText()}");
    }

    private void SaveSettingsQuietly()
    {
        try
        {
            _settingsService.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (MidiStatusText is not null)
            {
                MidiStatusText.Text = $"設定保存失敗: {ex.Message}";
            }
        }
    }

    private void AdvancePracticePlayback()
    {
        if (_disposed || !Monitor.TryEnter(_practiceWorkflowSync))
        {
            return;
        }

        try
        {
            _practiceWorkflow.AdvancePlayback();
        }
        finally
        {
            Monitor.Exit(_practiceWorkflowSync);
        }
    }

    private void PlaybackScheduler_SchedulerError(
        object? sender,
        PracticePlaybackSchedulerErrorEventArgs e)
    {
        Dispatcher.BeginInvoke(() => MessageBox.Show(
            this,
            e.Exception.Message,
            "再生処理エラー",
            MessageBoxButton.OK,
            MessageBoxImage.Error));
    }

    private void QueuePracticeSnapshot(PracticeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Interlocked.Exchange(
            ref _pendingPracticeSnapshot,
            snapshot);
        Interlocked.Increment(
            ref _presentationUiVersion);
        RequestPresentationUiFlush();
    }

    private T UsePracticeWorkflow<T>(Func<PracticeWorkflow, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_practiceWorkflowSync)
        {
            return action(_practiceWorkflow);
        }
    }

    private void UsePracticeWorkflow(Action<PracticeWorkflow> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_practiceWorkflowSync)
        {
            action(_practiceWorkflow);
        }
    }

    private void ApplyPracticeSnapshot(PracticeSnapshot snapshot)
    {
        if (ReferenceEquals(PageHost.Content, _performanceView))
        {
            _performanceView.SetSnapshot(snapshot);
        }

        if (_currentSong is not null
            && snapshot.LastResult is not null
            && snapshot.State == PracticeRunState.Completed
            && snapshot.LastResult.CompletedAtUtc != _lastPersistedResultCompletedAt)
        {
            try
            {
                _practiceHistoryService.Append(_currentSong.FilePath, snapshot.LastResult);
                _lastPersistedResultCompletedAt = snapshot.LastResult.CompletedAtUtc;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MidiStatusText.Text = $"履歴保存失敗: {ex.Message}";
            }
        }
    }

    private void RefreshMidiDevices(bool showErrorDialog)
    {
        try
        {
            _midiDevices = _midiInputService.GetDevices().ToArray();
            UpdateMidiConnectionUi();
        }
        catch (Exception ex)
        {
            _midiDevices = Array.Empty<MidiInputDevice>();
            MidiStatusText.Text = "MIDI再検出失敗";
            if (showErrorDialog)
            {
                MessageBox.Show(this, ex.Message, "MIDIデバイス取得エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void TryConnectPreferredMidi(bool showErrorDialog)
    {
        var device = FindPreferredMidiDevice();
        if (device is null)
        {
            UpdateMidiConnectionUi();
            return;
        }

        if (_midiInputService.IsConnected
            && string.Equals(
                _midiInputService.ConnectedDevice?.Name,
                device.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            UpdateMidiConnectionUi();
            return;
        }

        try
        {
            if (_midiInputService.IsConnected)
            {
                _midiInputService.Disconnect();
            }

            _midiInputService.Connect(device);
            _settings.PreferredMidiDeviceName = device.Name;
            ClearActiveMidiNotes();
            MidiInputMonitorText.Text = "入力: 待機中";
            UpdateMidiConnectionUi();
        }
        catch (Exception ex)
        {
            MidiStatusText.Text = "MIDI接続失敗";
            if (showErrorDialog)
            {
                MessageBox.Show(this, ex.Message, "MIDI接続エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private MidiInputDevice? FindPreferredMidiDevice()
    {
        if (_midiDevices.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(_settings.PreferredMidiDeviceName))
        {
            var preferred = _midiDevices.FirstOrDefault(device => string.Equals(
                device.Name,
                _settings.PreferredMidiDeviceName,
                StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return _midiDevices[0];
    }

    private void DisconnectMidi()
    {
        _midiInputService.Disconnect();
        UsePracticeWorkflow(workflow => workflow.AllNotesOff());
        ClearActiveMidiNotes();
        MidiInputMonitorText.Text = "入力: —";
        UpdateMidiConnectionUi();
    }

    private void RefreshMidiOutputDevices(bool showErrorDialog)
    {
        try
        {
            _midiOutputDevices = _midiSoundService.GetDevices().ToArray();
            UpdateMidiConnectionUi();
        }
        catch (Exception ex)
        {
            _midiOutputDevices = Array.Empty<MidiOutputDevice>();
            MidiStatusText.Text = "MIDI OUT再検出失敗";
            if (showErrorDialog)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "MIDI出力デバイス取得エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void TryConnectPreferredMidiOutput(bool showErrorDialog)
    {
        var device = MidiOutputSelector.Select(
            _midiOutputDevices,
            _settings.PreferredMidiOutputDeviceName);
        if (device is null)
        {
            _midiSoundService.Disconnect();
            UpdateMidiConnectionUi();
            return;
        }

        if (_midiSoundService.IsAvailable
            && string.Equals(
                _midiSoundService.DeviceName,
                device.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            _midiSoundService.Connect(device);
            _settings.PreferredMidiOutputDeviceName = device.Name;
            UpdateMidiConnectionUi();
        }
        catch (Exception ex)
        {
            _midiSoundService.Disconnect();
            MidiStatusText.Text = $"MIDI OUT接続失敗: {device.Name}";
            if (showErrorDialog)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "MIDI出力接続エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void UpdateMidiConnectionUi()
        => MidiStatusText.Text = GetMidiStatusText();

    private string GetMidiStatusText()
    {
        var inputText = _midiInputService.IsConnected
            ? $"IN: {_midiInputService.ConnectedDevice?.Name}"
            : _midiDevices.Count > 0
                ? $"IN: {_midiDevices.Count}台検出 / 未接続"
                : "IN: デバイスなし";
        var outputText = _midiSoundService.IsAvailable
            ? $"OUT: {_midiSoundService.DeviceName}"
            : _midiOutputDevices.Count > 0
                ? $"OUT: {_midiOutputDevices.Count}台検出 / 未接続"
                : "OUT: デバイスなし";
        return $"MIDI {inputText} / {outputText}";
    }

    private void MidiInputService_NoteOn(object? sender, MidiNoteEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        var settingsView = _settingsWindow?.View;
        if (settingsView?.IsKeyboardRangeCaptureActive == true)
        {
            Dispatcher.BeginInvoke(() => settingsView.CaptureKeyboardRangeMidiNote(e.MidiNote));
            return;
        }

        try
        {
            var mappedMidiNote = _practiceWorkflow.MapInputMidiNote(e.MidiNote);
            _midiSoundService.NoteOn(
                mappedMidiNote,
                Math.Clamp(e.Velocity, 1, 127),
                e.Channel);

            PracticeSnapshot snapshot;
            lock (_practiceWorkflowSync)
            {
                snapshot = _practiceWorkflow.ProcessNoteOn(e.MidiNote);
            }

            _activeMidiNotes[mappedMidiNote] = 0;
            Interlocked.Exchange(
                ref _pendingMidiInputText,
                $"入力: {MidiPitch.ToName(e.MidiNote)} / MIDI {e.MidiNote} / vel {e.Velocity}");
            QueuePracticeSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            DispatchMidiProcessingError(ex);
        }
    }

    private void MidiInputService_NoteOff(object? sender, MidiNoteEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var mappedMidiNote = _practiceWorkflow.MapInputMidiNote(e.MidiNote);
            _midiSoundService.NoteOff(
                mappedMidiNote,
                Math.Clamp(e.Velocity, 0, 127),
                e.Channel);

            PracticeSnapshot snapshot;
            lock (_practiceWorkflowSync)
            {
                snapshot = _practiceWorkflow.ProcessNoteOff(e.MidiNote);
            }

            _activeMidiNotes.TryRemove(mappedMidiNote, out _);
            QueuePracticeSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            DispatchMidiProcessingError(ex);
        }
    }

    private void MidiInputService_ControlChange(object? sender, MidiControlChangeEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _midiSoundService.ControlChange(
                e.Controller,
                e.Value,
                e.Channel);
        }
        catch (Exception ex)
        {
            DispatchMidiProcessingError(ex);
        }
    }

    private void MidiInputService_InputError(object? sender, MidiInputErrorEventArgs e)
        => Dispatcher.BeginInvoke(() => MidiStatusText.Text = e.Message);

    private void DispatchMidiProcessingError(Exception exception)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                MidiStatusText.Text = $"MIDI処理エラー: {exception.Message}";
            }
        });
    }

    private void ClearActiveMidiNotes()
    {
        _activeMidiNotes.Clear();
        Interlocked.Exchange(ref _pendingPracticeSnapshot, null);
        Interlocked.Exchange(ref _pendingMidiInputText, null);
        var version = Interlocked.Increment(ref _presentationUiVersion);

        if (Dispatcher.CheckAccess())
        {
            _performanceView.SetActiveMidiNotes(
                Array.Empty<int>());
            _appliedPresentationUiVersion =
                version;
            return;
        }

        RequestPresentationUiFlush();
    }
}
