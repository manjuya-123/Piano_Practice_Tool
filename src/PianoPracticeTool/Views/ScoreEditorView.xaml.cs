using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PianoPracticeTool.Controls;
using PianoPracticeTool.Core;
using PianoPracticeTool.Core.Editing;
using PianoPracticeTool.Services.Editor;
using PianoPracticeTool.Services.Settings;
using PianoPracticeTool.Services.Songs;

namespace PianoPracticeTool.Views;

public sealed class ScoreEditorSaveRequestedEventArgs : EventArgs
{
    public ScoreEditorSaveRequestedEventArgs(
        MusicScore score,
        SongDifficulty difficulty,
        bool saveAs)
    {
        Score = score ?? throw new ArgumentNullException(nameof(score));
        Difficulty = difficulty;
        SaveAs = saveAs;
    }

    public MusicScore Score { get; }

    public SongDifficulty Difficulty { get; }

    public bool SaveAs { get; }
}

public sealed class ScoreEditorPreviewRequestedEventArgs : EventArgs
{
    public ScoreEditorPreviewRequestedEventArgs(
        MusicScore score,
        double startBeat,
        double endBeat,
        bool loop,
        EditorReferencePlaybackMode playbackMode,
        bool alternateCompare = false,
        double? referenceAudioStartSeconds = null,
        double? referenceAudioEndSeconds = null)
    {
        Score = score ?? throw new ArgumentNullException(nameof(score));
        StartBeat = startBeat;
        EndBeat = endBeat;
        Loop = loop;
        PlaybackMode = playbackMode;
        AlternateCompare = alternateCompare;
        ReferenceAudioStartSeconds = referenceAudioStartSeconds;
        ReferenceAudioEndSeconds = referenceAudioEndSeconds;
    }

    public MusicScore Score { get; }

    public double StartBeat { get; }

    public double EndBeat { get; }

    public bool Loop { get; }

    public EditorReferencePlaybackMode PlaybackMode { get; }

    public bool AlternateCompare { get; }

    public double? ReferenceAudioStartSeconds { get; }

    public double? ReferenceAudioEndSeconds { get; }
}

public sealed class ScoreEditorReferenceAudioProjectChangedEventArgs : EventArgs
{
    public ScoreEditorReferenceAudioProjectChangedEventArgs(
        ReferenceAudioProject project)
    {
        Project = project?.Clone()
            ?? throw new ArgumentNullException(nameof(project));
    }

    public ReferenceAudioProject Project { get; }
}

public sealed class ScoreEditorReferenceAudioSeekRequestedEventArgs : EventArgs
{
    public ScoreEditorReferenceAudioSeekRequestedEventArgs(
        double audioSeconds)
    {
        AudioSeconds = audioSeconds;
    }

    public double AudioSeconds { get; }
}

public sealed class ScoreEditorOctaveShiftChangedEventArgs : EventArgs
{
    public ScoreEditorOctaveShiftChangedEventArgs(int semitones)
    {
        Semitones = semitones;
    }

    public int Semitones { get; }
}

public sealed partial class ScoreEditorView : UserControl
{
    private static readonly GridChoice[] GridChoices =
    {
        new("1/4", 960),
        new("1/8", 480),
        new("1/16", 240),
        new("1/32", 120),
        new("1/4 三連", 640),
        new("1/8 三連", 320),
        new("1/16 三連", 160)
    };

    private static readonly PreviewMeasureChoice[] PreviewMeasureChoices =
    {
        new(0, "0小節"),
        new(1, "1小節"),
        new(2, "2小節"),
        new(4, "4小節")
    };

    private static readonly ReferencePlaybackChoice[] ReferencePlaybackChoices =
    {
        new(EditorReferencePlaybackMode.ScoreOnly, "譜面"),
        new(EditorReferencePlaybackMode.ReferenceOnly, "原音"),
        new(EditorReferencePlaybackMode.Both, "両方")
    };

    private static readonly DifficultyChoice[] DifficultyChoices =
    {
        new(SongDifficulty.Introductory, "入門"),
        new(SongDifficulty.Beginner, "初級"),
        new(SongDifficulty.Intermediate, "中級"),
        new(SongDifficulty.Advanced, "上級"),
        new(SongDifficulty.Unknown, "未分類")
    };

    private readonly HashSet<int> _selectedNoteIds = new();
    private IReadOnlyList<EditableClipboardNote> _clipboard = Array.Empty<EditableClipboardNote>();
    private ScoreEditSession? _session;
    private string _filePath = string.Empty;
    private SongDifficulty _difficulty;
    private SongDifficulty _savedDifficulty;
    private long _gridTicks = 240L;
    private NoteDragState? _noteDrag;
    private SelectionDragState? _selectionDrag;
    private bool _previewPlaying;
    private bool _previewPaused;
    private bool _navigationFocusRestorePending;
    private bool _suppressDifficultyChange;
    private bool _suppressOctaveShiftChange;
    private bool _suppressPreviewRangeControls;
    private bool _suppressTheoryControls;
    private bool _rollPanActive;
    private bool _rollPanPitchEnabled = true;
    private Point _rollPanStartPoint;
    private int _rollPanStartLowestMidi;
    private Hand _insertHand = Hand.Right;
    private MusicScore? _theoryCacheScore;
    private IReadOnlyList<HarmonyTimelineSegment> _theoryHarmonyTimeline =
        Array.Empty<HarmonyTimelineSegment>();
    private IReadOnlyList<EditorScaleGuideRegion> _theoryScaleGuideRegions =
        Array.Empty<EditorScaleGuideRegion>();
    private IReadOnlyList<EditorChordGuideRegion> _theoryChordGuideRegions =
        Array.Empty<EditorChordGuideRegion>();
    private IReadOnlyList<MusicScaleCandidate> _theoryScaleCandidates =
        Array.Empty<MusicScaleCandidate>();
    private readonly Dictionary<long, MusicKeyAnalysis?> _theoryKeyCache = new();
    private int _lastChordSuggestionMeasureNumber = int.MinValue;
    private MusicScore? _theoryPresentationScore;
    private double _theoryPresentationStartBeat = double.NaN;
    private double _theoryPresentationEndBeat = double.NaN;
    private double _previewStartBeat;
    private double _previewEndBeat;
    private long? _harmonyEditTick;
    private int? _measureContextNumber;
    private double _measureContextLaneY;
    private MeasurePropertyPopupKind _pendingMeasurePropertyPopup;
    private ReferenceAudioProject _referenceAudioProject = new();
    private ReferenceAudioWaveform? _referenceAudioWaveform;
    private ReferenceAudioSynchronizer? _referenceAudioSynchronizer;
    private double _referenceAudioCurrentSeconds;
    private bool _suppressReferenceAudioControls;
    private string _savedReferenceAudioProjectSignature = string.Empty;
    private string _savedWorkspaceContentSignature = string.Empty;

    public ScoreEditorView()
    {
        InitializeComponent();
        GridComboBox.ItemsSource = GridChoices;
        GridComboBox.DisplayMemberPath = nameof(GridChoice.Label);
        GridComboBox.SelectedItem = GridChoices[2];
        DifficultyComboBox.ItemsSource = DifficultyChoices;
        DifficultyComboBox.DisplayMemberPath = nameof(DifficultyChoice.Label);
        ScaleTypeComboBox.ItemsSource = MusicTheoryAnalyzer.ScaleDefinitions;
        ScaleTypeComboBox.SelectedItem =
            MusicTheoryAnalyzer.GetScaleDefinition("major");
        RefreshScaleTonicChoices("major", 0);
        InspectorHandComboBox.ItemsSource = InspectorHandChoices;
        InspectorHandComboBox.DisplayMemberPath = nameof(HandChoice.Label);
        PreviewBeforeMeasureComboBox.ItemsSource = PreviewMeasureChoices;
        PreviewBeforeMeasureComboBox.DisplayMemberPath = nameof(PreviewMeasureChoice.Label);
        PreviewAfterMeasureComboBox.ItemsSource = PreviewMeasureChoices;
        PreviewAfterMeasureComboBox.DisplayMemberPath = nameof(PreviewMeasureChoice.Label);
        PreviewBeforeMeasureComboBox.SelectedItem = PreviewMeasureChoices[1];
        PreviewAfterMeasureComboBox.SelectedItem = PreviewMeasureChoices[1];
        ReferencePlaybackModeComboBox.ItemsSource =
            ReferencePlaybackChoices;
        ReferencePlaybackModeComboBox.SelectedItem =
            ReferencePlaybackChoices[2];
        SetInsertHand(Hand.Right);
        RefreshNoteInspector();
    }

    public event EventHandler? BackRequested;

    public event EventHandler<ScoreEditorSaveRequestedEventArgs>? SaveRequested;

    public event EventHandler? WorkspaceSaveRequested;

    public event EventHandler<ScoreEditorPreviewRequestedEventArgs>? PreviewRequested;

    public event EventHandler? PausePreviewRequested;

    public event EventHandler? ResumePreviewRequested;

    public event EventHandler? StopPreviewRequested;

    public event EventHandler<ScoreEditorOctaveShiftChangedEventArgs>? OctaveShiftChanged;

    public event EventHandler? ReferenceAudioFileRequested;

    public event EventHandler<ScoreEditorReferenceAudioProjectChangedEventArgs>? ReferenceAudioProjectChanged;

    public event EventHandler<ScoreEditorReferenceAudioSeekRequestedEventArgs>? ReferenceAudioSeekRequested;

    public void SetTimelineDragMode(EditorTimelineDragMode mode)
        => MeasureLane.DragMode = mode;

    public void SetRollPanPitchEnabled(bool enabled)
        => _rollPanPitchEnabled = enabled;

    public bool IsDirty =>
        _session?.IsDirty == true
        || _difficulty != _savedDifficulty
        || !string.Equals(
            BuildReferenceAudioProjectSignature(
                _referenceAudioProject),
            _savedReferenceAudioProjectSignature,
            StringComparison.Ordinal);

    public bool HasUnsavedWorkChanges =>
        _session is not null
        && !string.Equals(
            BuildWorkspaceContentSignature(),
            _savedWorkspaceContentSignature,
            StringComparison.Ordinal);

    public string FilePath => _filePath;

    public void SetDocument(
        string filePath,
        MusicScore score,
        SongDifficulty difficulty,
        MusicScore? savedScore = null,
        SongDifficulty? savedDifficulty = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new System.ArgumentException(
                "Value must not be null, empty, or whitespace.",
                nameof(filePath));
        }

        ArgumentNullException.ThrowIfNull(score);

        _session =
            new ScoreEditSession(
                EditableMusicScore.FromMusicScore(
                    score),
                savedScore is null
                    ? null
                    : EditableMusicScore.FromMusicScore(
                        savedScore));
        _filePath = System.IO.Path.GetFullPath(filePath);
        _difficulty = difficulty;
        _savedDifficulty =
            savedDifficulty
            ?? difficulty;
        _selectedNoteIds.Clear();
        _clipboard = Array.Empty<EditableClipboardNote>();
        _previewPlaying = false;
        _previewPaused = false;
        _noteDrag = null;
        _selectionDrag = null;
        _rollPanActive = false;
        _insertHand = Hand.Right;
        InsertRightHandButton.IsChecked = true;
        InsertLeftHandButton.IsChecked = false;
        ScaleGuideToggleButton.IsChecked = false;
        EditorRoll.ScaleGuideRegions = Array.Empty<EditorScaleGuideRegion>();
        EditorRoll.ChordGuideRegions = Array.Empty<EditorChordGuideRegion>();
        ScaleCandidateComboBox.SelectedItem = null;
        ChordComboBox.Text = string.Empty;
        ResetTheoryCache();
        _referenceAudioProject = new ReferenceAudioProject();
        _referenceAudioWaveform = null;
        _referenceAudioSynchronizer = null;
        _referenceAudioCurrentSeconds = 0d;
        _savedReferenceAudioProjectSignature =
            BuildReferenceAudioProjectSignature(
                _referenceAudioProject);
        RefreshReferenceAudioPresentation();

        _suppressDifficultyChange = true;
        DifficultyComboBox.SelectedItem = DifficultyChoices.First(choice => choice.Difficulty == difficulty);
        _suppressDifficultyChange = false;

        EditorRoll.CursorBeat = 0d;
        EditorRoll.PreviewBeat = null;
        EditorRoll.SelectionRectangle = null;
        _previewStartBeat = 0d;
        _previewEndBeat = score.LengthBeats;
        _suppressPreviewRangeControls = true;
        try
        {
            RangePreviewCheckBox.IsChecked = false;
            LoopCheckBox.IsChecked = false;
            LoopCheckBox.IsEnabled = false;
        }
        finally
        {
            _suppressPreviewRangeControls = false;
        }

