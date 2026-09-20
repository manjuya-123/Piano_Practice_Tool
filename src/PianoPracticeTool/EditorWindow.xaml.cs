using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PianoPracticeTool.Core;
using PianoPracticeTool.Core.Editing;
using PianoPracticeTool.Services.Editor;
using PianoPracticeTool.Services.Midi;
using PianoPracticeTool.Services.Settings;
using PianoPracticeTool.Services.Songs;
using PianoPracticeTool.Views;

namespace PianoPracticeTool;

public sealed partial class EditorWindow : Window, IDisposable
{
    private readonly EditorSongLibraryView _libraryView = new();
    private readonly ScoreEditorView _editorView = new();
    private readonly AppSettingsService _settingsService = new();
    private readonly IMidiSoundService _soundService = new NAudioMidiSoundService();
    private readonly EditorPreviewPlayer _previewPlayer;
    private readonly EditorReferenceAudioPlayer _referenceAudioPlayer;
    private readonly object _previewPositionSync = new();

    private AppSettings _settings;
    private SettingsWindow? _settingsWindow;
    private int _editorOctaveShiftSemitones;
    private double _pendingPreviewBeat;
    private double _pendingReferenceAudioSeconds;
    private bool _hasPendingPreviewBeat;
    private bool _pendingPreviewIsReference;
    private bool _previewPositionRenderingSubscribed;
    private bool _referencePlaybackActive;
    private bool _referencePlaybackFollowsScoreCursor;
    private bool _referenceComparisonActive;
    private ReferenceComparisonPhase _referenceComparisonPhase;
    private ScoreEditorPreviewRequestedEventArgs? _referenceComparisonRequest;
    private ReferenceAudioProject _referenceAudioProject = new();
    private ReferenceAudioWaveform? _referenceAudioWaveform;
    private ReferenceAudioSynchronizer? _referenceAudioSynchronizer;
    private string? _loadedReferenceAudioPath;
    private bool _suppressPlaybackStoppedNotification;
    private bool _disposed;
    private bool _closingConfirmed;

    public EditorWindow()
    {
        _settings = _settingsService.Load();
        _previewPlayer = new EditorPreviewPlayer(
            _soundService,
            _settings.AutomaticPlaybackVelocity);
        _referenceAudioPlayer = new EditorReferenceAudioPlayer(
            _soundService,
            _settings.AutomaticPlaybackVelocity);
        InitializeComponent();
        _editorView.SetTimelineDragMode(_settings.EditorTimelineDragMode);
        _editorView.SetRollPanPitchEnabled(_settings.EditorPanPitchEnabled);
        WireEvents();
        Loaded += EditorWindow_Loaded;
        Closing += EditorWindow_Closing;
        Closed += EditorWindow_Closed;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopPreviewPositionRendering();
        UnwireEvents();
        Loaded -= EditorWindow_Loaded;
        Closing -= EditorWindow_Closing;
        Closed -= EditorWindow_Closed;
        _previewPlayer.Dispose();
        _referenceAudioPlayer.Dispose();
        _soundService.Dispose();
        GC.SuppressFinalize(this);
    }

    private void WireEvents()
    {
        _libraryView.DirectEditRequested += LibraryView_DirectEditRequested;
        _libraryView.DuplicateEditRequested += LibraryView_DuplicateEditRequested;
        _libraryView.OpenFileRequested += LibraryView_OpenFileRequested;
        _libraryView.RefreshRequested += LibraryView_RefreshRequested;
        _libraryView.BackRequested += LibraryView_BackRequested;
        _editorView.BackRequested += EditorView_BackRequested;
        _editorView.SaveRequested += EditorView_SaveRequested;
        _editorView.WorkspaceSaveRequested +=
            EditorView_WorkspaceSaveRequested;
        _editorView.PreviewRequested += EditorView_PreviewRequested;
        _editorView.PausePreviewRequested += EditorView_PausePreviewRequested;
        _editorView.ResumePreviewRequested += EditorView_ResumePreviewRequested;
        _editorView.StopPreviewRequested += EditorView_StopPreviewRequested;
        _editorView.OctaveShiftChanged += EditorView_OctaveShiftChanged;
        _editorView.ReferenceAudioFileRequested += EditorView_ReferenceAudioFileRequested;
        _editorView.ReferenceAudioProjectChanged += EditorView_ReferenceAudioProjectChanged;
        _editorView.ReferenceAudioSeekRequested += EditorView_ReferenceAudioSeekRequested;
        _previewPlayer.PositionChanged += PreviewPlayer_PositionChanged;
        _previewPlayer.PlaybackStopped += PreviewPlayer_PlaybackStopped;
        _referenceAudioPlayer.PositionChanged += ReferenceAudioPlayer_PositionChanged;
        _referenceAudioPlayer.PlaybackStopped += ReferenceAudioPlayer_PlaybackStopped;
    }

    private void UnwireEvents()
    {
        _libraryView.DirectEditRequested -= LibraryView_DirectEditRequested;
        _libraryView.DuplicateEditRequested -= LibraryView_DuplicateEditRequested;
        _libraryView.OpenFileRequested -= LibraryView_OpenFileRequested;
        _libraryView.RefreshRequested -= LibraryView_RefreshRequested;
        _libraryView.BackRequested -= LibraryView_BackRequested;
        _editorView.BackRequested -= EditorView_BackRequested;
        _editorView.SaveRequested -= EditorView_SaveRequested;
        _editorView.WorkspaceSaveRequested -=
            EditorView_WorkspaceSaveRequested;
        _editorView.PreviewRequested -= EditorView_PreviewRequested;
        _editorView.PausePreviewRequested -= EditorView_PausePreviewRequested;
        _editorView.ResumePreviewRequested -= EditorView_ResumePreviewRequested;
        _editorView.StopPreviewRequested -= EditorView_StopPreviewRequested;
        _editorView.OctaveShiftChanged -= EditorView_OctaveShiftChanged;
        _editorView.ReferenceAudioFileRequested -= EditorView_ReferenceAudioFileRequested;
        _editorView.ReferenceAudioProjectChanged -= EditorView_ReferenceAudioProjectChanged;
        _editorView.ReferenceAudioSeekRequested -= EditorView_ReferenceAudioSeekRequested;
        _previewPlayer.PositionChanged -= PreviewPlayer_PositionChanged;
        _previewPlayer.PlaybackStopped -= PreviewPlayer_PlaybackStopped;
        _referenceAudioPlayer.PositionChanged -= ReferenceAudioPlayer_PositionChanged;
        _referenceAudioPlayer.PlaybackStopped -= ReferenceAudioPlayer_PlaybackStopped;
    }

