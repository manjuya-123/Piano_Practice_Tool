using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PianoPracticeTool.Controls;
using PianoPracticeTool.Core;
using PianoPracticeTool.Core.Editing;

namespace PianoPracticeTool.Views;

public sealed partial class ScoreEditorView
{
    private const int PitchPanSemitones = 6;

    private bool _hasPendingInsertPoint;
    private long _pendingInsertTick;
    private int _pendingInsertMidi;
    private double _pendingInsertViewportBeat;
    private EditorPreviewBoundary? _previewBoundaryDrag;

    public void SetConnectedKeyboardRange(PracticeMidiRange? range)
        => EditorRoll.ConnectedKeyboardRange = range;

    private void PitchPanLowerButton_Click(object sender, RoutedEventArgs e)
    {
        EditorRoll.PanPitchViewport(-PitchPanSemitones);
        StatusText.Text = $"表示音域 {GetVisiblePitchRangeText()}";
    }

    private void PitchPanHigherButton_Click(object sender, RoutedEventArgs e)
    {
        EditorRoll.PanPitchViewport(PitchPanSemitones);
        StatusText.Text = $"表示音域 {GetVisiblePitchRangeText()}";
    }

    private void PitchZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        EditorRoll.ZoomPitchViewport(zoomIn: false);
        StatusText.Text = $"表示音域 {GetVisiblePitchRangeText()}";
    }

    private void PitchZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        EditorRoll.ZoomPitchViewport(zoomIn: true);
        StatusText.Text = $"表示音域 {GetVisiblePitchRangeText()}";
    }

    private void PitchResetButton_Click(object sender, RoutedEventArgs e)
    {
        EditorRoll.ResetPitchViewport();
        StatusText.Text = $"表示音域を全体へ戻しました: {GetVisiblePitchRangeText()}";
    }

    private void EditorRoll_TimelineViewportChanged(object? sender, EventArgs e)
    {
        SyncMeasureLane();
        UpdateTheoryPresentation();
    }

    private void MeasureLane_CursorBeatRequested(
        object? sender,
        EditorTimelineCursorRequestedEventArgs e)
    {
        var beat = e.Kind == EditorTimelineCursorRequestKind.Click
            ? EditableMusicScore.TickToBeat(
                SnapTick(EditableMusicScore.BeatToTick(e.Beat)))
            : e.Beat;
        SetTimelineCursorBeat(
            beat,
            showStatus:
                e.Kind == EditorTimelineCursorRequestKind.Click);
    }

    private void SetTimelineCursorBeat(
        double beat,
        bool showStatus)
    {
        if (_previewPlaying || _previewPaused)
        {
            RequestStopPreview(
                restoreCursor: false);
        }

        EditorRoll.CursorBeat = beat;
        if (showStatus)
        {
            StatusText.Text =
                $"現在位置 {EditorRoll.CursorBeat:0.###} beat";
        }

        RestoreEditorInputFocusAfterNavigation();
    }

    private string GetVisiblePitchRangeText()
    {
        var range = EditorRoll.VisiblePitchRange;
        return $"{MidiPitch.ToName(range.LowestMidi)}–{MidiPitch.ToName(range.HighestMidi)}";
    }

    private void HandleNavigationKeyDown(KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        var control =
            Keyboard.Modifiers.HasFlag(
                ModifierKeys.Control);
        var shift =
            Keyboard.Modifiers.HasFlag(
                ModifierKeys.Shift);
        if (Keyboard.Modifiers == ModifierKeys.None
            && e.Key is Key.Home or Key.End)
        {
            var targetBeat =
                e.Key == Key.Home
                    ? 0d
                    : RequireSession()
                        .Score
                        .ToMusicScore()
                        .LengthBeats;
            SetTimelineCursorBeat(
                targetBeat,
                showStatus: true);
            e.Handled = true;
            return;
        }

        if (control && e.Key == Key.A)
        {
            SetSelection(
                RequireSession()
                    .Score
                    .Notes
                    .Select(note =>
                        note.Id));
            StatusText.Text =
                $"{_selectedNoteIds.Count} ノートをすべて選択しました。";
            e.Handled = true;
            return;
        }

        if (e.Key is not (
                Key.Left
                or Key.Right
                or Key.Up
                or Key.Down))
        {
            return;
        }

        if (_selectedNoteIds.Count == 0)
        {
            if (e.Key is Key.Up or Key.Down)
            {
                var deltaBeat =
                    EditableMusicScore.TickToBeat(
                        _gridTicks)
                    * (e.Key == Key.Up
                        ? 1d
                        : -1d);
                SetTimelineCursorBeat(
                    EditorRoll.CursorBeat
                    + deltaBeat,
                    showStatus: true);
            }

            e.Handled = true;
            return;
        }

        try
        {
            var session =
                RequireSession();
            if (shift
                && !control
                && e.Key is Key.Up or Key.Down)
            {
                var durationDelta =
                    e.Key == Key.Up
                        ? _gridTicks
                        : -_gridTicks;
                session.ResizeNotes(
                    _selectedNoteIds,
                    durationDelta);
                ApplySessionToView(
                    $"選択ノート長を {durationDelta:+#;-#;0} tick 変更しました。");
            }
            else if (shift
                     && !control
                     && e.Key is Key.Left or Key.Right)
            {
                var deltaMidi =
                    e.Key == Key.Right
                        ? 12
                        : -12;
                session.MoveNotes(
                    _selectedNoteIds,
                    0L,
                    deltaMidi);
                ApplySessionToView(
                    deltaMidi > 0
                        ? "選択ノートを1オクターブ上げました。"
                        : "選択ノートを1オクターブ下げました。");
            }
            else if (control
                     && !shift
                     && e.Key is Key.Left or Key.Right)
            {
                var direction =
                    e.Key == Key.Right
                        ? 1
                        : -1;
                session.MoveNotesByScaleStep(
                    _selectedNoteIds,
                    direction);
                ApplySessionToView(
                    direction > 0
                        ? "選択ノートをScale内で1段上げました。"
                        : "選択ノートをScale内で1段下げました。");
            }
            else
            {
                var deltaTicks =
                    e.Key switch
                    {
                        Key.Up =>
                            _gridTicks,
                        Key.Down =>
                            -_gridTicks,
                        _ =>
                            0L
                    };
                var deltaMidi =
                    e.Key switch
                    {
                        Key.Right =>
                            1,
                        Key.Left =>
                            -1,
                        _ =>
                            0
                    };
                session.MoveNotes(
                    _selectedNoteIds,
                    deltaTicks,
                    deltaMidi);
                ApplySessionToView(
                    deltaMidi != 0
                        ? $"選択ノートを {deltaMidi:+#;-#;0} 半音移動しました。"
                        : $"選択ノートを {deltaTicks:+#;-#;0} tick 移動しました。");
            }
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text =
                ex.Message;
        }

        e.Handled = true;
    }

    private void UpdatePreviewBoundaryFromPoint(Point point)
    {
        if (_previewBoundaryDrag is not EditorPreviewBoundary boundary || _session is null)
        {
            return;
        }

        var scoreLength = _session.Score.ToMusicScore().LengthBeats;
        var tick = SnapTick(EditableMusicScore.BeatToTick(EditorRoll.PointToBeat(point)));
        var beat = Math.Clamp(EditableMusicScore.TickToBeat(tick), 0d, scoreLength);
        var minimumGap = EditableMusicScore.TickToBeat(Math.Max(1L, _gridTicks));

        if (boundary == EditorPreviewBoundary.Start)
        {
            var maximumStart = Math.Max(0d, _previewEndBeat - minimumGap);
            _previewStartBeat = Math.Min(beat, maximumStart);
            UpdatePreviewRangePresentation();
            StatusText.Text = $"試聴開始 A = {_previewStartBeat:0.###} beat";
        }
        else
        {
            var minimumEnd = Math.Min(scoreLength, _previewStartBeat + minimumGap);
            _previewEndBeat = Math.Max(beat, minimumEnd);
            UpdatePreviewRangePresentation();
            StatusText.Text = $"試聴終了 B = {_previewEndBeat:0.###} beat";
        }
    }

    private void InsertNoteAtStablePosition(long requestedTick, int midi)
    {
        var session = RequireSession();
        var duration = Math.Max(EditableMusicScore.MinimumDurationTicks, _gridTicks);
        var maximumStartTick = Math.Max(0L, session.Score.LengthTicks - duration);
        var tick = Math.Clamp(requestedTick, 0L, maximumStartTick);

        try
        {
            var id = session.InsertNote(midi, tick, duration, _insertHand);
            _selectedNoteIds.Clear();
            _selectedNoteIds.Add(id);
            ApplySessionToView(
                $"{FormatHand(_insertHand)} {MidiPitch.ToName(midi)} を {EditableMusicScore.TickToBeat(tick):0.###} beat に挿入しました。");
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
        }
    }
}