        UpdatePreviewRangePresentation();
        ApplySessionToView("編集を開始しました。ダブルクリックでノートを挿入できます。");
        EditorRoll.FitPitchViewportToScore();
        MarkWorkSaved();
    }

    public void ActivateEditorInput()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (!IsVisible || !EditorRoll.IsVisible)
                {
                    return;
                }

                Keyboard.Focus(EditorRoll);
                RefreshEditorRollCursor();
            }));
    }

    private void RestoreEditorInputFocusAfterNavigation()
    {
        if (_navigationFocusRestorePending)
        {
            return;
        }

        _navigationFocusRestorePending =
            true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                _navigationFocusRestorePending =
                    false;
                if (!IsVisible
                    || !EditorRoll.IsVisible)
                {
                    return;
                }

                Focus();
                EditorRoll.Focus();
                Keyboard.Focus(
                    EditorRoll);
                RefreshEditorRollCursor();
            }));
    }

    public void SetActiveMidiNotes(IEnumerable<int> midiNotes)
    {
        ArgumentNullException.ThrowIfNull(midiNotes);
        EditorRoll.ActiveMidiNotes = midiNotes.ToArray();
    }

    public void SetOctaveShiftOptions(
        IReadOnlyList<int> availableShiftsSemitones,
        int selectedShiftSemitones,
        int recommendedShiftSemitones)
    {
        ArgumentNullException.ThrowIfNull(availableShiftsSemitones);

        var choices = availableShiftsSemitones
            .Distinct()
            .OrderBy(shift => shift)
            .Select(shift => new OctaveShiftChoice(
                shift,
                shift == recommendedShiftSemitones
                    ? $"{FormatOctaveShift(shift)} 推奨"
                    : FormatOctaveShift(shift)))
            .ToArray();

        _suppressOctaveShiftChange = true;
        try
        {
            OctaveShiftComboBox.ItemsSource = choices;
            OctaveShiftComboBox.DisplayMemberPath = nameof(OctaveShiftChoice.Label);
            OctaveShiftComboBox.SelectedItem = choices.FirstOrDefault(
                choice => choice.Semitones == selectedShiftSemitones)
                ?? choices.FirstOrDefault();
            OctaveShiftComboBox.IsEnabled = choices.Length > 1;
            OctaveShiftStatusText.Text = choices.Length > 1
                ? "MIDIキーボード本体のシフト量に合わせて入力します"
                : "MIDIキーボードのシフトなし";
        }
        finally
        {
            _suppressOctaveShiftChange = false;
        }
    }

    public void SetReferenceAudioProject(
        ReferenceAudioProject project,
        ReferenceAudioWaveform? waveform,
        bool markSaved = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        _referenceAudioProject = project.Clone();
        _referenceAudioWaveform = waveform;
        if (markSaved)
        {
            _savedReferenceAudioProjectSignature =
                BuildReferenceAudioProjectSignature(
                    _referenceAudioProject);
        }
        _referenceAudioCurrentSeconds = Math.Clamp(
            _referenceAudioCurrentSeconds,
            0d,
            waveform?.DurationSeconds ?? 0d);

        if (_session is not null)
        {
            _session.Score.ReferenceAudioSyncPoints.Clear();
            _session.Score.ReferenceAudioSyncPoints.AddRange(
                _referenceAudioProject.SyncPoints);
            RefreshReferenceAudioSynchronizer(
                _session.Score.ToMusicScore());
        }

        RefreshReferenceAudioPresentation();
        UpdateDirtyState();
    }

    public void SetReferenceAudioEnabled(bool enabled)
    {
        _referenceAudioProject.Enabled = enabled;
        RefreshReferenceAudioPresentation();
    }

    public void UpdateReferenceAudioPlaybackPosition(
        double scoreBeat,
        double audioSeconds,
        bool followScoreCursor)
    {
        UpdateReferenceAudioPositionCore(
            audioSeconds);
        if (followScoreCursor)
        {
            UpdatePreviewPosition(scoreBeat);
        }
        else
        {
            StatusText.Text =
                $"原音再生中  {FormatAudioTime(audioSeconds)}";
        }
    }

    public void UpdateReferenceAudioSeekPosition(
        double scoreBeat,
        double audioSeconds)
    {
        UpdateReferenceAudioPositionCore(
            audioSeconds);
        StatusText.Text =
            $"原音位置 {FormatAudioTime(audioSeconds)} / 対応候補 {scoreBeat:0.###} beat";
    }

    private void UpdateReferenceAudioPositionCore(
        double audioSeconds)
    {
        _referenceAudioCurrentSeconds = Math.Clamp(
            audioSeconds,
            0d,
            _referenceAudioWaveform?.DurationSeconds
                ?? Math.Max(0d, audioSeconds));
        ReferenceAudioLane.AudioPositionSeconds =
            _referenceAudioCurrentSeconds;

        _suppressReferenceAudioControls = true;
        try
        {
            ReferenceAudioPositionSlider.Value =
                _referenceAudioCurrentSeconds;
        }
        finally
        {
            _suppressReferenceAudioControls = false;
        }

        ReferenceAudioTimeText.Text =
            FormatAudioTime(
                _referenceAudioCurrentSeconds);
    }

    public EditorWorkspaceViewState CaptureWorkspaceViewState()
    {
        var pitchRange =
            EditorRoll.VisiblePitchRange;
        return new EditorWorkspaceViewState
        {
            CursorBeat =
                EditorRoll.CursorBeat,
            GridTicks =
                _gridTicks,
            InsertHand =
                _insertHand,
            SelectedNoteIds =
                _selectedNoteIds
                    .OrderBy(id =>
                        id)
                    .ToList(),
            VisibleBeats =
                EditorRoll.VisibleBeats,
            VisiblePitchLowestMidi =
                pitchRange.LowestMidi,
            VisiblePitchHighestMidi =
                pitchRange.HighestMidi,
            ScaleGuideEnabled =
                ScaleGuideToggleButton.IsChecked
                == true,
            ReferenceAudioCurrentSeconds =
                _referenceAudioCurrentSeconds
        };
    }

    public void RestoreWorkspaceViewState(
        EditorWorkspaceViewState state)
    {
        ArgumentNullException.ThrowIfNull(
            state);

        var gridChoice =
            GridChoices.FirstOrDefault(choice =>
                choice.Ticks
                    == state.GridTicks);
        if (gridChoice is not null)
        {
            GridComboBox.SelectedItem =
                gridChoice;
        }

        SetInsertHand(
            state.InsertHand);
        EditorRoll.VisibleBeats =
            state.VisibleBeats;
        EditorRoll.RestorePitchViewport(
            state.VisiblePitchLowestMidi,
            state.VisiblePitchHighestMidi);
        EditorRoll.CursorBeat =
            state.CursorBeat;
        ScaleGuideToggleButton.IsChecked =
            state.ScaleGuideEnabled;

        var existingIds =
            RequireSession()
                .Score
                .Notes
                .Select(note =>
                    note.Id)
                .ToHashSet();
        SetSelection(
            state.SelectedNoteIds
                .Where(existingIds.Contains));
        UpdateReferenceAudioSeekPosition(
            state.CursorBeat,
            state.ReferenceAudioCurrentSeconds);
        StatusText.Text =
            "作業データから編集状態を復元しました。";
    }

    public void SetSavedReferenceAudioProjectBaseline(
        ReferenceAudioProject project)
    {
        ArgumentNullException.ThrowIfNull(
            project);
        _savedReferenceAudioProjectSignature =
            BuildReferenceAudioProjectSignature(
                project);
        UpdateDirtyState();
    }

    public void MarkWorkSaved()
    {
        if (_session is null)
        {
            _savedWorkspaceContentSignature =
                string.Empty;
            return;
        }

        _savedWorkspaceContentSignature =
            BuildWorkspaceContentSignature();
        UpdateDirtyState();
    }

    public MusicScore GetCurrentScore()
        => RequireSession().Score.ToMusicScore();

    public SongDifficulty GetCurrentDifficulty()
        => _difficulty;

    public void MarkSaved(string filePath, SongDifficulty difficulty)
    {
        var session = RequireSession();
        _filePath = System.IO.Path.GetFullPath(filePath);
        _difficulty = difficulty;
        _savedDifficulty = difficulty;
        _savedReferenceAudioProjectSignature =
            BuildReferenceAudioProjectSignature(
                _referenceAudioProject);
        session.MarkSaved();
        MarkWorkSaved();
        ApplySessionToView("保存しました。");
    }

    public void UpdatePreviewPosition(double beat)
    {
        EditorRoll.PreviewBeat = beat;
        StatusText.Text = $"試聴中  {beat:0.###} beat";
    }

    public void NotifyPreviewPaused()
    {
        _previewPlaying = false;
        _previewPaused = true;
        PreviewButton.Content = "▶ 再開  Space";
        StatusText.Text = "試聴を一時停止しました。";
    }

    public void NotifyPreviewResumed()
    {
        _previewPlaying = true;
        _previewPaused = false;
        PreviewButton.Content = "⏸ 一時停止  Space";
        StatusText.Text = "試聴を再開しました。";
    }

    public void NotifyPreviewStopped()
    {
        _previewPlaying = false;
        _previewPaused = false;
        PreviewButton.Content = "▶ 試聴  Space";
        EditorRoll.PreviewBeat = null;
        StatusText.Text = "試聴を停止しました。";
    }

    public void HandlePreviewShortcut()
        => TogglePreviewPlayback();

    private void BackButton_Click(object sender, RoutedEventArgs e)
        => BackRequested?.Invoke(this, EventArgs.Empty);

    private void SaveButton_Click(object sender, RoutedEventArgs e)
        => RequestSave(saveAs: false);

    private void SaveAsButton_Click(object sender, RoutedEventArgs e)
        => RequestSave(saveAs: true);

    private void WorkspaceSaveButton_Click(
        object sender,
        RoutedEventArgs e)
        => WorkspaceSaveRequested?.Invoke(
            this,
            EventArgs.Empty);

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (RequireSession().Undo())
        {
            RemoveMissingSelections();
            ApplySessionToView("Undoしました。");
        }
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        if (RequireSession().Redo())
        {
            RemoveMissingSelections();
            ApplySessionToView("Redoしました。");
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
        => CopySelected();

    private void CutButton_Click(object sender, RoutedEventArgs e)
    {
        CopySelected();
        DeleteSelected();
    }

    private void PasteButton_Click(object sender, RoutedEventArgs e)
        => PasteAtCursor();

    private void DuplicateButton_Click(object sender, RoutedEventArgs e)
        => DuplicateSelected();

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
        => DeleteSelected();

    private void SplitTwoButton_Click(object sender, RoutedEventArgs e)
        => SplitSelected(2);

    private void SplitThreeButton_Click(object sender, RoutedEventArgs e)
        => SplitSelected(3);

    private void RightHandButton_Click(object sender, RoutedEventArgs e)
        => SetSelectedHand(Hand.Right);

    private void LeftHandButton_Click(object sender, RoutedEventArgs e)
        => SetSelectedHand(Hand.Left);

    private void GridComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GridComboBox.SelectedItem is not GridChoice choice)
        {
            return;
        }

        _gridTicks = choice.Ticks;
        if (MeasureLane is not null)
        {
            MeasureLane.GridTicks = _gridTicks;
        }
    }

    private void OctaveShiftComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressOctaveShiftChange
            || OctaveShiftComboBox.SelectedItem is not OctaveShiftChoice choice)
        {
            return;
        }

        OctaveShiftChanged?.Invoke(
            this,
            new ScoreEditorOctaveShiftChangedEventArgs(choice.Semitones));
    }

    private void DifficultyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDifficultyChange || DifficultyComboBox.SelectedItem is not DifficultyChoice choice)
        {
            return;
        }

        _difficulty = choice.Difficulty;
        UpdateDirtyState();
    }

    private void ReferenceAudioToggleButton_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_suppressReferenceAudioControls)
        {
            return;
        }

        var enabled =
            ReferenceAudioToggleButton.IsChecked == true;
        if (!enabled
            && (_previewPlaying || _previewPaused))
        {
            RequestStopPreview(
                restoreCursor: false);
        }

        _referenceAudioProject.Enabled = enabled;
        RefreshReferenceAudioPresentation();
        RaiseReferenceAudioProjectChanged();

        if (enabled
            && string.IsNullOrWhiteSpace(
                _referenceAudioProject.AudioPath))
        {
            ReferenceAudioFileRequested?.Invoke(
                this,
                EventArgs.Empty);
        }
    }

    private void ReferenceAudioSelectButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_previewPlaying || _previewPaused)
        {
            RequestStopPreview(
                restoreCursor: false);
        }

        ReferenceAudioFileRequested?.Invoke(
            this,
            EventArgs.Empty);
    }

    private void ReferencePlaybackModeComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_suppressReferenceAudioControls
            || ReferencePlaybackModeComboBox.SelectedItem
                is not ReferencePlaybackChoice choice
            || _referenceAudioProject.PlaybackMode
                == choice.Mode)
        {
            return;
        }

        if (_previewPlaying
            || _previewPaused)
        {
            RequestStopPreview(
                restoreCursor: false);
        }

        _referenceAudioProject.PlaybackMode =
            choice.Mode;
        RaiseReferenceAudioProjectChanged();
    }

    private void ReferenceAudioVolumeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressReferenceAudioControls)
        {
            return;
        }

        _referenceAudioProject.ReferenceVolumePercent =
            (int)Math.Round(
                Math.Clamp(e.NewValue, 0d, 100d),
                MidpointRounding.AwayFromZero);
        RaiseReferenceAudioProjectChanged();
    }

    private void ReferenceScoreVolumeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressReferenceAudioControls)
        {
            return;
        }

        _referenceAudioProject.ScoreVolumePercent =
            (int)Math.Round(
                Math.Clamp(e.NewValue, 0d, 100d),
                MidpointRounding.AwayFromZero);
        RaiseReferenceAudioProjectChanged();
    }

    private void ReferenceAudioPanSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressReferenceAudioControls)
        {
            return;
        }

        _referenceAudioProject.ReferencePanPercent =
            (int)Math.Round(
                Math.Clamp(
                    e.NewValue,
                    -100d,
                    100d),
                MidpointRounding.AwayFromZero);
        RaiseReferenceAudioProjectChanged();
    }

    private void ReferenceScorePanSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressReferenceAudioControls)
        {
            return;
        }

        _referenceAudioProject.ScorePanPercent =
            (int)Math.Round(
                Math.Clamp(
                    e.NewValue,
                    -100d,
                    100d),
                MidpointRounding.AwayFromZero);
        RaiseReferenceAudioProjectChanged();
    }

    private void ReferenceAudioCompareButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!EnsureReferenceAudioAvailable())
        {
            return;
        }

        var score =
            RequireSession().Score.ToMusicScore();
        double startBeat;
        double endBeat;

        if (RangePreviewCheckBox.IsChecked == true)
        {
            startBeat = _previewStartBeat;
            endBeat = _previewEndBeat;
        }
        else
        {
            var selected = score.Notes
                .Where(note =>
                    _selectedNoteIds.Contains(
                        note.Id))
                .ToArray();
            if (selected.Length > 0)
            {
                var firstMeasure =
                    score.GetMeasureAt(
                        selected.Min(note =>
                            note.StartBeat));
                var lastBeat = selected.Max(note =>
                    note.EndBeat);
                var lastMeasure =
                    score.GetMeasureAt(
                        Math.Max(
                            0d,
                            lastBeat
                            - ScoreTiming.EventBeatTolerance));
                startBeat =
                    firstMeasure?.StartBeat
                    ?? selected.Min(note =>
                        note.StartBeat);
                endBeat =
                    lastMeasure?.EndBeat
                    ?? lastBeat;
            }
            else
            {
                var measure =
                    score.GetMeasureAt(
                        EditorRoll.CursorBeat);
                if (measure is null)
                {
                    StatusText.Text =
                        "比較する小節を確認できません。";
                    return;
                }

                startBeat =
                    measure.StartBeat;
                endBeat =
                    measure.EndBeat;
            }
        }

        if (endBeat
            <= startBeat
            + ScoreTiming.EventBeatTolerance)
        {
            StatusText.Text =
                "A/B比較できる範囲がありません。";
            return;
        }

        _previewPlaying = true;
        _previewPaused = false;
        PreviewButton.Content =
            "⏸ 一時停止  Space";
        PreviewRequested?.Invoke(
            this,
            new ScoreEditorPreviewRequestedEventArgs(
                score,
                startBeat,
                endBeat,
                loop: false,
                playbackMode:
                    EditorReferencePlaybackMode.ReferenceOnly,
                alternateCompare: true));
    }

    private void ReferenceAudioPositionSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressReferenceAudioControls
            || !_referenceAudioProject.Enabled
            || _referenceAudioWaveform is null)
        {
            return;
        }

        _referenceAudioCurrentSeconds = Math.Clamp(
            e.NewValue,
            0d,
            _referenceAudioWaveform.DurationSeconds);
        ReferenceAudioTimeText.Text =
            FormatAudioTime(
                _referenceAudioCurrentSeconds);
        ReferenceAudioSeekRequested?.Invoke(
            this,
            new ScoreEditorReferenceAudioSeekRequestedEventArgs(
                _referenceAudioCurrentSeconds));
    }

    private void ReferenceAudioLane_AudioSegmentAdjustRequested(
        object? sender,
        EditorReferenceAudioSegmentAdjustRequestedEventArgs e)
    {
        if (!EnsureReferenceAudioAvailable())
        {
            return;
        }

        var originalPoints =
            _referenceAudioProject.SyncPoints.ToList();
        var score =
            RequireSession().Score.ToMusicScore();
        IReadOnlyList<ReferenceAudioSyncPoint> shiftedPoints;
        try
        {
            shiftedPoints =
                ReferenceAudioSynchronizer
                    .AdjustAudioSegment(
                        score,
                        originalPoints,
                        _referenceAudioWaveform!.DurationSeconds,
                        e.ScoreBeat,
                        e.DeltaSeconds);
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
            return;
        }

        if (ReferenceSyncPointsEqual(
                originalPoints,
                shiftedPoints))
        {
            StatusText.Text =
                "この区間はこれ以上その方向へ調整できません。";
            return;
        }

        _referenceAudioProject.SyncPoints =
            shiftedPoints.ToList();
        var adjustedSynchronizer =
            new ReferenceAudioSynchronizer(
                score,
                shiftedPoints);
        var gapCount =
            adjustedSynchronizer.AudioOnlyGaps.Count;
        if (!TryNormalizeReferenceSyncPoints(
                gapCount > 0
                    ? $"原音区間を調整しました。カット {gapCount}区間"
                    : "原音区間を調整しました。"))
        {
            _referenceAudioProject.SyncPoints =
                originalPoints;
            RefreshReferenceAudioSynchronizer(
                RequireSession().Score.ToMusicScore());
            RefreshReferenceAudioPresentation();
            return;
        }

        ReferenceAudioLane.SelectedSyncPoint =
            null;
        RefreshReferenceAudioPresentation();
    }

    private void ReferenceAudioLane_SeekRequested(
        object? sender,
        EditorReferenceAudioSeekRequestedEventArgs e)
    {
        _referenceAudioCurrentSeconds =
            e.AudioSeconds;
        ReferenceAudioSeekRequested?.Invoke(
            this,
            new ScoreEditorReferenceAudioSeekRequestedEventArgs(
                e.AudioSeconds));
    }

    private void ReferenceAudioLane_PlayRequested(
        object? sender,
        EditorReferenceAudioPlayRequestedEventArgs e)
    {
        if (!EnsureReferenceAudioAvailable()
            || _referenceAudioWaveform is null)
        {
            return;
        }

        if (_previewPlaying || _previewPaused)
        {
            RequestStopPreview(
                restoreCursor: false);
        }

        _referenceAudioCurrentSeconds =
            e.AudioSeconds;
        UpdateReferenceAudioPositionCore(
            e.AudioSeconds);

        var score =
            RequireSession().Score.ToMusicScore();
        var endSeconds =
            _referenceAudioWaveform.DurationSeconds;
        if (endSeconds
            <= e.AudioSeconds + 0.001d)
        {
            StatusText.Text =
                "この原音位置より後に再生できる範囲がありません。";
            return;
        }

        _previewPlaying = true;
        _previewPaused = false;
        PreviewButton.Content =
            "⏸ 一時停止  Space";
        PreviewRequested?.Invoke(
            this,
            new ScoreEditorPreviewRequestedEventArgs(
                score,
                e.ScoreBeat,
                score.LengthBeats,
                loop: false,
                playbackMode:
                    EditorReferencePlaybackMode.ReferenceOnly,
                referenceAudioStartSeconds:
                    e.AudioSeconds,
                referenceAudioEndSeconds:
                    endSeconds));
    }

    private void ReferenceAudioLane_SyncPointAddRequested(
        object? sender,
        EditorReferenceAudioSyncPointAddRequestedEventArgs e)
        => AddReferenceSyncPoint(
            e.ScoreBeat,
            e.AudioSeconds);

    private void ReferenceAudioLane_SyncPointSelected(
        object? sender,
        EditorReferenceAudioSyncPointEventArgs e)
    {
        ReferenceAudioStatusText.Text =
            FormatReferenceSyncPointStatus(
                e.Point);
    }

    private void ReferenceAudioLane_SyncPointMoveRequested(
        object? sender,
        EditorReferenceAudioSyncPointMoveRequestedEventArgs e)
        => MoveReferenceSyncPoint(
            e.Point,
            e.NewScoreBeat);

    private void ReferenceAudioLane_SyncPointDeleteRequested(
        object? sender,
        EditorReferenceAudioSyncPointEventArgs e)
        => DeleteReferenceSyncPoint(
            e.Point);

    private void ReferenceAudioLane_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        var selected =
            ReferenceAudioLane.SelectedSyncPoint;
        var modifiers =
            Keyboard.Modifiers;
        if (selected is not null
            && modifiers
                == ModifierKeys.Shift)
        {
            NudgeReferenceSyncPoint(
                selected,
                e.Delta > 0
                    ? 0.05d
                    : -0.05d);
            e.Handled = true;
            return;
        }

        EditorRoll_PreviewMouseWheel(
            sender,
            e);
    }

    private void AddReferenceSyncPoint(
        double scoreBeat,
        double audioSeconds)
    {
        if (!EnsureReferenceAudioAvailable())
        {
            return;
        }

        var point = new ReferenceAudioSyncPoint(
            Math.Clamp(
                scoreBeat,
                0d,
                RequireSession().Score.LengthTicks
                / (double)EditableMusicScore.TicksPerQuarter),
            Math.Max(
                0d,
                audioSeconds));
        if (_referenceAudioProject.SyncPoints.Any(existing =>
                Math.Abs(
                    existing.ScoreBeat
                    - point.ScoreBeat)
                    <= ScoreTiming.EventBeatTolerance
                && Math.Abs(
                    existing.AudioSeconds
                    - point.AudioSeconds)
                    <= 0.001d))
        {
            StatusText.Text =
                "同じ同期ポイントがすでにあります。";
            return;
        }

        _referenceAudioProject.SyncPoints.Add(
            point);
        if (!TryNormalizeReferenceSyncPoints(
                $"同期ポイントを {point.ScoreBeat:0.###} beat に追加しました。"))
        {
            _referenceAudioProject.SyncPoints.Remove(
                point);
            return;
        }

        ReferenceAudioLane.SelectedSyncPoint =
            point;
        ReferenceAudioStatusText.Text =
            FormatReferenceSyncPointStatus(
                point);
    }

    private void DeleteReferenceSyncPoint(
        ReferenceAudioSyncPoint point)
    {
        if (IsFixedReferenceBoundaryPoint(
                point))
        {
            StatusText.Text =
                "原音の開始端・終了端は削除できません。Shift+ドラッグで対応位置を調整してください。";
            return;
        }

        var index =
            FindReferenceSyncPointIndex(
                point);
        if (index < 0)
        {
            StatusText.Text =
                "同期ポイントを確認できません。";
            return;
        }

        _referenceAudioProject.SyncPoints.RemoveAt(
            index);
        if (TryNormalizeReferenceSyncPoints(
                "同期ポイントを削除しました。"))
        {
            ReferenceAudioLane.SelectedSyncPoint =
                null;
        }
    }

    private void MoveReferenceSyncPoint(
        ReferenceAudioSyncPoint point,
        double newScoreBeat)
    {
        if (IsFixedReferenceBoundaryPoint(
                point))
        {
            StatusText.Text =
                "原音の開始端・終了端は移動できません。";
            return;
        }

        var index =
            FindReferenceSyncPointIndex(
                point);
        if (index < 0)
        {
            StatusText.Text =
                "同期ポイントを確認できません。";
            return;
        }

        var original =
            _referenceAudioProject.SyncPoints[index];
        var moved =
            original with
            {
                ScoreBeat = Math.Clamp(
                    newScoreBeat,
                    0d,
                    EditableMusicScore.TickToBeat(
                        RequireSession().Score.LengthTicks))
            };
        _referenceAudioProject.SyncPoints[index] =
            moved;

        if (!TryNormalizeReferenceSyncPoints(
                $"同期位置を {moved.ScoreBeat:0.###} beat に移動しました。"))
        {
            _referenceAudioProject.SyncPoints[index] =
                original;
            RefreshReferenceAudioSynchronizer(
                RequireSession().Score.ToMusicScore());
            return;
        }

        ReferenceAudioLane.SelectedSyncPoint =
            moved;
        ReferenceAudioStatusText.Text =
            FormatReferenceSyncPointStatus(
                moved);
    }

    private void NudgeReferenceSyncPoint(
        ReferenceAudioSyncPoint point,
        double deltaSeconds)
    {
        if (IsFixedReferenceBoundaryPoint(
                point))
        {
            StatusText.Text =
                "原音の開始端・終了端は微調整できません。";
            return;
        }

        var index =
            FindReferenceSyncPointIndex(
                point);
        if (index < 0)
        {
            StatusText.Text =
                "同期ポイントを確認できません。";
            return;
        }

        var original =
            _referenceAudioProject.SyncPoints[index];
        var moved =
            original with
            {
                AudioSeconds = Math.Max(
                    0d,
                    original.AudioSeconds
                    + deltaSeconds)
            };
        _referenceAudioProject.SyncPoints[index] =
            moved;

        if (!TryNormalizeReferenceSyncPoints(
                $"原音側を {deltaSeconds * 1000d:+0;-0} ms 調整しました。"))
        {
            _referenceAudioProject.SyncPoints[index] =
                original;
            RefreshReferenceAudioSynchronizer(
                RequireSession().Score.ToMusicScore());
            return;
        }

        _referenceAudioCurrentSeconds =
            moved.AudioSeconds;
        UpdateReferenceAudioPositionCore(
            moved.AudioSeconds);
        ReferenceAudioSeekRequested?.Invoke(
            this,
            new ScoreEditorReferenceAudioSeekRequestedEventArgs(
                moved.AudioSeconds));
        ReferenceAudioLane.SelectedSyncPoint =
            moved;
        ReferenceAudioStatusText.Text =
            FormatReferenceSyncPointStatus(
                moved);
    }

    private bool TryNormalizeReferenceSyncPoints(
        string successMessage)
    {
        if (_previewPlaying || _previewPaused)
        {
            RequestStopPreview(
                restoreCursor: false);
        }

        try
        {
            _referenceAudioProject.SyncPoints =
                ReferenceAudioSynchronizer.Normalize(
                        _referenceAudioProject.SyncPoints)
                    .ToList();
            if (_session is not null)
            {
                _session.Score.ReferenceAudioSyncPoints.Clear();
                _session.Score.ReferenceAudioSyncPoints.AddRange(
                    _referenceAudioProject.SyncPoints);
                RefreshReferenceAudioSynchronizer(
                    _session.Score.ToMusicScore());
            }

            RefreshReferenceAudioPresentation();
            RaiseReferenceAudioProjectChanged();
            StatusText.Text = successMessage;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
            return false;
        }
    }

    private bool IsFixedReferenceBoundaryPoint(
        ReferenceAudioSyncPoint point)
    {
        if (_referenceAudioWaveform is null
            || _session is null)
        {
            return false;
        }

        var scoreLength =
            EditableMusicScore.TickToBeat(
                _session.Score.LengthTicks);
        var isSourceStart =
            Math.Abs(
                point.ScoreBeat)
                <= ScoreTiming.EventBeatTolerance
            && point.AudioSeconds
                <= ScoreTiming.EventBeatTolerance;
        var isSourceEnd =
            Math.Abs(
                point.ScoreBeat
                - scoreLength)
                <= ScoreTiming.EventBeatTolerance
            && Math.Abs(
                point.AudioSeconds
                - _referenceAudioWaveform.DurationSeconds)
                <= 0.001d;
        return isSourceStart
               || isSourceEnd;
    }

    private int FindReferenceSyncPointIndex(
        ReferenceAudioSyncPoint point)
        => _referenceAudioProject.SyncPoints.FindIndex(
            item =>
                Math.Abs(
                    item.ScoreBeat
                    - point.ScoreBeat)
                    <= ScoreTiming.EventBeatTolerance
                && Math.Abs(
                    item.AudioSeconds
                    - point.AudioSeconds)
                    <= 0.001d);

    private static string FormatReferenceSyncPointStatus(
        ReferenceAudioSyncPoint point)
        => $"同期 {point.ScoreBeat:0.###} beat ↔ {FormatAudioTime(point.AudioSeconds)}"
           + " / ドラッグ=譜面位置 / Shift+ホイール=原音±50ms / 右クリック・Delete=削除";

    private bool EnsureReferenceAudioAvailable()
    {
        if (!_referenceAudioProject.Enabled
            || _referenceAudioWaveform is null
            || string.IsNullOrWhiteSpace(
                _referenceAudioProject.AudioPath))
        {
            StatusText.Text =
                "先に耳コピ用の元音源を選択してください。";
            return false;
        }

        return true;
    }

    private void RaiseReferenceAudioProjectChanged()
    {
        UpdateDirtyState();
        ReferenceAudioProjectChanged?.Invoke(
            this,
            new ScoreEditorReferenceAudioProjectChangedEventArgs(
                _referenceAudioProject));
    }

    private void RefreshReferenceAudioSynchronizer(
        MusicScore score)
    {
        var projectChanged = false;
        if (_session is not null)
        {
            var sessionPoints =
                _session.Score.ReferenceAudioSyncPoints;
            projectChanged =
                !ReferenceSyncPointsEqual(
                    _referenceAudioProject.SyncPoints,
                    sessionPoints);
            if (projectChanged)
            {
                _referenceAudioProject.SyncPoints =
                    sessionPoints.ToList();
            }
        }

        _referenceAudioSynchronizer =
            new ReferenceAudioSynchronizer(
                score,
                _referenceAudioProject.SyncPoints);
        ReferenceAudioLane.Synchronizer =
            _referenceAudioSynchronizer;
        ReferenceAudioLane.Score = score;
        ReferenceAudioLane.Waveform =
            _referenceAudioWaveform;

        if (projectChanged)
        {
            RaiseReferenceAudioProjectChanged();
            RefreshReferenceAudioPresentation();
        }
    }

    private static bool ReferenceSyncPointsEqual(
        IReadOnlyList<ReferenceAudioSyncPoint> left,
        IReadOnlyList<ReferenceAudioSyncPoint> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0;
             index < left.Count;
             index++)
        {
            if (Math.Abs(
                    left[index].ScoreBeat
                    - right[index].ScoreBeat)
                    > ScoreTiming.EventBeatTolerance
                || Math.Abs(
                    left[index].AudioSeconds
                    - right[index].AudioSeconds)
                    > 0.001d)
            {
                return false;
            }
        }

        return true;
    }

    private void RefreshReferenceAudioPresentation()
    {
        var enabled =
            _referenceAudioProject.Enabled;
        _suppressReferenceAudioControls = true;
        try
        {
            ReferenceAudioToggleButton.IsChecked =
                enabled;
            ReferenceAudioControlsPanel.Visibility =
                enabled
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            ReferenceAudioLane.Visibility =
                enabled
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            ReferenceAudioLaneColumn.Width =
                enabled
                    ? new GridLength(160d)
                    : new GridLength(0d);

            var choice =
                ReferencePlaybackChoices.First(
                    item =>
                        item.Mode
                        == _referenceAudioProject.PlaybackMode);
            ReferencePlaybackModeComboBox.SelectedItem =
                choice;
            ReferenceAudioVolumeSlider.Value =
                Math.Clamp(
                    _referenceAudioProject.ReferenceVolumePercent,
                    0,
                    100);
            ReferenceScoreVolumeSlider.Value =
                Math.Clamp(
                    _referenceAudioProject.ScoreVolumePercent,
                    0,
                    100);
            ReferenceAudioPanSlider.Value =
                Math.Clamp(
                    _referenceAudioProject.ReferencePanPercent,
                    -100,
                    100);
            ReferenceScorePanSlider.Value =
                Math.Clamp(
                    _referenceAudioProject.ScorePanPercent,
                    -100,
                    100);

            var duration =
                _referenceAudioWaveform?.DurationSeconds
                ?? 0d;
            ReferenceAudioPositionSlider.Maximum =
                Math.Max(1d, duration);
            ReferenceAudioPositionSlider.Value =
                Math.Clamp(
                    _referenceAudioCurrentSeconds,
                    0d,
                    Math.Max(1d, duration));
            ReferenceAudioPositionSlider.IsEnabled =
                enabled && duration > 0d;
            ReferenceAudioTimeText.Text =
                FormatAudioTime(
                    _referenceAudioCurrentSeconds);
        }
        finally
        {
            _suppressReferenceAudioControls = false;
        }

        ReferenceAudioLane.Waveform =
            _referenceAudioWaveform;
        ReferenceAudioLane.AudioPositionSeconds =
            _referenceAudioCurrentSeconds;

        var fileLabel =
            string.IsNullOrWhiteSpace(
                _referenceAudioProject.AudioPath)
                ? "音源未選択"
                : Path.GetFileName(
                    _referenceAudioProject.AudioPath);
        var gapCount =
            _referenceAudioSynchronizer
                ?.AudioOnlyGaps.Count
            ?? 0;
        var scoreStartAudioSeconds =
            _referenceAudioProject.SyncPoints
                .Where(point =>
                    Math.Abs(point.ScoreBeat)
                    <= ScoreTiming.EventBeatTolerance)
                .Select(point => point.AudioSeconds)
                .DefaultIfEmpty(0d)
                .Max();
        var startLabel =
            scoreStartAudioSeconds
                > ScoreTiming.EventBeatTolerance
                ? $" / 開始 {FormatAudioTime(scoreStartAudioSeconds)}"
                : string.Empty;
        var scoreEndAudioSeconds =
            _referenceAudioSynchronizer
                ?.ScoreBeatToAudioSecondsForRangeEnd(
                    RequireSession()
                        .Score
                        .ToMusicScore()
                        .LengthBeats)
            ?? 0d;
        var endLabel =
            _referenceAudioWaveform is not null
                ? $" / 終了 {FormatAudioTime(scoreEndAudioSeconds)}"
                : string.Empty;
        ReferenceAudioStatusText.Text =
            ReferenceAudioLane.SelectedSyncPoint
                is ReferenceAudioSyncPoint selected
                ? FormatReferenceSyncPointStatus(
                    selected)
                : $"{fileLabel}{startLabel}{endLabel} / 同期 {_referenceAudioProject.SyncPoints.Count}"
                  + (gapCount > 0
                      ? $" / カット {gapCount}区間"
                      : string.Empty);
    }

    private static string BuildReferenceAudioProjectSignature(
        ReferenceAudioProject project)
    {
        var path = string.IsNullOrWhiteSpace(
                project.AudioPath)
            ? string.Empty
            : Path.GetFullPath(
                    project.AudioPath)
                .ToUpperInvariant();
        var points = ReferenceAudioSynchronizer.Normalize(
                project.SyncPoints)
            .Select(point =>
                $"{point.ScoreBeat.ToString("R", CultureInfo.InvariantCulture)}@{point.AudioSeconds.ToString("R", CultureInfo.InvariantCulture)}");
        return string.Join(
            "|",
            project.Enabled ? "1" : "0",
            path,
            ((int)project.PlaybackMode).ToString(
                CultureInfo.InvariantCulture),
            project.ReferenceVolumePercent.ToString(
                CultureInfo.InvariantCulture),
            project.ScoreVolumePercent.ToString(
                CultureInfo.InvariantCulture),
            project.ReferencePanPercent.ToString(
                CultureInfo.InvariantCulture),
            project.ScorePanPercent.ToString(
                CultureInfo.InvariantCulture),
            string.Join(";", points));
    }

    private static string FormatAudioTime(
        double seconds)
    {
        var value = TimeSpan.FromSeconds(
            Math.Max(0d, seconds));
        return value.TotalHours >= 1d
            ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}"
            : $"{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
    }

    private void RangePreviewCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressPreviewRangeControls)
        {
            return;
        }

        var enabled = RangePreviewCheckBox.IsChecked == true;
        LoopCheckBox.IsEnabled = enabled;
        if (!enabled)
        {
            LoopCheckBox.IsChecked = false;
        }

        UpdatePreviewRangePresentation();
    }

    private void SetCursorPreviewButton_Click(object sender, RoutedEventArgs e)
        => SetPreviewRangeAround(EditorRoll.CursorBeat, EditorRoll.CursorBeat, "現在位置");

    private void SetSelectionPreviewButton_Click(object sender, RoutedEventArgs e)
        => SetPreviewRangeAroundSelection();

    private void PreviewMeasureCountComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_suppressPreviewRangeControls || RangePreviewCheckBox.IsChecked != true)
        {
            return;
        }

        UpdatePreviewRangePresentation();
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
        => TogglePreviewPlayback();

    private void StopPreviewButton_Click(object sender, RoutedEventArgs e)
        => RequestStopPreview();

    private void ScoreEditorView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsAltKey(e))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space)
        {
            TogglePreviewPlayback();
            e.Handled = true;
            return;
        }

        var textEntryFocused =
            Keyboard.FocusedElement is TextBoxBase or ComboBox;
        if (!textEntryFocused && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.R)
            {
                SetInsertHand(Hand.Right);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.L)
            {
                SetInsertHand(Hand.Left);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.K)
            {
                if (!e.IsRepeat)
                {
                    ScaleGuideToggleButton.IsChecked =
                        ScaleGuideToggleButton.IsChecked != true;
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.H)
            {
                UpdateChordGuide(show: true);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.B
                && _referenceAudioProject.Enabled)
            {
                ReferenceAudioCompareButton_Click(
                    sender,
                    e);
                e.Handled = true;
                return;
            }
        }

        if (ReferenceEquals(
                Keyboard.FocusedElement,
                ReferenceAudioLane)
            && e.Key == Key.Delete
            && ReferenceAudioLane.SelectedSyncPoint
                is ReferenceAudioSyncPoint selectedSyncPoint)
        {
            DeleteReferenceSyncPoint(
                selectedSyncPoint);
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement is TextBoxBase or ComboBox or ButtonBase)
        {
            return;
        }

        HandleNavigationKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (control && e.Key == Key.Z)
        {
            UndoButton_Click(sender, e);
            e.Handled = true;
        }
        else if (control && e.Key == Key.Y)
        {
            RedoButton_Click(sender, e);
            e.Handled = true;
        }
        else if (control && e.Key == Key.C)
        {
            CopySelected();
            e.Handled = true;
        }
        else if (control && e.Key == Key.X)
        {
            CopySelected();
            DeleteSelected();
            e.Handled = true;
        }
        else if (control && e.Key == Key.V)
        {
            PasteAtCursor();
            e.Handled = true;
        }
        else if (control && e.Key == Key.D)
        {
            DuplicateSelected();
            e.Handled = true;
        }
        else if (control
                 && Keyboard.Modifiers.HasFlag(
                     ModifierKeys.Shift)
                 && e.Key == Key.S)
        {
            WorkspaceSaveRequested?.Invoke(
                this,
                EventArgs.Empty);
            e.Handled = true;
        }
        else if (control && e.Key == Key.S)
        {
            RequestSave(saveAs: false);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
    }

    private void ScoreEditorView_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if ((e.Key == Key.H || e.SystemKey == Key.H)
            && Keyboard.FocusedElement is not TextBoxBase)
        {
            UpdateChordGuide(show: false);
            e.Handled = true;
        }
    }

    private void InsertRightHandButton_Click(object sender, RoutedEventArgs e)
        => SetInsertHand(Hand.Right);

    private void InsertLeftHandButton_Click(object sender, RoutedEventArgs e)
        => SetInsertHand(Hand.Left);

    private void ApplyKeySignatureButton_Click(object sender, RoutedEventArgs e)
    {
        if (ScaleTonicComboBox.SelectedItem is not ScaleTonicChoice tonic
            || ScaleTypeComboBox.SelectedItem is not MusicScaleDefinition scale)
        {
            StatusText.Text = "TonicとScaleを選択してください。";
            return;
        }

        var measure = RequireCurrentMeasure();
        RequireSession().SetKeySignature(
            EditableMusicScore.BeatToTick(measure.StartBeat),
            tonic.Fifths,
            scale.MusicXmlMode,
            scale.IsDiatonic ? null : scale.Id);
        ApplySessionToView(
            $"小節 {measure.Number} から {MusicTheoryAnalyzer.GetScaleDisplayName(tonic.Fifths, scale.Id)} に設定しました。");
        ScaleAdvancedPopup.IsOpen = false;
    }

    private void ApplyScaleCandidateButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ScaleCandidateComboBox.SelectedItem
            is not MusicScaleCandidate candidate)
        {
            StatusText.Text = "適用する推定候補を選択してください。";
            return;
        }

        var definition =
            MusicTheoryAnalyzer.GetScaleDefinition(candidate.ScaleId);
        if (definition is null)
        {
            StatusText.Text = "このScale候補は使用できません。";
            return;
        }

        var measure = RequireCurrentMeasure();
        RequireSession().SetKeySignature(
            EditableMusicScore.BeatToTick(measure.StartBeat),
            candidate.Fifths,
            definition.MusicXmlMode,
            definition.IsDiatonic ? null : definition.Id);
        ApplySessionToView(
            $"小節 {measure.Number} から {candidate.Label} に設定しました。");
    }

    private void ScaleAdvancedButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ScaleAdvancedPopup.IsOpen = true;
        ScaleTonicComboBox.Focus();
    }

    private void ScaleCandidateComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_suppressTheoryControls
            || ScaleCandidateComboBox.SelectedItem is not MusicScaleCandidate candidate)
        {
            return;
        }

        _suppressTheoryControls = true;
        try
        {
            var definition =
                MusicTheoryAnalyzer.GetScaleDefinition(candidate.ScaleId);
            ScaleTypeComboBox.SelectedItem = definition;
            RefreshScaleTonicChoices(
                candidate.ScaleId,
                candidate.Fifths);
        }
        finally
        {
            _suppressTheoryControls = false;
        }

        StatusText.Text = $"Scale候補: {candidate.Label}";
    }

    private void ScaleTypeComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_suppressTheoryControls
            || ScaleTypeComboBox.SelectedItem is not MusicScaleDefinition scale)
        {
            return;
        }

        var preferredFifths =
            ScaleTonicComboBox.SelectedItem is ScaleTonicChoice tonic
                ? tonic.Fifths
                : 0;
        RefreshScaleTonicChoices(scale.Id, preferredFifths);
    }

    private void ScaleGuideToggleButton_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressTheoryControls)
        {
            return;
        }

        UpdateScaleGuide();
        if (!_previewPlaying)
        {
            UpdateTheoryPresentation(force: true);
        }
    }

    private void ApplyChordButton_Click(object sender, RoutedEventArgs e)
    {
        var symbol = ChordComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            StatusText.Text = "コード名を入力してください。";
            return;
        }

        if (!MusicTheoryAnalyzer.IsValidChordSymbol(symbol))
        {
            StatusText.Text = "コード名を確認してください。例: C / Am / G7 / Fmaj7 / Cadd9";
            return;
        }

        if (_harmonyEditTick is not long tick)
        {
            StatusText.Text = "編集するコード位置を選択してください。";
            return;
        }

        var session = RequireSession();
        var beat = EditableMusicScore.TickToBeat(tick);
        var measure = session.Score.ToMusicScore().GetMeasureAt(beat);
        if (measure is null)
        {
            StatusText.Text = "選択した位置の小節を確認できません。";
            return;
        }

        var isExistingHarmony = session.Score.HarmonyEvents
            .Any(item => item.Tick == tick);
        try
        {
            session.SetHarmony(
                tick,
                symbol);
            var action = isExistingHarmony ? "変更" : "追加";
            ApplySessionToView(
                $"小節 {measure.Number} / {beat:0.###} beat のコードを {symbol} に{action}しました。");
            ChordComboBox.IsDropDownOpen = false;
            HarmonyEditorPopup.IsOpen = false;
        }
        catch (ArgumentException ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void RemoveChordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_harmonyEditTick is not long tick)
        {
            StatusText.Text = "編集するコード位置を選択してください。";
            return;
        }

        if (RequireSession().RemoveHarmony(tick))
        {
            ApplySessionToView(
                $"{EditableMusicScore.TickToBeat(tick):0.###} beat のコード指定を削除しました。");
            HarmonyEditorPopup.IsOpen = false;
        }
        else
        {
            StatusText.Text = "この位置には削除できるコード指定がありません。";
        }
    }

    private void ChordComboBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ApplyChordButton_Click(sender, e);
        e.Handled = true;
    }

    private void MeasureLane_ContextMenuOpening(
        object sender,
        ContextMenuEventArgs e)
    {
        var point = Mouse.GetPosition(
            MeasureLane);
        var measure = MeasureLane.GetMeasureAtLaneY(
            point.Y);
        if (measure is null)
        {
            _measureContextNumber = null;
            e.Handled = true;
            return;
        }

        var session = RequireSession();
        _measureContextNumber = measure.Number;
        _measureContextLaneY = point.Y;
        _pendingMeasurePropertyPopup =
            MeasurePropertyPopupKind.None;

        var tempo = GetTempoAtTick(
            session.Score,
            EditableMusicScore.BeatToTick(
                measure.StartBeat));
        MeasureContextInfoMenuItem.Header =
            $"小節 {measure.Number} / {measure.Beats}/{measure.BeatType} / {tempo:0.##} BPM";
        InsertMeasureBeforeMenuItem.Header =
            $"小節 {measure.Number} の前に空小節を挿入";
        InsertMeasureAfterMenuItem.Header =
            $"小節 {measure.Number} の後に空小節を挿入";
        DeleteMeasureMenuItem.Header =
            $"小節 {measure.Number} を削除";
        DeleteMeasureMenuItem.IsEnabled =
            session.Score.Measures.Count > 1;

        var measureStartTick =
            EditableMusicScore.BeatToTick(
                measure.StartBeat);
        RemoveMeasureTempoMenuItem.IsEnabled =
            measureStartTick > 0L
            && session.Score.TempoEvents.Any(item =>
                item.Tick == measureStartTick);
    }

    private void MeasureLaneContextMenu_Closed(
        object sender,
        RoutedEventArgs e)
    {
        var pending =
            _pendingMeasurePropertyPopup;
        _pendingMeasurePropertyPopup =
            MeasurePropertyPopupKind.None;

        if (pending != MeasurePropertyPopupKind.None)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(() =>
                    OpenMeasurePropertyPopup(
                        pending)));
            return;
        }

        _measureContextNumber = null;
        RestoreEditorInputFocus();
    }

    private void EditMeasureTimeSignatureMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (TryGetContextMeasure(out _))
        {
            _pendingMeasurePropertyPopup =
                MeasurePropertyPopupKind.TimeSignature;
        }
    }

    private void EditMeasureTempoMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (TryGetContextMeasure(out _))
        {
            _pendingMeasurePropertyPopup =
                MeasurePropertyPopupKind.Tempo;
        }
    }

    private void RemoveMeasureTempoMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!TryGetContextMeasure(out var target))
        {
            return;
        }

        PrepareForMeasureStructureEdit();
        var session = RequireSession();
        if (session.RemoveTempoAtMeasure(
                target.Number))
        {
            ApplySessionToView(
                $"小節 {target.Number} のテンポ変更を削除しました。");
        }
        else
        {
            StatusText.Text =
                "この小節の先頭には削除できるテンポ変更がありません。";
        }

        _measureContextNumber = null;
    }

    private void OpenMeasurePropertyPopup(
        MeasurePropertyPopupKind kind)
    {
        if (!TryGetContextMeasure(
                out var target))
        {
            RestoreEditorInputFocus();
            return;
        }

        var session = RequireSession();
        var horizontalOffset =
            Math.Max(
                8d,
                MeasureLane.ActualWidth - 4d);
        var verticalOffset = Math.Clamp(
            _measureContextLaneY - 24d,
            0d,
            Math.Max(
                0d,
                MeasureLane.ActualHeight - 150d));

        if (kind
            == MeasurePropertyPopupKind.TimeSignature)
        {
            MeasureTimeSignatureTitleText.Text =
                $"小節 {target.Number} の拍子";
            MeasureTimeSignatureCurrentText.Text =
                $"現在: {target.Beats}/{target.BeatType}";
            MeasureBeatsTextBox.Text =
                target.Beats.ToString(
                    CultureInfo.CurrentCulture);
            MeasureBeatTypeTextBox.Text =
                target.BeatType.ToString(
                    CultureInfo.CurrentCulture);
            MeasureTimeSignaturePopup.HorizontalOffset =
                horizontalOffset;
            MeasureTimeSignaturePopup.VerticalOffset =
                verticalOffset;
            MeasureTimeSignaturePopup.IsOpen = true;
            MeasureBeatsTextBox.Focus();
            MeasureBeatsTextBox.SelectAll();
            return;
        }

        var tempo = GetTempoAtTick(
            session.Score,
            target.StartTick);
        MeasureTempoTitleText.Text =
            $"小節 {target.Number} のテンポ";
        MeasureTempoCurrentText.Text =
            $"現在: {tempo:0.##} BPM";
        MeasureTempoTextBox.Text =
            tempo.ToString(
                "0.##",
                CultureInfo.CurrentCulture);
        MeasureTempoPopup.HorizontalOffset =
            horizontalOffset;
        MeasureTempoPopup.VerticalOffset =
            verticalOffset;
        MeasureTempoPopup.IsOpen = true;
        MeasureTempoTextBox.Focus();
        MeasureTempoTextBox.SelectAll();
    }

    private void ApplyMeasureTimeSignatureButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!TryGetContextMeasure(
                out var target))
        {
            return;
        }

        if (!int.TryParse(
                MeasureBeatsTextBox.Text,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out var beats)
            || !int.TryParse(
                MeasureBeatTypeTextBox.Text,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out var beatType)
            || beats <= 0
            || beatType <= 0
            || (beatType & (beatType - 1)) != 0)
        {
            StatusText.Text =
                "拍子は 4/4、3/4、6/8 のように入力してください。";
            return;
        }

        PrepareForMeasureStructureEdit();
        try
        {
            var startTick =
                target.StartTick;
            RequireSession()
                .SetMeasureTimeSignature(
                    target.Number,
                    beats,
                    beatType);
            ApplySessionToView(
                $"小節 {target.Number} の拍子を {beats}/{beatType} に変更しました。");
            EditorRoll.CursorBeat =
                EditableMusicScore.TickToBeat(
                    startTick);
            ResetPreviewRangeAfterMeasureEdit();
            MeasureTimeSignaturePopup.IsOpen = false;
        }
        catch (Exception ex) when (
            ex is ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void ApplyMeasureTempoButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!TryGetContextMeasure(
                out var target))
        {
            return;
        }

        if (!double.TryParse(
                MeasureTempoTextBox.Text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out var tempo)
            && !double.TryParse(
                MeasureTempoTextBox.Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out tempo)
            || tempo <= 0d
            || double.IsNaN(tempo)
            || double.IsInfinity(tempo))
        {
            StatusText.Text =
                "テンポは0より大きいBPMで入力してください。";
            return;
        }

        PrepareForMeasureStructureEdit();
        try
        {
            RequireSession().SetTempoAtMeasure(
                target.Number,
                tempo);
            ApplySessionToView(
                $"小節 {target.Number} から {tempo:0.##} BPM に変更しました。");
            EditorRoll.CursorBeat =
                EditableMusicScore.TickToBeat(
                    target.StartTick);
            MeasureTempoPopup.IsOpen = false;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void MeasureTimeSignatureTextBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ApplyMeasureTimeSignatureButton_Click(
            sender,
            e);
        e.Handled = true;
    }

    private void MeasureTempoTextBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ApplyMeasureTempoButton_Click(
            sender,
            e);
        e.Handled = true;
    }

    private void MeasurePropertyPopup_Closed(
        object? sender,
        EventArgs e)
    {
        _measureContextNumber = null;
        RestoreEditorInputFocus();
    }

    private void InsertMeasureBeforeMenuItem_Click(
        object sender,
        RoutedEventArgs e)
        => InsertMeasureFromContext(insertAfter: false);

    private void InsertMeasureAfterMenuItem_Click(
        object sender,
        RoutedEventArgs e)
        => InsertMeasureFromContext(insertAfter: true);

    private void DeleteMeasureMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!TryGetContextMeasure(out var target))
        {
            return;
        }

        PrepareForMeasureStructureEdit();
        var session = RequireSession();
        try
        {
            session.DeleteMeasure(target.Number);
            RemoveMissingSelections();
            ApplySessionToView(
                $"小節 {target.Number} を削除しました。");

            var destination = session.Score.Measures
                .OrderBy(measure => measure.StartTick)
                .FirstOrDefault(measure =>
                    measure.StartTick >= target.StartTick)
                ?? session.Score.Measures
                    .OrderBy(measure => measure.StartTick)
                    .Last();
            EditorRoll.CursorBeat =
                EditableMusicScore.TickToBeat(
                    destination.StartTick);
            ResetPreviewRangeAfterMeasureEdit();
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            _measureContextNumber = null;
        }
    }

    private void InsertMeasureFromContext(bool insertAfter)
    {
        if (!TryGetContextMeasure(out var target))
        {
            return;
        }

        PrepareForMeasureStructureEdit();
        var insertionTick = insertAfter
            ? target.EndTick
            : target.StartTick;
        var session = RequireSession();

        if (insertAfter)
        {
            session.InsertMeasureAfter(target.Number);
        }
        else
        {
            session.InsertMeasureBefore(target.Number);
        }

        ApplySessionToView(
            insertAfter
                ? $"小節 {target.Number} の後に空小節を挿入しました。"
                : $"小節 {target.Number} の前に空小節を挿入しました。");
        EditorRoll.CursorBeat =
            EditableMusicScore.TickToBeat(
                insertionTick);
        ResetPreviewRangeAfterMeasureEdit();
        _measureContextNumber = null;
    }

    private static double GetTempoAtTick(
        EditableMusicScore score,
        long tick)
        => score.TempoEvents
            .Where(item => item.Tick <= tick)
            .OrderByDescending(item => item.Tick)
            .FirstOrDefault()
            ?.BeatsPerMinute
            ?? 120d;

    private void RestoreEditorInputFocus()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                Focus();
                EditorRoll.Focus();
                Keyboard.Focus(
                    EditorRoll);
            }));
    }

    private bool TryGetContextMeasure(
        out EditableMeasure measure)
    {
        measure = null!;
        if (_measureContextNumber is not int measureNumber)
        {
            StatusText.Text =
                "小節レーンを右クリックして操作してください。";
            return false;
        }

        var found = RequireSession().Score.Measures
            .FirstOrDefault(item =>
                item.Number == measureNumber);
        if (found is null)
        {
            _measureContextNumber = null;
            StatusText.Text =
                "選択した小節を確認できません。";
            return false;
        }

        measure = found;
        return true;
    }

    private void PrepareForMeasureStructureEdit()
    {
        if (_previewPlaying || _previewPaused)
        {
            RequestStopPreview(
                restoreCursor: false);
        }

        HarmonyEditorPopup.IsOpen = false;
    }

    private void ResetPreviewRangeAfterMeasureEdit()
    {
        if (_session is null)
        {
            return;
        }

        _previewStartBeat = 0d;
        _previewEndBeat =
            EditableMusicScore.TickToBeat(
                _session.Score.LengthTicks);
        UpdatePreviewRangePresentation();
    }

    private void MeasureLane_HarmonyEditRequested(
        object? sender,
        EditorHarmonyEditRequestedEventArgs e)
    {
        if (_previewPlaying || _previewPaused)
        {
            RequestStopPreview(restoreCursor: false);
        }

        var session = RequireSession();
        var score = EditorRoll.Score ?? session.Score.ToMusicScore();
        EnsureTheoryCache(score);

        var editTick = Math.Clamp(
            EditableMusicScore.BeatToTick(e.Beat),
            0L,
            Math.Max(0L, session.Score.LengthTicks - 1L));
        _harmonyEditTick = editTick;

        var editBeat = EditableMusicScore.TickToBeat(editTick);
        var measure = score.GetMeasureAt(editBeat);
        if (measure is null)
        {
            StatusText.Text = "選択した位置の小節を確認できません。";
            _harmonyEditTick = null;
            return;
        }

        RefreshChordSuggestions(score, measure);

        var explicitHarmony = session.Score.HarmonyEvents
            .FirstOrDefault(item => item.Tick == editTick);
        var activeHarmony = GetCachedHarmonySegment(editBeat);
        var nextHarmony = session.Score.HarmonyEvents
            .Where(item => item.Tick > editTick)
            .OrderBy(item => item.Tick)
            .FirstOrDefault();
        var targetEndTick = nextHarmony?.Tick
            ?? session.Score.LengthTicks;
        var targetEndBeat = EditableMusicScore.TickToBeat(
            targetEndTick);

        ChordComboBox.SelectedItem = null;
        ChordComboBox.Text =
            explicitHarmony?.Symbol
            ?? activeHarmony?.Symbol
            ?? string.Empty;
        ChordComboBox.IsDropDownOpen = false;

        var isExistingHarmony = explicitHarmony is not null;
        HarmonyPopupTitleText.Text =
            isExistingHarmony
                ? "コードを変更"
                : "コードを追加";
        ApplyChordButton.Content =
            isExistingHarmony
                ? "変更"
                : "追加";
        RemoveChordButton.Visibility =
            isExistingHarmony
                ? Visibility.Visible
                : Visibility.Collapsed;

        HarmonyTargetCodeText.Text = isExistingHarmony
            ? $"現在: {explicitHarmony!.Symbol}"
            : activeHarmony is null
                ? "現在の有効コード: —"
                : $"現在の有効コード: {activeHarmony.Symbol}";
        HarmonyTargetRangeText.Text = isExistingHarmony
            ? $"変更対象: {editBeat:0.###}–{targetEndBeat:0.###} beat"
            : $"追加後の適用範囲: {editBeat:0.###}–{targetEndBeat:0.###} beat";
        ChordSourceText.Text = isExistingHarmony
            ? "この位置から有効なコードを変更します。"
            : activeHarmony is null
                ? "この位置から新しいコードを設定します。"
                : activeHarmony.IsInferred
                    ? "推定コードを入力欄に表示しています。必要に応じて変更してください。"
                    : "現在のコードを入力欄に表示しています。必要に応じて変更してください。";

        MeasureLane.ShowHarmonyEditTarget(
            editBeat,
            targetEndBeat);

        HarmonyPopupPositionText.Text =
            $"小節 {e.MeasureNumber} / {editBeat:0.###} beat";
        HarmonyEditorPopup.HorizontalOffset =
            Math.Max(8d, MeasureLane.ActualWidth - 4d);
        HarmonyEditorPopup.VerticalOffset = Math.Clamp(
            e.LaneY - 28d,
            0d,
            Math.Max(0d, MeasureLane.ActualHeight - 160d));
        HarmonyEditorPopup.IsOpen = true;
        ChordComboBox.Focus();
        StatusText.Text = isExistingHarmony
            ? $"小節 {e.MeasureNumber} / {editBeat:0.###} beat のコードを変更"
            : $"小節 {e.MeasureNumber} / {editBeat:0.###} beat にコードを追加";
    }

    private void HarmonyEditorPopup_Closed(
        object? sender,
        EventArgs e)
    {
        _harmonyEditTick = null;
        ChordComboBox.IsDropDownOpen = false;
        MeasureLane.ClearHarmonyEditTarget();
    }

    private void EditorRoll_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        Focus();
        EditorRoll.Focus();
        BeginRollPan(e.GetPosition(EditorRoll));
        e.Handled = true;
    }

    private void EditorRoll_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || !_rollPanActive)
        {
            return;
        }

        EndRollPan();
        e.Handled = true;
    }

    private void EditorRoll_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        EditorRoll.Focus();

        var point = e.GetPosition(EditorRoll);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)
            || Keyboard.IsKeyDown(Key.LeftAlt)
            || Keyboard.IsKeyDown(Key.RightAlt);
        if (alt)
        {
            BeginRollPan(point);
            e.Handled = true;
            return;
        }
        if (EditorRoll.TryHitTestPreviewBoundary(point, out var boundary))
        {
            _previewBoundaryDrag = boundary;
            _hasPendingInsertPoint = false;
            EditorRoll.HideDragPitchPreview();
            EditorRoll.CaptureMouse();
            EditorRoll.Cursor = Cursors.SizeNS;
            e.Handled = true;
            return;
        }

        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (EditorRoll.TryHitTestNote(point, out var hit))
        {
            _hasPendingInsertPoint = false;
            if (control)
            {
                if (!_selectedNoteIds.Add(hit.NoteId))
                {
                    _selectedNoteIds.Remove(hit.NoteId);
                    ApplySelection();
                    e.Handled = true;
                    return;
                }
            }
            else if (!_selectedNoteIds.Contains(hit.NoteId))
            {
                _selectedNoteIds.Clear();
                _selectedNoteIds.Add(hit.NoteId);
            }

            ApplySelection();
            var session = RequireSession();
            var originals = session.Score.Notes
                .Where(note => _selectedNoteIds.Contains(note.Id))
                .ToDictionary(
                    note => note.Id,
                    note => new NoteOriginal(note.StartTick, note.DurationTick, note.MidiNote));
            _noteDrag = new NoteDragState(
                point,
                EditorRoll.PointToBeat(point),
                EditorRoll.PointToMidi(point),
                hit.IsResizeEdge,
                originals);
            EditorRoll.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ClickCount >= 2)
        {
            var tick = _hasPendingInsertPoint
                ? _pendingInsertTick
                : SnapTick(EditableMusicScore.BeatToTick(EditorRoll.PointToBeat(point)));
            var midi = _hasPendingInsertPoint
                ? _pendingInsertMidi
                : EditorRoll.PointToMidi(point);

            if (_hasPendingInsertPoint)
            {
                EditorRoll.CursorBeat = _pendingInsertViewportBeat;
            }

            _hasPendingInsertPoint = false;
            InsertNoteAtStablePosition(tick, midi);
            e.Handled = true;
            return;
        }

        _pendingInsertTick = SnapTick(EditableMusicScore.BeatToTick(EditorRoll.PointToBeat(point)));
        _pendingInsertMidi = EditorRoll.PointToMidi(point);
        _pendingInsertViewportBeat = EditorRoll.CursorBeat;
        _hasPendingInsertPoint = true;
        _selectionDrag = new SelectionDragState(point, control);
        EditorRoll.CaptureMouse();
        e.Handled = true;
    }

    private void EditorRoll_MouseMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(EditorRoll);
        if (_rollPanActive
            && (e.LeftButton == MouseButtonState.Pressed
                || e.MiddleButton == MouseButtonState.Pressed))
        {
            MeasureLane.UpdateExternalPan(point.Y);
            if (_rollPanPitchEnabled)
            {
                var laneWidth = Math.Max(1d, EditorRoll.PitchLaneWidth);
                var pitchDelta = (int)Math.Round(
                    (point.X - _rollPanStartPoint.X) / laneWidth,
                    MidpointRounding.AwayFromZero);
                EditorRoll.SetPitchViewportLowest(
                    _rollPanStartLowestMidi - pitchDelta);
            }

            EditorRoll.Cursor = Cursors.Hand;
            e.Handled = true;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (_previewBoundaryDrag is not null)
            {
                EditorRoll.HideDragPitchPreview();
                UpdatePreviewBoundaryFromPoint(point);
                EditorRoll.Cursor = Cursors.SizeNS;
                e.Handled = true;
                return;
            }

            if (_noteDrag is not null)
            {
                if (_noteDrag.Resize)
                {
                    EditorRoll.HideDragPitchPreview();
                }
                else
                {
                    EditorRoll.ShowDragPitchPreview(EditorRoll.PointToMidi(point), point);
                }

                PreviewNoteDrag(point);
                e.Handled = true;
                return;
            }

            if (_selectionDrag is not null)
            {
                var rectangle = CreateRectangle(_selectionDrag.StartPoint, point);
                EditorRoll.SelectionRectangle = rectangle;
                if (rectangle.Width >= 4d || rectangle.Height >= 4d)
                {
                    _hasPendingInsertPoint = false;
                }

                e.Handled = true;
            }

            return;
        }

        EditorRoll.HideDragPitchPreview();
        UpdateEditorRollCursor(point);
    }

    private void EditorRoll_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_rollPanActive)
        {
            EndRollPan();
            e.Handled = true;
            return;
        }

        EditorRoll.HideDragPitchPreview();
        var point = e.GetPosition(EditorRoll);

        if (_previewBoundaryDrag is not null)
        {
            UpdatePreviewBoundaryFromPoint(point);
            _previewBoundaryDrag = null;
            EditorRoll.ReleaseMouseCapture();
            EditorRoll.Cursor = Cursors.Cross;
            e.Handled = true;
            return;
        }

        if (_hasPendingInsertPoint && _selectionDrag is not null)
        {
            var rectangle = CreateRectangle(_selectionDrag.StartPoint, point);
            if (rectangle.Width < 4d && rectangle.Height < 4d)
            {
                if (!_selectionDrag.Additive)
                {
                    _selectedNoteIds.Clear();
                    ApplySelection();
                }

                _selectionDrag = null;
                EditorRoll.SelectionRectangle = null;
                EditorRoll.ReleaseMouseCapture();
                StatusText.Text = "空白をダブルクリックするとノートを挿入できます。";
                e.Handled = true;
                return;
            }
        }

        try
        {
            if (_noteDrag is not null)
            {
                CommitNoteDrag(point);
            }
            else if (_selectionDrag is not null)
            {
                CompleteSelectionDrag(point);
            }
        }
        finally
        {
            _noteDrag = null;
            _selectionDrag = null;
            EditorRoll.SelectionRectangle = null;
            EditorRoll.ReleaseMouseCapture();
            ApplySessionToView(StatusText.Text);
        }

        e.Handled = true;
    }

    private void EditorRoll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        RestoreEditorInputFocusAfterNavigation();

        var modifiers =
            Keyboard.Modifiers;
        var alt =
            modifiers.HasFlag(
                ModifierKeys.Alt)
            || Keyboard.IsKeyDown(
                Key.LeftAlt)
            || Keyboard.IsKeyDown(
                Key.RightAlt);
        var shift =
            modifiers.HasFlag(
                ModifierKeys.Shift);
        var control =
            modifiers.HasFlag(
                ModifierKeys.Control);

        if (!alt
            && control
            && shift)
        {
            var scoreLength =
                RequireSession()
                    .Score
                    .ToMusicScore()
                    .LengthBeats;
            SetTimelineCursorBeat(
                e.Delta > 0
                    ? scoreLength
                    : 0d,
                showStatus: true);
            RefreshEditorRollCursor();
            e.Handled = true;
            return;
        }

        if (alt && shift)
        {
            var point =
                e.GetPosition(
                    EditorRoll);
            var anchorRatio =
                EditorRoll.ActualWidth <= 0d
                    ? 0.5d
                    : Math.Clamp(
                        point.X
                        / EditorRoll.ActualWidth,
                        0d,
                        1d);
            EditorRoll.ZoomPitchViewport(
                e.Delta > 0,
                anchorRatio);
            StatusText.Text =
                $"表示音域 {GetVisiblePitchRangeText()}";
            RefreshEditorRollCursor();
            e.Handled = true;
            return;
        }

        if (alt)
        {
            EditorRoll.PanPitchViewport(
                e.Delta > 0
                    ? -PitchPanSemitones
                    : PitchPanSemitones);
            StatusText.Text =
                $"表示音域 {GetVisiblePitchRangeText()}";
            RefreshEditorRollCursor();
            e.Handled = true;
            return;
        }

        if (control)
        {
            EditorRoll.VisibleBeats *=
                e.Delta > 0
                    ? 0.8d
                    : 1.25d;
            StatusText.Text =
                $"時間ズーム: {EditorRoll.VisibleBeats:0.#} beat 表示";
            RefreshEditorRollCursor();
            e.Handled = true;
            return;
        }

        var step =
            EditableMusicScore.TickToBeat(
                _gridTicks)
            * 4d;
        if (shift)
        {
            step *=
                4d;
        }

        SetTimelineCursorBeat(
            EditorRoll.CursorBeat
            + (e.Delta > 0
                ? step
                : -step),
            showStatus: true);
        RefreshEditorRollCursor();
        e.Handled = true;
    }

    private static bool IsAltKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return key is Key.LeftAlt or Key.RightAlt;
    }

    private void BeginRollPan(Point point)
    {
        if (_rollPanActive)
        {
            return;
        }

        _hasPendingInsertPoint = false;
        _previewBoundaryDrag = null;
        _noteDrag = null;
        _selectionDrag = null;
        EditorRoll.SelectionRectangle = null;
        EditorRoll.HideDragPitchPreview();
        _rollPanStartPoint = point;
        _rollPanStartLowestMidi = EditorRoll.VisiblePitchRange.LowestMidi;
        MeasureLane.BeginExternalPan(point.Y);
        _rollPanActive = true;
        EditorRoll.CaptureMouse();
        EditorRoll.Cursor = Cursors.Hand;
        StatusText.Text = _rollPanPitchEnabled
            ? "時間・音域パン"
            : "時間方向パン";
    }

    private void EndRollPan()
    {
        if (!_rollPanActive)
        {
            return;
        }

        _rollPanActive = false;
        MeasureLane.EndExternalPan();
        if (EditorRoll.IsMouseCaptured)
        {
            EditorRoll.ReleaseMouseCapture();
        }

        EditorRoll.Cursor = Cursors.Cross;
        StatusText.Text = _rollPanPitchEnabled
            ? $"現在位置 {EditorRoll.CursorBeat:0.###} beat / 表示音域 {GetVisiblePitchRangeText()}"
            : $"現在位置 {EditorRoll.CursorBeat:0.###} beat";
    }

    private void EditorRoll_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_rollPanActive)
        {
            return;
        }

        if (Mouse.LeftButton != MouseButtonState.Pressed
            && Mouse.MiddleButton != MouseButtonState.Pressed)
        {
            EditorRoll.HideDragPitchPreview();
            EditorRoll.Cursor = Cursors.Arrow;
        }
    }

    private void RefreshEditorRollCursor()
    {
        if (_rollPanActive
            || !EditorRoll.IsMouseOver
            || Mouse.LeftButton == MouseButtonState.Pressed
            || Mouse.MiddleButton == MouseButtonState.Pressed)
        {
            return;
        }

        UpdateEditorRollCursor(Mouse.GetPosition(EditorRoll));
    }

    private void UpdateEditorRollCursor(Point point)
    {
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)
            || Keyboard.IsKeyDown(Key.LeftAlt)
            || Keyboard.IsKeyDown(Key.RightAlt);
        if (alt)
        {
            EditorRoll.Cursor = Cursors.Hand;
        }
        else if (EditorRoll.TryHitTestPreviewBoundary(point, out _))
        {
            EditorRoll.Cursor = Cursors.SizeNS;
        }
        else if (EditorRoll.TryHitTestNote(point, out var hit))
        {
            EditorRoll.Cursor = hit.IsResizeEdge ? Cursors.SizeNS : Cursors.SizeAll;
        }
        else
        {
            EditorRoll.Cursor = Cursors.Cross;
        }
    }

    private void PreviewNoteDrag(Point point)
    {
        var drag = _noteDrag!;
        var session = RequireSession();
        var clone = session.Score.Clone();
        var currentTick = SnapTick(EditableMusicScore.BeatToTick(EditorRoll.PointToBeat(point)));
        var startTick = SnapTick(EditableMusicScore.BeatToTick(drag.StartBeat));
        var deltaTicks = currentTick - startTick;
        var deltaMidi = EditorRoll.PointToMidi(point) - drag.StartMidi;

        foreach (var note in clone.Notes.Where(note => drag.Originals.ContainsKey(note.Id)))
        {
            var original = drag.Originals[note.Id];
            if (drag.Resize)
            {
                note.DurationTick = original.DurationTick + deltaTicks;
            }
            else
            {
                note.StartTick = original.StartTick + deltaTicks;
                note.MidiNote = original.MidiNote + deltaMidi;
            }
        }

        try
        {
            ScoreEditSession.Validate(clone);
            EditorRoll.Score = clone.ToMusicScore();
            EditorRoll.SelectedNoteIds = _selectedNoteIds.ToArray();
            StatusText.Text = drag.Resize
                ? $"長さ変更 {deltaTicks:+#;-#;0} tick"
                : $"移動 {deltaTicks:+#;-#;0} tick / {deltaMidi:+#;-#;0} 半音";
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void CommitNoteDrag(Point point)
    {
        var drag = _noteDrag!;
        var session = RequireSession();
        var currentTick = SnapTick(EditableMusicScore.BeatToTick(EditorRoll.PointToBeat(point)));
        var startTick = SnapTick(EditableMusicScore.BeatToTick(drag.StartBeat));
        var deltaTicks = currentTick - startTick;
        var deltaMidi = EditorRoll.PointToMidi(point) - drag.StartMidi;

        try
        {
            if (drag.Resize)
            {
                session.ResizeNotes(_selectedNoteIds, deltaTicks);
            }
            else
            {
                session.MoveNotes(_selectedNoteIds, deltaTicks, deltaMidi);
            }
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void CompleteSelectionDrag(Point point)
    {
        var drag = _selectionDrag!;
        var rectangle = CreateRectangle(drag.StartPoint, point);
        if (rectangle.Width >= 4d || rectangle.Height >= 4d)
        {
            var ids = EditorRoll.GetNoteIdsInRectangle(rectangle);
            if (!drag.Additive)
            {
                _selectedNoteIds.Clear();
            }

            foreach (var id in ids)
            {
                _selectedNoteIds.Add(id);
            }

            ApplySelection();
            StatusText.Text = $"{_selectedNoteIds.Count} ノートを選択中";
            return;
        }

        if (!drag.Additive)
        {
            _selectedNoteIds.Clear();
            ApplySelection();
        }

        var tick = SnapTick(
            EditableMusicScore.BeatToTick(
                EditorRoll.PointToBeat(point)));
        SetTimelineCursorBeat(
            EditableMusicScore.TickToBeat(tick),
            showStatus: true);
    }

    private void CopySelected()
    {
        _clipboard = RequireSession().CopyNotes(_selectedNoteIds);
        StatusText.Text = _clipboard.Count == 0
            ? "コピーするノートが選択されていません。"
            : $"{_clipboard.Count} ノートをコピーしました。";
    }

    private void PasteAtCursor()
    {
        if (_clipboard.Count == 0)
        {
            StatusText.Text = "貼り付けるノートがありません。";
            return;
        }

        var targetTick = SnapTick(EditableMusicScore.BeatToTick(EditorRoll.CursorBeat));
        try
        {
            var ids = RequireSession().PasteNotes(_clipboard, targetTick);
            SetSelection(ids);
            ApplySessionToView($"{ids.Count} ノートを貼り付けました。");
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void DuplicateSelected()
    {
        var session = RequireSession();
        var clipboard = session.CopyNotes(_selectedNoteIds);
        if (clipboard.Count == 0)
        {
            StatusText.Text = "複製するノートが選択されていません。";
            return;
        }

        var selected = session.Score.Notes.Where(note => _selectedNoteIds.Contains(note.Id)).ToArray();
        var targetTick = selected.Max(note => note.EndTick);
        try
        {
            var ids = session.PasteNotes(clipboard, targetTick);
            SetSelection(ids);
            ApplySessionToView($"{ids.Count} ノートを複製しました。");
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void DeleteSelected()
    {
        if (_selectedNoteIds.Count == 0)
        {
            return;
        }

        RequireSession().DeleteNotes(_selectedNoteIds);
        _selectedNoteIds.Clear();
        ApplySessionToView("選択ノートを削除しました。");
    }

    private void SplitSelected(int parts)
    {
        if (_selectedNoteIds.Count == 0)
        {
            return;
        }

        try
        {
            var ids = RequireSession().SplitNotes(_selectedNoteIds, parts);
            SetSelection(ids);
            ApplySessionToView($"選択ノートを{parts}分割しました。");
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void SetSelectedHand(Hand hand)
    {
        if (_selectedNoteIds.Count == 0)
        {
            return;
        }

        try
        {
            RequireSession().SetHand(_selectedNoteIds, hand);
            ApplySessionToView(hand == Hand.Right ? "右手へ変更しました。" : "左手へ変更しました。");
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void SetPreviewRangeAroundSelection()
    {
        var score = RequireSession().Score.ToMusicScore();
        var selected = score.Notes
            .Where(note => _selectedNoteIds.Contains(note.Id))
            .ToArray();
        if (selected.Length == 0)
        {
            StatusText.Text = "選択ノートがありません。「現在位置周辺」を使用できます。";
            return;
        }

        SetPreviewRangeAround(
            selected.Min(note => note.StartBeat),
            selected.Max(note => note.EndBeat),
            "選択箇所");
    }

    private void SetPreviewRangeAround(double anchorStart, double anchorEnd, string sourceLabel)
    {
        var score = RequireSession().Score.ToMusicScore();
        var measures = score.Measures.OrderBy(measure => measure.StartBeat).ToArray();
        var beforeCount = GetSelectedPreviewMeasureCount(PreviewBeforeMeasureComboBox);
        var afterCount = GetSelectedPreviewMeasureCount(PreviewAfterMeasureComboBox);

        if (measures.Length == 0)
        {
            var fallbackMeasureBeats = 4d;
            _previewStartBeat = Math.Max(0d, anchorStart - beforeCount * fallbackMeasureBeats);
            _previewEndBeat = Math.Min(score.LengthBeats, anchorEnd + afterCount * fallbackMeasureBeats);
        }
        else
        {
            var startIndex = Math.Max(
                0,
                Array.FindLastIndex(
                    measures,
                    measure => measure.StartBeat <= anchorStart + ScoreTiming.EventBeatTolerance));
            var endIndex = Math.Max(
                startIndex,
                Array.FindLastIndex(
                    measures,
                    measure => measure.StartBeat <= anchorEnd + ScoreTiming.EventBeatTolerance));
            _previewStartBeat = measures[Math.Max(0, startIndex - beforeCount)].StartBeat;
            _previewEndBeat = measures[Math.Min(measures.Length - 1, endIndex + afterCount)].EndBeat;
        }

        _suppressPreviewRangeControls = true;
        try
        {
            RangePreviewCheckBox.IsChecked = true;
            LoopCheckBox.IsEnabled = true;
        }
        finally
        {
            _suppressPreviewRangeControls = false;
        }

        UpdatePreviewRangePresentation();
        StatusText.Text =
            $"{sourceLabel}を基準に前{beforeCount} / 後{afterCount}小節の試聴範囲を設定しました。";
    }

    private void TogglePreviewPlayback()
    {
        if (_previewPlaying)
        {
            _previewPlaying = false;
            _previewPaused = true;
            PausePreviewRequested?.Invoke(this, EventArgs.Empty);
            PreviewButton.Content = "▶ 再開  Space";
            return;
        }

        if (_previewPaused)
        {
            ResumePreviewRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        RequestPreview();
    }

    private void RequestPreview()
    {
        var score = RequireSession().Score.ToMusicScore();
        var rangeEnabled = RangePreviewCheckBox.IsChecked == true;
        var startBeat = rangeEnabled
            ? _previewStartBeat
            : Math.Clamp(EditorRoll.CursorBeat, 0d, score.LengthBeats);
        var endBeat = rangeEnabled
            ? _previewEndBeat
            : score.LengthBeats;

        var playbackMode =
            _referenceAudioProject.Enabled
                ? _referenceAudioProject.PlaybackMode
                : EditorReferencePlaybackMode.ScoreOnly;
        if (playbackMode
                != EditorReferencePlaybackMode.ReferenceOnly
            && endBeat
                <= startBeat
                + ScoreTiming.EventBeatTolerance)
        {
            StatusText.Text = "現在位置より後に再生できる範囲がありません。";
            return;
        }

        _previewPlaying = true;
        _previewPaused = false;
        PreviewButton.Content = "⏸ 一時停止  Space";
        double? referenceStartSeconds = null;
        double? referenceEndSeconds = null;
        if (playbackMode
            == EditorReferencePlaybackMode.ReferenceOnly)
        {
            referenceStartSeconds =
                _referenceAudioCurrentSeconds;
            if (rangeEnabled
                && _referenceAudioSynchronizer is not null)
            {
                referenceEndSeconds =
                    _referenceAudioSynchronizer
                        .ScoreBeatToAudioSecondsForRangeEnd(
                            endBeat);
                if (referenceEndSeconds
                    <= referenceStartSeconds
                    + 0.001d)
                {
                    referenceEndSeconds =
                        _referenceAudioWaveform
                            ?.DurationSeconds;
                }
            }
            else
            {
                referenceEndSeconds =
                    _referenceAudioWaveform
                        ?.DurationSeconds;
            }
        }

        if (playbackMode
                == EditorReferencePlaybackMode.ReferenceOnly
            && referenceStartSeconds is double sourceStart
            && referenceEndSeconds is double sourceEnd
            && sourceEnd <= sourceStart + 0.001d)
        {
            _previewPlaying = false;
            PreviewButton.Content =
                "▶ 試聴  Space";
            StatusText.Text =
                "現在の原音位置より後に再生できる範囲がありません。";
            return;
        }

        PreviewRequested?.Invoke(
            this,
            new ScoreEditorPreviewRequestedEventArgs(
                score,
                startBeat,
                endBeat,
                rangeEnabled && LoopCheckBox.IsChecked == true,
                playbackMode,
                referenceAudioStartSeconds:
                    referenceStartSeconds,
                referenceAudioEndSeconds:
                    referenceEndSeconds));
    }

    private void UpdatePreviewRangePresentation()
    {
        var enabled = RangePreviewCheckBox.IsChecked == true;
        EditorRoll.PreviewStartBeat = enabled ? _previewStartBeat : null;
        EditorRoll.PreviewEndBeat = enabled ? _previewEndBeat : null;
        PreviewRangeText.Text = enabled
            ? $"A {_previewStartBeat:0.###} → B {_previewEndBeat:0.###} beat"
            : "範囲再生: OFF / 現在位置から曲末";
    }

    private static int GetSelectedPreviewMeasureCount(ComboBox comboBox)
        => comboBox.SelectedItem is PreviewMeasureChoice choice
            ? choice.MeasureCount
            : 1;

    private void SyncMeasureLane()
    {
        if (MeasureLane is null || EditorRoll is null)
        {
            return;
        }

        MeasureLane.Score = EditorRoll.Score;
        MeasureLane.CursorBeat = EditorRoll.CursorBeat;
        MeasureLane.VisibleBeats = EditorRoll.VisibleBeats;
        MeasureLane.GridTicks = EditorRoll.GridTicks;
        ReferenceAudioLane.Score = EditorRoll.Score;
        ReferenceAudioLane.CursorBeat = EditorRoll.CursorBeat;
        ReferenceAudioLane.VisibleBeats = EditorRoll.VisibleBeats;
        ReferenceAudioLane.GridTicks = EditorRoll.GridTicks;
        ReferenceAudioLane.Synchronizer = _referenceAudioSynchronizer;
        ReferenceAudioLane.Waveform = _referenceAudioWaveform;
    }

    private void SetInsertHand(Hand hand)
    {
        _insertHand = hand;
        if (InsertRightHandButton is not null)
        {
            InsertRightHandButton.IsChecked = hand == Hand.Right;
        }

        if (InsertLeftHandButton is not null)
        {
            InsertLeftHandButton.IsChecked = hand == Hand.Left;
        }

        StatusText.Text = hand == Hand.Right
            ? "右手ノート挿入モード"
            : "左手ノート挿入モード";
    }

    private void UpdateTheoryPresentation(bool force = false)
    {
        if (_previewPlaying
            || _session is null
            || EditorRoll is null
            || ScaleCandidateComboBox is null
            || ScaleTonicComboBox is null
            || ScaleTypeComboBox is null
            || ChordComboBox is null)
        {
            return;
        }

        var score = EditorRoll.Score ?? _session.Score.ToMusicScore();
        var cursorBeat = EditorRoll.CursorBeat;
        if (!force
            && ReferenceEquals(_theoryPresentationScore, score)
            && cursorBeat >= _theoryPresentationStartBeat - ScoreTiming.EventBeatTolerance
            && cursorBeat < _theoryPresentationEndBeat - ScoreTiming.EventBeatTolerance)
        {
            return;
        }

        EnsureTheoryCache(score);
        var measure = score.GetMeasureAt(cursorBeat);
        var key = GetCachedKeyAnalysis(score, cursorBeat);

        var contextStart = measure?.StartBeat ?? 0d;
        var contextEnd = measure?.EndBeat ?? score.LengthBeats;
        var activeKey = score.KeySignatureEvents
            .Where(item =>
                item.Beat <= cursorBeat + ScoreTiming.EventBeatTolerance)
            .OrderByDescending(item => item.Beat)
            .FirstOrDefault();
        if (activeKey is not null)
        {
            contextStart = Math.Max(contextStart, activeKey.Beat);
        }

        var nextKey = score.KeySignatureEvents
            .Where(item =>
                item.Beat > cursorBeat + ScoreTiming.EventBeatTolerance)
            .OrderBy(item => item.Beat)
            .FirstOrDefault();
        if (nextKey is not null)
        {
            contextEnd = Math.Min(contextEnd, nextKey.Beat);
        }

        _suppressTheoryControls = true;
        try
        {
            ScaleCandidateComboBox.ItemsSource = _theoryScaleCandidates;
            if (key is null)
            {
                ScaleCandidateComboBox.SelectedItem = null;
                ScaleCurrentText.Text = "現在: —";
                KeySourceText.Text = "—";
            }
            else
            {
                var scale =
                    MusicTheoryAnalyzer.GetScaleDefinition(key.ScaleId);
                ScaleTypeComboBox.SelectedItem = scale;
                RefreshScaleTonicChoices(key.ScaleId, key.Fifths);
                ScaleCandidateComboBox.SelectedItem =
                    _theoryScaleCandidates.FirstOrDefault(candidate =>
                        candidate.Fifths == key.Fifths
                        && string.Equals(
                            candidate.ScaleId,
                            key.ScaleId,
                            StringComparison.OrdinalIgnoreCase));
                ScaleCurrentText.Text =
                    $"現在: {MusicTheoryAnalyzer.GetScaleDisplayName(key.Fifths, key.ScaleId)}";
                KeySourceText.Text = key.IsInferred ? "推定" : "設定済み";
            }
        }
        finally
        {
            _suppressTheoryControls = false;
        }

        UpdateScaleGuide();

        var harmony = GetCachedHarmonySegment(cursorBeat);
        if (harmony is not null)
        {
            contextStart = Math.Max(
                contextStart,
                harmony.StartBeat);
            contextEnd = Math.Min(
                contextEnd,
                harmony.EndBeat);
        }

        if (measure is not null)
        {
            RefreshChordSuggestions(score, measure);
        }

        if (!ChordComboBox.IsKeyboardFocusWithin)
        {
            ChordComboBox.Text =
                harmony?.Symbol
                ?? string.Empty;
        }

        ChordSourceText.Text = harmony is null
            ? "—"
            : harmony.IsInferred ? "推定" : "設定済み";
        ChordSourceText.Foreground = harmony?.IsInferred == true
            ? Brushes.Gray
            : (Brush)FindResource("TextMutedBrush");

        UpdateChordGuide(
            Keyboard.IsKeyDown(Key.H));

        _theoryPresentationScore = score;
        _theoryPresentationStartBeat = contextStart;
        _theoryPresentationEndBeat = Math.Max(
            contextStart + ScoreTiming.EventBeatTolerance,
            contextEnd);
    }

    private void UpdateScaleGuide()
    {
        EditorRoll.ScaleGuideRegions =
            ScaleGuideToggleButton.IsChecked == true && _session is not null
                ? _theoryScaleGuideRegions
                : Array.Empty<EditorScaleGuideRegion>();
    }

    private void UpdateChordGuide(bool show)
    {
        EditorRoll.ChordGuideRegions =
            show && _session is not null
                ? _theoryChordGuideRegions
                : Array.Empty<EditorChordGuideRegion>();
    }

    private void EnsureTheoryCache(MusicScore score)
    {
        if (ReferenceEquals(_theoryCacheScore, score))
        {
            return;
        }

        _theoryCacheScore = score;
        _theoryHarmonyTimeline =
            MusicTheoryAnalyzer.CreateHarmonyTimeline(score);
        _theoryScaleGuideRegions =
            MusicTheoryAnalyzer.CreateScaleGuideRegions(score);
        _theoryChordGuideRegions =
            MusicTheoryAnalyzer.CreateChordGuideRegions(score);
        _theoryScaleCandidates =
            MusicTheoryAnalyzer.CreateScaleCandidates(score, 6);
        _theoryKeyCache.Clear();
        _lastChordSuggestionMeasureNumber = int.MinValue;
        _theoryPresentationScore = null;
        _theoryPresentationStartBeat = double.NaN;
        _theoryPresentationEndBeat = double.NaN;
    }

    private MusicKeyAnalysis? GetCachedKeyAnalysis(
        MusicScore score,
        double beat)
    {
        var activeKey = score.KeySignatureEvents
            .Where(item => item.Beat <= beat + ScoreTiming.EventBeatTolerance)
            .OrderByDescending(item => item.Beat)
            .FirstOrDefault();
        var cacheKey = activeKey is null
            ? long.MinValue
            : EditableMusicScore.BeatToTick(activeKey.Beat);
        if (_theoryKeyCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var analysis = MusicTheoryAnalyzer.AnalyzeKeyAt(score, beat);
        _theoryKeyCache[cacheKey] = analysis;
        return analysis;
    }

    private void RefreshScaleTonicChoices(
        string scaleId,
        int preferredFifths)
    {
        var definition =
            MusicTheoryAnalyzer.GetScaleDefinition(scaleId);
        if (definition is null || ScaleTonicComboBox is null)
        {
            return;
        }

        var choices = Enumerable.Range(-7, 15)
            .Select(fifths =>
            {
                var display =
                    MusicTheoryAnalyzer.GetScaleDisplayName(
                        fifths,
                        definition.Id);
                var suffix = " " + definition.DisplayName;
                var tonicLabel = display.EndsWith(
                        suffix,
                        StringComparison.Ordinal)
                    ? display[..^suffix.Length]
                    : display;
                return new ScaleTonicChoice(
                    fifths,
                    tonicLabel);
            })
            .GroupBy(choice => choice.Label, StringComparer.Ordinal)
            .Select(group =>
                group.OrderBy(choice => Math.Abs(choice.Fifths)).First())
            .OrderBy(choice => Math.Abs(choice.Fifths))
            .ThenBy(choice => choice.Fifths)
            .ToArray();

        ScaleTonicComboBox.ItemsSource = choices;
        ScaleTonicComboBox.SelectedItem =
            choices.FirstOrDefault(choice =>
                choice.Fifths == preferredFifths)
            ?? choices.FirstOrDefault();
    }

    private void RefreshChordSuggestions(
        MusicScore score,
        ScoreMeasure measure)
    {
        if (_lastChordSuggestionMeasureNumber == measure.Number)
        {
            return;
        }

        _lastChordSuggestionMeasureNumber = measure.Number;
        var currentText = ChordComboBox.Text;
        ChordComboBox.ItemsSource =
            MusicTheoryAnalyzer.CreateChordSuggestions(
                score,
                measure,
                16);
        if (ChordComboBox.IsKeyboardFocusWithin)
        {
            ChordComboBox.Text = currentText;
        }
    }

    private void ResetTheoryCache()
    {
        _theoryCacheScore = null;
        _theoryHarmonyTimeline =
            Array.Empty<HarmonyTimelineSegment>();
        _theoryScaleGuideRegions =
            Array.Empty<EditorScaleGuideRegion>();
        _theoryChordGuideRegions =
            Array.Empty<EditorChordGuideRegion>();
        _theoryScaleCandidates =
            Array.Empty<MusicScaleCandidate>();
        _theoryKeyCache.Clear();
        _lastChordSuggestionMeasureNumber = int.MinValue;
        _theoryPresentationScore = null;
        _theoryPresentationStartBeat = double.NaN;
        _theoryPresentationEndBeat = double.NaN;
    }

    private HarmonyTimelineSegment? GetCachedHarmonySegment(
        double beat)
    {
        var scoreLength = _theoryCacheScore?.LengthBeats ?? 0d;
        foreach (var item in _theoryHarmonyTimeline)
        {
            if (beat >= item.StartBeat - ScoreTiming.EventBeatTolerance
                && (beat < item.EndBeat - ScoreTiming.EventBeatTolerance
                    || Math.Abs(beat - item.EndBeat) <= ScoreTiming.EventBeatTolerance
                        && Math.Abs(item.EndBeat - scoreLength)
                            <= ScoreTiming.EventBeatTolerance))
            {
                return item;
            }
        }

        return null;
    }

    private ScoreMeasure RequireCurrentMeasure()
    {
        var score = RequireSession().Score.ToMusicScore();
        return score.GetMeasureAt(EditorRoll.CursorBeat)
            ?? throw new InvalidOperationException(
                "現在位置の小節を特定できません。");
    }

    private static string FormatHand(Hand hand)
        => hand == Hand.Right ? "右手" : "左手";

    private void RequestStopPreview(bool restoreCursor = true)
    {
        _previewPlaying = false;
        _previewPaused = false;
        PreviewButton.Content =
            "▶ 試聴  Space";
        StopPreviewRequested?.Invoke(
            this,
            EventArgs.Empty);
        EditorRoll.ClearPreviewBeat(
            restoreCursor);
    }

    private void RequestSave(bool saveAs)
    {
        var session = RequireSession();
        SaveRequested?.Invoke(
            this,
            new ScoreEditorSaveRequestedEventArgs(
                session.Score.ToMusicScore(),
                _difficulty,
                saveAs));
    }

    private void ApplySessionToView(string status)
    {
        if (_session is null)
        {
            return;
        }

        var score = _session.Score.ToMusicScore();
        ResetTheoryCache();
        EditorRoll.Score = score;
        EditorRoll.SelectedNoteIds = _selectedNoteIds.ToArray();
        RefreshReferenceAudioSynchronizer(score);
        TitleText.Text = _session.Score.Title;
        PathText.Text = _filePath;
        UndoButton.IsEnabled = _session.CanUndo;
        RedoButton.IsEnabled = _session.CanRedo;
        StatusText.Text = status;
        UpdateDirtyState();
        RefreshNoteInspector();
    }

    private void ApplySelection()
    {
        EditorRoll.SelectedNoteIds = _selectedNoteIds.ToArray();
        RefreshNoteInspector();
    }

    private string BuildWorkspaceContentSignature()
        => RequireSession().CurrentSignature
            + "|difficulty="
            + ((int)_difficulty).ToString(
                CultureInfo.InvariantCulture)
            + "|reference="
            + BuildReferenceAudioProjectSignature(
                _referenceAudioProject);

    private void UpdateDirtyState()
    {
        if (HasUnsavedWorkChanges)
        {
            DirtyText.Text =
                "● 作業未保存";
            DirtyText.Visibility =
                Visibility.Visible;
            return;
        }

        if (IsDirty)
        {
            DirtyText.Text =
                "● 作業保存済み / XML未反映";
            DirtyText.Visibility =
                Visibility.Visible;
            return;
        }

        DirtyText.Visibility =
            Visibility.Collapsed;
    }

    private void SetSelection(IEnumerable<int> ids)
    {
        _selectedNoteIds.Clear();
        foreach (var id in ids)
        {
            _selectedNoteIds.Add(id);
        }

        ApplySelection();
    }

    private void RemoveMissingSelections()
    {
        var existingIds = RequireSession().Score.Notes.Select(note => note.Id).ToHashSet();
        _selectedNoteIds.RemoveWhere(id => !existingIds.Contains(id));
    }

    private ScoreEditSession RequireSession()
        => _session ?? throw new InvalidOperationException("編集対象が設定されていません。");

    private long SnapTick(long tick)
    {
        var step = Math.Max(1L, _gridTicks);
        return checked((long)Math.Round(
            tick / (double)step,
            MidpointRounding.AwayFromZero) * step);
    }

    private static Rect CreateRectangle(Point start, Point end)
        => new(
            new Point(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y)),
            new Point(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y)));

    private static bool TryParseBeat(string value, out double beat)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out beat)
            || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out beat);

    private static string FormatOctaveShift(int semitones)
        => semitones == 0
            ? "なし"
            : $"{semitones / 12:+#;-#;0} oct";

    private enum MeasurePropertyPopupKind
    {
        None,
        TimeSignature,
        Tempo
    }

    private sealed record PreviewMeasureChoice(int MeasureCount, string Label);

    private sealed record ReferencePlaybackChoice(
        EditorReferencePlaybackMode Mode,
        string Label);

    private sealed record OctaveShiftChoice(int Semitones, string Label);

    private sealed record GridChoice(string Label, long Ticks);

    private sealed record ScaleTonicChoice(
        int Fifths,
        string Label);

    private sealed record DifficultyChoice(SongDifficulty Difficulty, string Label);

    private sealed record NoteOriginal(long StartTick, long DurationTick, int MidiNote);

    private sealed record NoteDragState(
        Point StartPoint,
        double StartBeat,
        int StartMidi,
        bool Resize,
        IReadOnlyDictionary<int, NoteOriginal> Originals);

    private sealed record SelectionDragState(Point StartPoint, bool Additive);
}