    private async void EditorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _soundService.Initialize();
            _soundService.ConfigureBuiltInSoundFont(
                SoundFontSettings.CreateConfiguration(_settings));
            ConnectEditorMidiOutput();
            EditorStatusText.Text = $"編集モード / MIDI OUT: {_soundService.DeviceName ?? "未接続"}";
        }
        catch (Exception ex)
        {
            EditorStatusText.Text = $"編集モード / MIDI OUT初期化失敗: {ex.Message}";
        }

        await ReloadLibraryAsync();
        ShowLibrary();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is not null)
        {
            return;
        }

        IReadOnlyList<MidiOutputDevice> outputDevices;
        try
        {
            outputDevices = _soundService.GetDevices().ToArray();
        }
        catch (Exception ex)
        {
            outputDevices = Array.Empty<MidiOutputDevice>();
            EditorMidiStatusText.Text = $"MIDI OUT再検出失敗: {ex.Message}";
        }

        var window = new SettingsWindow(
            _settings,
            _editorMidiDevices.Select(device => device.Name).ToArray(),
            outputDevices.Select(device => device.Name).ToArray(),
            EditorMidiStatusText.Text)
        {
            Owner = this
        };

        _settingsWindow = window;
        window.View.RefreshMidiRequested += EditorSettingsView_RefreshMidiRequested;
        window.View.BuiltInSoundFontPreviewChanged += EditorSettingsView_BuiltInSoundFontPreviewChanged;
        window.View.BuiltInSoundFontTestRequested += EditorSettingsView_BuiltInSoundFontTestRequested;
        window.View.SaveRequested += EditorSettingsView_SaveRequested;

        try
        {
            window.ShowDialog();
        }
        finally
        {
            window.View.RefreshMidiRequested -= EditorSettingsView_RefreshMidiRequested;
            window.View.BuiltInSoundFontPreviewChanged -= EditorSettingsView_BuiltInSoundFontPreviewChanged;
            window.View.BuiltInSoundFontTestRequested -= EditorSettingsView_BuiltInSoundFontTestRequested;
            window.View.SaveRequested -= EditorSettingsView_SaveRequested;
            _settingsWindow = null;

            try
            {
                _soundService.ConfigureBuiltInSoundFont(
                    SoundFontSettings.CreateConfiguration(_settings));
            }
            catch (Exception ex)
            {
                EditorMidiStatusText.Text = $"SoundFont設定の復元失敗: {ex.Message}";
            }
            if (ReferenceEquals(PageHost.Content, _editorView))
            {
                _editorView.ActivateEditorInput();
            }
        }
    }

    private void EditorSettingsView_RefreshMidiRequested(object? sender, EventArgs e)
    {
        RefreshEditorMidiDevices();

        try
        {
            var outputDevices = _soundService.GetDevices().ToArray();
            _settingsWindow?.View.UpdateMidiState(
                _editorMidiDevices.Select(device => device.Name).ToArray(),
                outputDevices.Select(device => device.Name).ToArray(),
                EditorMidiStatusText.Text);
        }
        catch (Exception ex)
        {
            _settingsWindow?.View.SetStatus($"MIDI OUT再検出失敗: {ex.Message}");
        }
    }

    private void EditorSettingsView_BuiltInSoundFontPreviewChanged(
        object? sender,
        SoundFontConfigurationEventArgs e)
    {
        try
        {
            _soundService.ConfigureBuiltInSoundFont(e.Configuration);
        }
        catch (Exception ex)
        {
            _settingsWindow?.View.SetStatus($"SoundFontプレビュー更新失敗: {ex.Message}");
        }
    }

    private async void EditorSettingsView_BuiltInSoundFontTestRequested(
        object? sender,
        SoundFontConfigurationEventArgs e)
    {
        if (!string.Equals(
                _soundService.DeviceName,
                MidiOutputDevice.BuiltInSoundFont.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            _settingsWindow?.View.SetStatus(
                "発音先に「Built-in SoundFont Synthesizer」を選択して設定を保存してください。");
            return;
        }

        try
        {
            _previewPlayer.Stop();
            _referenceAudioPlayer.Stop();
            _soundService.AllNotesOff();
            _soundService.ConfigureBuiltInSoundFont(e.Configuration);
            _soundService.NoteOn(60, 127, 1);
            await Task.Delay(700);
            _soundService.NoteOff(60, 0, 1);
            await Task.Delay(900);
            _settingsWindow?.View.SetStatus("現在のSoundFont・Preset・リバーブ設定で試聴しました。");
        }
        catch (Exception ex)
        {
            _soundService.ConfigureBuiltInSoundFont(
                SoundFontSettings.CreateConfiguration(_settings));
            _settingsWindow?.View.SetStatus($"SoundFont音源の試聴に失敗しました: {ex.Message}");
        }
    }

    private async void EditorSettingsView_SaveRequested(object? sender, SettingsSaveRequestedEventArgs e)
    {
        try
        {
            _settings = e.Settings.Clone();
            _settingsService.Save(_settings);
            _previewPlayer.SetVelocity(_settings.AutomaticPlaybackVelocity);
            _referenceAudioPlayer.SetVelocity(_settings.AutomaticPlaybackVelocity);
            _editorView.SetTimelineDragMode(_settings.EditorTimelineDragMode);
            _editorView.SetRollPanPitchEnabled(_settings.EditorPanPitchEnabled);
            _soundService.ConfigureBuiltInSoundFont(
                SoundFontSettings.CreateConfiguration(_settings));

            if (_editorMidiInputService.IsConnected
                && (!_settings.AutoConnectMidi
                    || !string.Equals(
                        _editorMidiInputService.ConnectedDevice?.Name,
                        _settings.PreferredMidiDeviceName,
                        StringComparison.OrdinalIgnoreCase)))
            {
                _editorMidiInputService.Disconnect();
            }

            RefreshEditorMidiDevices();
            if (_settings.AutoConnectMidi)
            {
                TryConnectEditorMidi();
            }

            ConnectEditorMidiOutput();
            if (ReferenceEquals(PageHost.Content, _editorView))
            {
                ConfigureEditorOctaveShift(_editorView.GetCurrentScore());
            }
            else
            {
                ApplyEditorKeyboardRange();
            }

            await ReloadLibraryAsync();

            var outputDevices = _soundService.GetDevices().ToArray();
            _settingsWindow?.View.SetState(
                _settings,
                _editorMidiDevices.Select(device => device.Name).ToArray(),
                outputDevices.Select(device => device.Name).ToArray(),
                $"保存しました / {EditorMidiStatusText.Text}");
        }
        catch (Exception ex)
        {
            _settingsWindow?.View.SetStatus($"設定保存失敗: {ex.Message}");
        }
    }

    private void ConnectEditorMidiOutput()
    {
        var devices = _soundService.GetDevices();
        var device = MidiOutputSelector.Select(
            devices,
            _settings.PreferredMidiOutputDeviceName);
        if (device is null)
        {
            _soundService.Disconnect();
            return;
        }

        _soundService.Connect(device);
        _settings.PreferredMidiOutputDeviceName = device.Name;
    }

    private void EditorWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_closingConfirmed
            || !ReferenceEquals(
                PageHost.Content,
                _editorView)
            || !_editorView.HasUnsavedWorkChanges)
        {
            return;
        }

        if (!ConfirmLeaveEditor())
        {
            e.Cancel = true;
            return;
        }

        _closingConfirmed = true;
    }

    private void EditorWindow_Closed(object? sender, EventArgs e)
        => Dispose();

    private async Task ReloadLibraryAsync()
    {
        try
        {
            _settings = _settingsService.Load();
            _previewPlayer.SetVelocity(_settings.AutomaticPlaybackVelocity);
            _referenceAudioPlayer.SetVelocity(_settings.AutomaticPlaybackVelocity);
            _editorView.SetTimelineDragMode(_settings.EditorTimelineDragMode);
            _editorView.SetRollPanPitchEnabled(_settings.EditorPanPitchEnabled);
            var catalog = await SongCatalogService.LoadDirectoriesAsync(
                _settings.SongFolders,
                _settings.SongDifficultyOverrides);
            if (!_disposed)
            {
                _libraryView.SetSongs(catalog.Songs, catalog.Errors);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _libraryView.SetSongs(
                Array.Empty<SongCatalogEntry>(),
                new[] { $"楽曲探索に失敗しました: {ex.Message}" });
        }
    }

    private void ShowLibrary()
    {
        StopPreviewPlayersSilently();
        PageHost.Content = _libraryView;
        EditorStatusText.Text = "編集モード / 楽曲を選択してください";
    }

    private async Task OpenEditorAsync(
        string path,
        SongDifficulty? fallbackDifficulty = null)
    {
        try
        {
            var document =
                await EditorScoreLoader.LoadAsync(
                    path,
                    _settings.SongDifficultyOverrides);
            var xmlDifficulty =
                document.Difficulty
                    == SongDifficulty.Unknown
                    && fallbackDifficulty is not null
                    ? fallbackDifficulty.Value
                    : document.Difficulty;

            EditorWorkspaceDocument? workspace =
                null;
            if (EditorWorkspaceService.Exists(
                    document.FilePath))
            {
                try
                {
                    var candidate =
                        EditorWorkspaceService.Load(
                            document.FilePath);
                    var xmlChanged =
                        EditorWorkspaceService
                            .SourceMusicXmlChanged(
                                document.FilePath,
                                candidate);
                    var message =
                        "このMusicXMLには編集途中の作業データがあります。\n\n"
                        + $"作業保存: {candidate.SavedAtUtc.ToLocalTime():yyyy/MM/dd HH:mm:ss}\n"
                        + (xmlChanged
                            ? "注意: MusicXMLは作業保存後に変更されています。\n作業データを開くと、作業保存時の編集内容を優先します。\n\n"
                            : string.Empty)
                        + "[はい] 作業データから再開\n"
                        + "[いいえ] MusicXMLだけを開く\n"
                        + "[キャンセル] 開かない";
                    var choice =
                        MessageBox.Show(
                            this,
                            message,
                            "編集途中の作業データ",
                            MessageBoxButton.YesNoCancel,
                            xmlChanged
                                ? MessageBoxImage.Warning
                                : MessageBoxImage.Question);
                    if (choice
                        == MessageBoxResult.Cancel)
                    {
                        return;
                    }

                    if (choice
                        == MessageBoxResult.Yes)
                    {
                        workspace =
                            candidate;
                    }
                }
                catch (InvalidDataException ex)
                {
                    MessageBox.Show(
                        this,
                        ex.Message
                        + "\n\nMusicXMLのみを開きます。",
                        "作業データ読込エラー",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            var score =
                workspace?.Score
                ?? document.Score;
            var difficulty =
                workspace?.Difficulty
                ?? xmlDifficulty;

            StopPreviewPlayersSilently();
            _editorOctaveShiftSemitones = 0;
            _editorView.SetDocument(
                document.FilePath,
                score,
                difficulty,
                savedScore:
                    workspace is null
                        ? null
                        : document.Score,
                savedDifficulty:
                    workspace is null
                        ? null
                        : xmlDifficulty);
            await LoadReferenceAudioProjectAsync(
                document.FilePath,
                score,
                workspace?.ReferenceAudioProject);
            if (workspace is not null)
            {
                _editorView.RestoreWorkspaceViewState(
                    workspace.ViewState);
            }

            _editorView.MarkWorkSaved();
            ConfigureEditorOctaveShift(
                score);
            PageHost.Content =
                _editorView;
            _editorView.ActivateEditorInput();
            EditorStatusText.Text =
                workspace is null
                    ? $"編集中: {document.FileName}"
                    : $"作業を再開: {document.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "MusicXML読込エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void LibraryView_DirectEditRequested(object? sender, EditorSongRequestedEventArgs e)
        => await OpenEditorAsync(e.Song.FilePath);

    private async void LibraryView_DuplicateEditRequested(object? sender, EditorSongRequestedEventArgs e)
    {
        var destination = SelectDuplicateDestination(e.Song.FilePath);
        if (destination is null)
        {
            return;
        }

        try
        {
            File.Copy(e.Song.FilePath, destination, overwrite: true);
            EditorReferenceAudioProjectService.CopyForDuplicate(
                e.Song.FilePath,
                destination);
            await OpenEditorAsync(destination, e.Song.Difficulty);
            await ReloadLibraryAsync();
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            MessageBox.Show(this, ex.Message, "複製エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LibraryView_OpenFileRequested(object? sender, EventArgs e)
    {
        var openDialog = new OpenFileDialog
        {
            Title = "編集するMusicXMLを選択",
            Filter = "MusicXML (*.xml;*.musicxml)|*.xml;*.musicxml|All files (*.*)|*.*"
        };
        if (openDialog.ShowDialog(this) != true)
        {
            return;
        }

        var choice = MessageBox.Show(
            this,
            "このファイルを直接編集しますか？\n\n[はい] 直接編集\n[いいえ] 複製して編集\n[キャンセル] 戻る",
            "編集方法",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (choice == MessageBoxResult.Cancel)
        {
            return;
        }

        if (choice == MessageBoxResult.Yes)
        {
            await OpenEditorAsync(openDialog.FileName);
            return;
        }

        var destination = SelectDuplicateDestination(openDialog.FileName);
        if (destination is null)
        {
            return;
        }

        try
        {
            File.Copy(openDialog.FileName, destination, overwrite: true);
            EditorReferenceAudioProjectService.CopyForDuplicate(
                openDialog.FileName,
                destination);
            await OpenEditorAsync(destination);
            await ReloadLibraryAsync();
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            MessageBox.Show(this, ex.Message, "複製エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LibraryView_RefreshRequested(object? sender, EventArgs e)
        => await ReloadLibraryAsync();

    private void LibraryView_BackRequested(object? sender, EventArgs e)
    {
        _closingConfirmed = true;
        Close();
    }

    private async void EditorView_BackRequested(object? sender, EventArgs e)
    {
        if (!ConfirmLeaveEditor())
        {
            return;
        }

        StopPreviewPlayersSilently();
        await ReloadLibraryAsync();
        ShowLibrary();
    }

    private void EditorView_SaveRequested(
        object? sender,
        ScoreEditorSaveRequestedEventArgs e)
        => SaveEditor(
            e.Score,
            e.Difficulty,
            e.SaveAs);

    private void EditorView_WorkspaceSaveRequested(
        object? sender,
        EventArgs e)
        => SaveEditorWorkspace();

    private void EditorView_OctaveShiftChanged(
        object? sender,
        ScoreEditorOctaveShiftChangedEventArgs e)
    {
        _editorOctaveShiftSemitones = e.Semitones;
        ApplyEditorKeyboardRange();
        UpdateEditorMidiStatus();
    }

    private async Task LoadReferenceAudioProjectAsync(
        string musicXmlPath,
        MusicScore score,
        ReferenceAudioProject? workspaceProject = null)
    {
        StopPreviewPlayersSilently();
        _referenceAudioWaveform = null;
        _loadedReferenceAudioPath = null;

        ReferenceAudioProject? savedProject =
            null;
        try
        {
            savedProject =
                EditorReferenceAudioProjectService.Load(
                    musicXmlPath);
            _referenceAudioProject =
                workspaceProject?.Clone()
                ?? savedProject.Clone();
            _referenceAudioSynchronizer =
                new ReferenceAudioSynchronizer(
                    score,
                    _referenceAudioProject.SyncPoints);
            ApplyReferenceComparisonVolumes();

            if (_referenceAudioProject.Enabled
                && !string.IsNullOrWhiteSpace(
                    _referenceAudioProject.AudioPath)
                && File.Exists(
                    _referenceAudioProject.AudioPath))
            {
                await LoadReferenceAudioAssetsAsync(
                    _referenceAudioProject);
                EnsureReferenceAudioOuterAnchors(
                    score);
            }
            else if (_referenceAudioProject.Enabled
                     && !string.IsNullOrWhiteSpace(
                         _referenceAudioProject.AudioPath))
            {
                MessageBox.Show(
                    this,
                    "登録されている耳コピ用の元音源が見つかりません。\n「音源選択…」から選び直してください。",
                    "元音源",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (InvalidDataException ex)
        {
            _referenceAudioProject =
                new ReferenceAudioProject();
            _referenceAudioSynchronizer =
                new ReferenceAudioSynchronizer(
                    score,
                    Array.Empty<ReferenceAudioSyncPoint>());
            MessageBox.Show(
                this,
                ex.Message,
                "元音源設定エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _referenceAudioPlayer.Unload();
            _referenceAudioWaveform = null;
            _loadedReferenceAudioPath = null;
            MessageBox.Show(
                this,
                $"元音源を読み込めませんでした。MusicXML編集はそのまま続けられます。\n\n{ex.Message}",
                "元音源読込エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _editorView.SetReferenceAudioProject(
            _referenceAudioProject,
            _referenceAudioWaveform,
            markSaved:
                workspaceProject is null);
        if (workspaceProject is not null
            && savedProject is not null)
        {
            _editorView.SetSavedReferenceAudioProjectBaseline(
                savedProject);
        }
    }

    private async Task LoadReferenceAudioAssetsAsync(
        ReferenceAudioProject project)
    {
        if (string.IsNullOrWhiteSpace(
                project.AudioPath))
        {
            _referenceAudioWaveform = null;
            _loadedReferenceAudioPath = null;
            return;
        }

        var fullPath = Path.GetFullPath(
            project.AudioPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "元音源ファイルが見つかりません。",
                fullPath);
        }

        if (!string.Equals(
                _loadedReferenceAudioPath,
                fullPath,
                StringComparison.OrdinalIgnoreCase)
            || !_referenceAudioPlayer.IsLoaded)
        {
            _referenceAudioPlayer.Load(
                fullPath,
                project.ReferenceVolumePercent,
                project.ReferencePanPercent);
            _referenceAudioWaveform =
                await ReferenceAudioWaveformService.LoadAsync(
                    fullPath);
            _loadedReferenceAudioPath =
                fullPath;
        }
        else
        {
            _referenceAudioPlayer.SetVolumePercent(
                project.ReferenceVolumePercent);
            _referenceAudioPlayer.SetPanPercent(
                project.ReferencePanPercent);
        }
    }

    private void EnsureReferenceAudioOuterAnchors(
        MusicScore score)
    {
        if (_referenceAudioWaveform is null)
        {
            return;
        }

        _referenceAudioProject.SyncPoints =
            ReferenceAudioSynchronizer
                .EnsureOuterAnchors(
                    _referenceAudioProject.SyncPoints,
                    score.LengthBeats,
                    _referenceAudioWaveform.DurationSeconds)
                .ToList();
        _referenceAudioSynchronizer =
            new ReferenceAudioSynchronizer(
                score,
                _referenceAudioProject.SyncPoints);
    }

    private async void EditorView_ReferenceAudioFileRequested(
        object? sender,
        EventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "耳コピ用の元音源を選択",
            Filter = "Audio (*.wav;*.mp3;*.aiff;*.aif)|*.wav;*.mp3;*.aiff;*.aif|All files (*.*)|*.*"
        };

        if (!string.IsNullOrWhiteSpace(
                _referenceAudioProject.AudioPath))
        {
            dialog.InitialDirectory =
                Path.GetDirectoryName(
                    _referenceAudioProject.AudioPath);
        }

        if (dialog.ShowDialog(this) != true)
        {
            if (_referenceAudioProject.Enabled
                && string.IsNullOrWhiteSpace(
                    _referenceAudioProject.AudioPath))
            {
                _referenceAudioProject.Enabled = false;
                _editorView.SetReferenceAudioProject(
                    _referenceAudioProject,
                    waveform: null);
            }

            return;
        }

        try
        {
            var project =
                _referenceAudioProject.Clone();
            project.Enabled = true;
            project.AudioPath =
                Path.GetFullPath(
                    dialog.FileName);
            if (project.SyncPoints.Count == 0)
            {
                project.SyncPoints.Add(
                    new ReferenceAudioSyncPoint(
                        0d,
                        0d));
            }

            _referenceAudioProject = project;
            _referenceAudioSynchronizer =
                new ReferenceAudioSynchronizer(
                    _editorView.GetCurrentScore(),
                    project.SyncPoints);
            await LoadReferenceAudioAssetsAsync(
                project);
            EnsureReferenceAudioOuterAnchors(
                _editorView.GetCurrentScore());
            _editorView.SetReferenceAudioProject(
                _referenceAudioProject,
                _referenceAudioWaveform);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or ArgumentException)
        {
            _referenceAudioPlayer.Unload();
            _referenceAudioWaveform = null;
            _loadedReferenceAudioPath = null;
            _editorView.SetReferenceAudioProject(
                _referenceAudioProject,
                waveform: null);
            MessageBox.Show(
                this,
                ex.Message,
                "元音源読込エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void EditorView_ReferenceAudioProjectChanged(
        object? sender,
        ScoreEditorReferenceAudioProjectChangedEventArgs e)
    {
        _referenceAudioProject =
            e.Project.Clone();
        _referenceAudioSynchronizer =
            new ReferenceAudioSynchronizer(
                _editorView.GetCurrentScore(),
                _referenceAudioProject.SyncPoints);
        ApplyReferenceComparisonVolumes();

        try
        {
            if (_referenceAudioProject.Enabled
                && !string.IsNullOrWhiteSpace(
                    _referenceAudioProject.AudioPath)
                && File.Exists(
                    _referenceAudioProject.AudioPath))
            {
                await LoadReferenceAudioAssetsAsync(
                    _referenceAudioProject);
                EnsureReferenceAudioOuterAnchors(
                    _editorView.GetCurrentScore());
                _editorView.SetReferenceAudioProject(
                    _referenceAudioProject,
                    _referenceAudioWaveform);
            }

            if (!_referenceAudioProject.Enabled)
            {
                var wasComparison =
                    _referenceComparisonActive;
                _referenceComparisonActive = false;
                _referenceComparisonPhase =
                    ReferenceComparisonPhase.None;
                _referenceComparisonRequest = null;

                if (_referencePlaybackActive)
                {
                    _referenceAudioPlayer.Stop();
                    _referencePlaybackActive = false;
                }
                else if (wasComparison
                         && (_previewPlayer.IsPlaying
                             || _previewPlayer.IsPaused))
                {
                    _previewPlayer.Stop();
                }

                _referenceAudioPlayer.Unload();
                _referenceAudioWaveform = null;
                _loadedReferenceAudioPath = null;
                _editorView.SetReferenceAudioProject(
                    _referenceAudioProject,
                    waveform: null);
            }

        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or ArgumentException)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "元音源設定エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void EditorView_ReferenceAudioSeekRequested(
        object? sender,
        ScoreEditorReferenceAudioSeekRequestedEventArgs e)
    {
        if (_referenceAudioSynchronizer is null)
        {
            return;
        }

        var beat =
            _referenceAudioSynchronizer
                .AudioSecondsToScoreBeat(
                    e.AudioSeconds);
        _referenceAudioPlayer.Seek(
            e.AudioSeconds);
        _editorView.UpdateReferenceAudioSeekPosition(
            beat,
            e.AudioSeconds);
    }

    private void StopPreviewPlayersSilently()
    {
        _suppressPlaybackStoppedNotification = true;
        try
        {
            _previewPlayer.Stop();
            _referenceAudioPlayer.Stop();
        }
        finally
        {
            _suppressPlaybackStoppedNotification = false;
        }

        StopPreviewPositionRendering();
        ClearPendingPreviewPosition();
        _referencePlaybackActive = false;
        _referencePlaybackFollowsScoreCursor = false;
        _referenceComparisonActive = false;
        _referenceComparisonPhase =
            ReferenceComparisonPhase.None;
        _referenceComparisonRequest = null;
    }

    private void ApplyReferenceComparisonVolumes()
    {
        var scoreVolume =
            _referenceAudioProject.Enabled
                ? _referenceAudioProject.ScoreVolumePercent
                : 100;
        var scorePan =
            _referenceAudioProject.Enabled
                ? _referenceAudioProject.ScorePanPercent
                : 0;
        _previewPlayer.SetVolumePercent(
            scoreVolume);
        _previewPlayer.SetPanPercent(
            scorePan);
        _referenceAudioPlayer.SetScoreVolumePercent(
            scoreVolume);
        _referenceAudioPlayer.SetScorePanPercent(
            scorePan);
        _referenceAudioPlayer.SetVolumePercent(
            _referenceAudioProject.ReferenceVolumePercent);
        _referenceAudioPlayer.SetPanPercent(
            _referenceAudioProject.ReferencePanPercent);
    }

    private void ConfigureEditorOctaveShift(MusicScore score)
    {
        var compatibility = KeyboardCompatibilityService.Analyze(score, _settings);
        var availableShifts = compatibility.AvailableOctaveShiftsSemitones;
        if (!availableShifts.Contains(_editorOctaveShiftSemitones))
        {
            _editorOctaveShiftSemitones = 0;
        }

        _editorView.SetOctaveShiftOptions(
            availableShifts,
            _editorOctaveShiftSemitones,
            compatibility.BothHands.RecommendedOctaveShiftSemitones);
        ApplyEditorKeyboardRange();
        UpdateEditorMidiStatus();
    }

    private void EditorView_PreviewRequested(object? sender, ScoreEditorPreviewRequestedEventArgs e)
    {
        try
        {
            ClearPendingPreviewPosition();
            _referenceComparisonActive = false;
            _referenceComparisonPhase =
                ReferenceComparisonPhase.None;
            _referenceComparisonRequest = null;
            _suppressPlaybackStoppedNotification = true;
            try
            {
                _previewPlayer.Stop();
                _referenceAudioPlayer.Stop();
            }
            finally
            {
                _suppressPlaybackStoppedNotification = false;
            }

            _referencePlaybackActive = false;
            ApplyReferenceComparisonVolumes();

            if (e.AlternateCompare)
            {
                StartReferenceComparison(e);
                return;
            }

            if (e.PlaybackMode == EditorReferencePlaybackMode.ScoreOnly)
            {
                _previewPlayer.Play(
                    e.Score,
                    e.StartBeat,
                    e.EndBeat,
                    e.Loop);
            }
            else
            {
                if (!_referenceAudioProject.Enabled
                    || string.IsNullOrWhiteSpace(
                        _referenceAudioProject.AudioPath)
                    || !_referenceAudioPlayer.IsLoaded)
                {
                    throw new InvalidOperationException(
                        "元音源を使用するには、先に音源ファイルを選択してください。");
                }

                _referenceAudioSynchronizer =
                    new ReferenceAudioSynchronizer(
                        e.Score,
                        _referenceAudioProject.SyncPoints);
                _referenceAudioPlayer.Play(
                    e.Score,
                    _referenceAudioSynchronizer,
                    e.StartBeat,
                    e.EndBeat,
                    e.Loop,
                    includeScore:
                        e.PlaybackMode
                        == EditorReferencePlaybackMode.Both,
                    startAudioSecondsOverride:
                        e.ReferenceAudioStartSeconds,
                    endAudioSecondsOverride:
                        e.ReferenceAudioEndSeconds);
                _referencePlaybackActive = true;
                _referencePlaybackFollowsScoreCursor =
                    e.PlaybackMode
                    == EditorReferencePlaybackMode.Both;
            }

            StartPreviewPositionRendering();
            var sourceLabel = e.PlaybackMode switch
            {
                EditorReferencePlaybackMode.ReferenceOnly => "原音",
                EditorReferencePlaybackMode.Both => "原音＋譜面",
                _ => "譜面"
            };
            EditorStatusText.Text =
                e.Loop
                    ? $"編集モード / {sourceLabel} 範囲ループ試聴中"
                    : $"編集モード / {sourceLabel} 試聴中";
        }
        catch (Exception ex)
        {
            StopPreviewPlayersSilently();
            _editorView.NotifyPreviewStopped();
            MessageBox.Show(
                this,
                ex.Message,
                "試聴エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void EditorView_PausePreviewRequested(object? sender, EventArgs e)
    {
        if (_referencePlaybackActive)
        {
            _referenceAudioPlayer.Pause();
        }
        else
        {
            _previewPlayer.Pause();
        }

        StopPreviewPositionRendering();
        ClearPendingPreviewPosition();
        _editorView.NotifyPreviewPaused();
        EditorStatusText.Text = "編集モード / 試聴一時停止";
    }

    private void EditorView_ResumePreviewRequested(object? sender, EventArgs e)
    {
        ClearPendingPreviewPosition();
        if (_referencePlaybackActive)
        {
            _referenceAudioPlayer.Resume();
        }
        else
        {
            _previewPlayer.Resume();
        }

        StartPreviewPositionRendering();
        _editorView.NotifyPreviewResumed();
        EditorStatusText.Text = "編集モード / 試聴再開";
    }

    private void EditorView_StopPreviewRequested(object? sender, EventArgs e)
    {
        StopPreviewPositionRendering();
        ClearPendingPreviewPosition();
        _referenceComparisonActive = false;
        _referenceComparisonPhase =
            ReferenceComparisonPhase.None;
        _referenceComparisonRequest = null;
        if (_referencePlaybackActive)
        {
            _referenceAudioPlayer.Stop();
            _referencePlaybackActive = false;
            _referencePlaybackFollowsScoreCursor = false;
        }
        else
        {
            _previewPlayer.Stop();
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled
            || e.IsRepeat
            || e.Key != Key.Space
            || Keyboard.Modifiers != ModifierKeys.None
            || !ReferenceEquals(PageHost.Content, _editorView))
        {
            return;
        }

        e.Handled = true;
        _editorView.HandlePreviewShortcut();
    }

    private void PreviewPlayer_PositionChanged(object? sender, EditorPreviewPositionEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        lock (_previewPositionSync)
        {
            _pendingPreviewBeat = e.Beat;
            _pendingPreviewIsReference = false;
            _hasPendingPreviewBeat = true;
        }
    }

    private void ReferenceAudioPlayer_PositionChanged(
        object? sender,
        EditorReferenceAudioPositionEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        lock (_previewPositionSync)
        {
            _pendingPreviewBeat = e.ScoreBeat;
            _pendingReferenceAudioSeconds =
                e.AudioSeconds;
            _pendingPreviewIsReference = true;
            _hasPendingPreviewBeat = true;
        }
    }

    private void PreviewPosition_Rendering(object? sender, EventArgs e)
    {
        double beat;
        double audioSeconds;
        bool isReference;
        lock (_previewPositionSync)
        {
            if (!_hasPendingPreviewBeat)
            {
                return;
            }

            beat = _pendingPreviewBeat;
            audioSeconds =
                _pendingReferenceAudioSeconds;
            isReference =
                _pendingPreviewIsReference;
            _hasPendingPreviewBeat = false;
        }

        if (_disposed || !ReferenceEquals(PageHost.Content, _editorView))
        {
            return;
        }

        if (isReference)
        {
            _editorView.UpdateReferenceAudioPlaybackPosition(
                beat,
                audioSeconds,
                _referencePlaybackFollowsScoreCursor);
        }
        else
        {
            _editorView.UpdatePreviewPosition(beat);
        }
    }

    private void StartPreviewPositionRendering()
    {
        if (_previewPositionRenderingSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering += PreviewPosition_Rendering;
        _previewPositionRenderingSubscribed = true;
    }

    private void StopPreviewPositionRendering()
    {
        if (!_previewPositionRenderingSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering -= PreviewPosition_Rendering;
        _previewPositionRenderingSubscribed = false;
    }

    private void ClearPendingPreviewPosition()
    {
        lock (_previewPositionSync)
        {
            _hasPendingPreviewBeat = false;
            _pendingPreviewIsReference = false;
        }
    }

    private void PreviewPlayer_PlaybackStopped(object? sender, EventArgs e)
    {
        if (_suppressPlaybackStoppedNotification)
        {
            return;
        }

        if (_referenceComparisonActive
            && _referenceComparisonPhase
                == ReferenceComparisonPhase.Score)
        {
            _referenceComparisonActive = false;
            _referenceComparisonPhase =
                ReferenceComparisonPhase.None;
            _referenceComparisonRequest = null;
        }

        HandleEditorPlaybackStopped();
    }

    private void ReferenceAudioPlayer_PlaybackStopped(object? sender, EventArgs e)
    {
        if (_suppressPlaybackStoppedNotification)
        {
            return;
        }

        _referencePlaybackActive = false;
        _referencePlaybackFollowsScoreCursor = false;
        if (_referenceComparisonActive
            && _referenceComparisonPhase
                == ReferenceComparisonPhase.Reference)
        {
            Dispatcher.BeginInvoke(
                StartReferenceComparisonScorePhase);
            return;
        }

        HandleEditorPlaybackStopped();
    }

    private void StartReferenceComparison(
        ScoreEditorPreviewRequestedEventArgs request)
    {
        if (!_referenceAudioProject.Enabled
            || !_referenceAudioPlayer.IsLoaded)
        {
            throw new InvalidOperationException(
                "A/B比較には元音源が必要です。");
        }

        _referenceAudioSynchronizer =
            new ReferenceAudioSynchronizer(
                request.Score,
                _referenceAudioProject.SyncPoints);
        _referenceComparisonActive = true;
        _referenceComparisonPhase =
            ReferenceComparisonPhase.Reference;
        _referenceComparisonRequest = request;
        _referencePlaybackActive = true;
        _referencePlaybackFollowsScoreCursor = true;
        _referenceAudioPlayer.Play(
            request.Score,
            _referenceAudioSynchronizer,
            request.StartBeat,
            request.EndBeat,
            loop: false,
            includeScore: false);
        StartPreviewPositionRendering();
        EditorStatusText.Text =
            "編集モード / A/B比較: 原音";
    }

    private void StartReferenceComparisonScorePhase()
    {
        if (_disposed
            || !_referenceComparisonActive
            || _referenceComparisonPhase
                != ReferenceComparisonPhase.Reference
            || _referenceComparisonRequest is null)
        {
            return;
        }

        var request =
            _referenceComparisonRequest;
        ClearPendingPreviewPosition();
        _referencePlaybackActive = false;
        _referencePlaybackFollowsScoreCursor = false;
        _referenceComparisonPhase =
            ReferenceComparisonPhase.Score;
        try
        {
            _previewPlayer.Play(
                request.Score,
                request.StartBeat,
                request.EndBeat,
                loop: false);
            StartPreviewPositionRendering();
            EditorStatusText.Text =
                "編集モード / A/B比較: 譜面";
        }
        catch (Exception ex)
        {
            StopPreviewPlayersSilently();
            _editorView.NotifyPreviewStopped();
            MessageBox.Show(
                this,
                ex.Message,
                "A/B比較エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void HandleEditorPlaybackStopped()
    {
        if (_disposed
            || _suppressPlaybackStoppedNotification)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (_previewPlayer.IsPlaying
                || _previewPlayer.IsPaused
                || _referenceAudioPlayer.IsPlaying
                || _referenceAudioPlayer.IsPaused)
            {
                return;
            }

            StopPreviewPositionRendering();
            ClearPendingPreviewPosition();
            _editorView.NotifyPreviewStopped();
            EditorStatusText.Text = "編集モード";
        });
    }

    private bool SaveEditorWorkspace()
    {
        try
        {
            EditorWorkspaceService.Save(
                _editorView.FilePath,
                _editorView.GetCurrentScore(),
                _editorView.GetCurrentDifficulty(),
                _referenceAudioProject,
                _editorView.CaptureWorkspaceViewState());
            _editorView.MarkWorkSaved();
            EditorStatusText.Text =
                $"作業保存済み: {Path.GetFileName(_editorView.FilePath)}";
            return true;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentException)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "作業保存エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private bool SaveEditor(
        MusicScore score,
        SongDifficulty difficulty,
        bool saveAs)
    {
        var sourcePath =
            _editorView.FilePath;
        var destination =
            sourcePath;
        if (saveAs)
        {
            var dialog = new SaveFileDialog
            {
                Title = "MusicXMLを保存",
                Filter = "MusicXML (*.musicxml)|*.musicxml|XML (*.xml)|*.xml",
                AddExtension = true,
                DefaultExt = ".musicxml",
                FileName = Path.GetFileName(destination),
                InitialDirectory = Path.GetDirectoryName(destination)
            };
            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            destination = dialog.FileName;
        }

        try
        {
            EditorMusicXmlSaveService.SaveValidated(destination, score, difficulty);
            EditorReferenceAudioProjectService.Save(
                destination,
                _referenceAudioProject);
            _settings.SongDifficultyOverrides.Remove(Path.GetFullPath(destination));
            _settingsService.Save(_settings);
            TryDeleteEditorWorkspace(
                destination);
            if (!string.Equals(
                    Path.GetFullPath(
                        sourcePath),
                    Path.GetFullPath(
                        destination),
                    StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteEditorWorkspace(
                    sourcePath);
            }

            _editorView.MarkSaved(
                destination,
                difficulty);
            EditorStatusText.Text =
                $"保存済み: {Path.GetFileName(destination)}";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "MusicXML保存エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private bool ConfirmLeaveEditor()
    {
        if (!_editorView.HasUnsavedWorkChanges)
        {
            return true;
        }

        var result =
            MessageBox.Show(
                this,
                "編集途中の変更が作業保存されていません。\n\n"
                + "[はい] 作業用ファイルに保存して終了\n"
                + "[いいえ] 保存せず終了\n"
                + "[キャンセル] 編集を続ける\n\n"
                + "MusicXMLへ確定する場合は、先に上部の「保存」を使用してください。",
                "未保存の作業",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);
        return result switch
        {
            MessageBoxResult.Yes =>
                SaveEditorWorkspace(),
            MessageBoxResult.No =>
                true,
            _ =>
                false
        };
    }

    private static void TryDeleteEditorWorkspace(
        string musicXmlPath)
    {
        try
        {
            EditorWorkspaceService.Delete(
                musicXmlPath);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or ArgumentException)
        {
            // The MusicXML save has already succeeded. A stale workspace
            // sidecar should not turn that successful save into an error.
        }
    }

    private string? SelectDuplicateDestination(string sourcePath)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var sourceDirectory = Path.GetDirectoryName(fullSourcePath) ?? AppContext.BaseDirectory;
        var initialDirectory = sourceDirectory;

        var registeredDirectory = _settings.SongFolders
            .Where(Directory.Exists)
            .FirstOrDefault(folder => IsPathInside(fullSourcePath, folder));
        if (!string.IsNullOrWhiteSpace(registeredDirectory))
        {
            initialDirectory = sourceDirectory;
        }
        else
        {
            initialDirectory = _settings.SongFolders.FirstOrDefault(Directory.Exists)
                ?? sourceDirectory;
        }

        var extension = Path.GetExtension(fullSourcePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".musicxml";
        }

        var dialog = new SaveFileDialog
        {
            Title = "複製先を選択",
            Filter = "MusicXML (*.musicxml)|*.musicxml|XML (*.xml)|*.xml",
            AddExtension = true,
            DefaultExt = extension,
            InitialDirectory = initialDirectory,
            FileName = $"{Path.GetFileNameWithoutExtension(fullSourcePath)}_edit{extension}"
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private enum ReferenceComparisonPhase
    {
        None,
        Reference,
        Score
    }

    private static bool IsPathInside(string filePath, string directoryPath)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(directoryPath), Path.GetFullPath(filePath));
        return !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !string.Equals(relative, "..", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }
}
