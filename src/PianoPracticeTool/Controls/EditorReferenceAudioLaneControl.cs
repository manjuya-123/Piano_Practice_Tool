using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PianoPracticeTool.Core;
using PianoPracticeTool.Core.Editing;
using PianoPracticeTool.Services.Editor;

namespace PianoPracticeTool.Controls;

public sealed class EditorReferenceAudioSeekRequestedEventArgs : EventArgs
{
    public EditorReferenceAudioSeekRequestedEventArgs(
        double scoreBeat,
        double audioSeconds)
    {
        ScoreBeat = scoreBeat;
        AudioSeconds = audioSeconds;
    }

    public double ScoreBeat { get; }

    public double AudioSeconds { get; }
}

public sealed class EditorReferenceAudioPlayRequestedEventArgs : EventArgs
{
    public EditorReferenceAudioPlayRequestedEventArgs(
        double scoreBeat,
        double audioSeconds)
    {
        ScoreBeat = scoreBeat;
        AudioSeconds = audioSeconds;
    }

    public double ScoreBeat { get; }

    public double AudioSeconds { get; }
}

public sealed class EditorReferenceAudioSyncPointAddRequestedEventArgs : EventArgs
{
    public EditorReferenceAudioSyncPointAddRequestedEventArgs(
        double scoreBeat,
        double audioSeconds)
    {
        ScoreBeat = scoreBeat;
        AudioSeconds = audioSeconds;
    }

    public double ScoreBeat { get; }

    public double AudioSeconds { get; }
}

public sealed class EditorReferenceAudioSyncPointEventArgs : EventArgs
{
    public EditorReferenceAudioSyncPointEventArgs(
        ReferenceAudioSyncPoint point)
    {
        Point = point;
    }

    public ReferenceAudioSyncPoint Point { get; }
}

public sealed class EditorReferenceAudioSyncPointMoveRequestedEventArgs : EventArgs
{
    public EditorReferenceAudioSyncPointMoveRequestedEventArgs(
        ReferenceAudioSyncPoint point,
        double newScoreBeat)
    {
        Point = point;
        NewScoreBeat = newScoreBeat;
    }

    public ReferenceAudioSyncPoint Point { get; }

    public double NewScoreBeat { get; }
}

public sealed class EditorReferenceAudioSegmentAdjustRequestedEventArgs : EventArgs
{
    public EditorReferenceAudioSegmentAdjustRequestedEventArgs(
        double scoreBeat,
        double deltaSeconds)
    {
        ScoreBeat = scoreBeat;
        DeltaSeconds = deltaSeconds;
    }

    public double ScoreBeat { get; }

    public double DeltaSeconds { get; }
}

public sealed class EditorReferenceAudioLaneControl : FrameworkElement
{
    private const double SyncHandleRadius = 4.5d;
    private const double SyncHitRadius = 9d;
    private const double DragThresholdPixels = 2d;

    private static readonly Brush BackgroundBrush =
        CreateBrush(Color.FromRgb(10, 15, 22));
    private static readonly Brush FooterBrush =
        CreateBrush(Color.FromRgb(14, 20, 29));
    private static readonly Brush WaveformBrush =
        CreateBrush(Color.FromRgb(111, 208, 255));
    private static readonly Brush SyncBrush =
        CreateBrush(Color.FromRgb(255, 209, 102));
    private static readonly Brush SelectedSyncBrush =
        CreateBrush(Color.FromRgb(255, 245, 194));
    private static readonly Brush GapBrush =
        CreateBrush(Color.FromArgb(70, 255, 154, 92));
    private static readonly Brush TextBrush =
        CreateBrush(Color.FromRgb(199, 210, 225));
    private static readonly Brush MutedTextBrush =
        CreateBrush(Color.FromRgb(135, 146, 163));
    private static readonly Brush SubdivisionBrush =
        CreateBrush(Color.FromArgb(14, 255, 255, 255));
    private static readonly Brush BeatBrush =
        CreateBrush(Color.FromArgb(28, 255, 255, 255));
    private static readonly Brush MeasureBrush =
        CreateBrush(Color.FromArgb(82, 255, 255, 255));
    private static readonly Pen WaveformPen =
        CreatePen(WaveformBrush, 1d);
    private static readonly Pen ScoreCursorPen =
        CreatePen(
            CreateBrush(Color.FromRgb(238, 241, 247)),
            2d);
    private static readonly Pen AudioCursorPen =
        CreatePen(WaveformBrush, 1.8d);
    private static readonly Pen SyncPen =
        CreatePen(SyncBrush, 1.2d);
    private static readonly Pen SelectedSyncPen =
        CreatePen(SelectedSyncBrush, 2d);
    private static readonly Pen SubdivisionPen =
        CreatePen(SubdivisionBrush, 1d);
    private static readonly Pen BeatPen =
        CreatePen(BeatBrush, 1d);
    private static readonly Pen MeasurePen =
        CreatePen(MeasureBrush, 1.4d);
    private static readonly Pen FooterBorderPen =
        CreatePen(
            CreateBrush(Color.FromArgb(80, 255, 255, 255)),
            1d);
    private static readonly Typeface LabelTypeface =
        new("Segoe UI");

    private MusicScore? _score;
    private ReferenceAudioSynchronizer? _synchronizer;
    private ReferenceAudioWaveform? _waveform;
    private double _cursorBeat;
    private double _visibleBeats = 16d;
    private long _gridTicks = 240L;
    private double? _audioPositionSeconds;
    private ReferenceAudioSyncPoint? _selectedSyncPoint;
    private ReferenceAudioSyncPoint? _dragSyncPoint;
    private double? _dragPreviewScoreBeat;
    private Point _dragStartPoint;
    private bool _audioSegmentAdjustDragActive;
    private Point _audioSegmentAdjustDragStartPoint;
    private double _audioSegmentAdjustDragStartBeat;
    private double _audioSegmentAdjustDragDeltaSeconds;
    private ScoreTimeline? _audioSegmentAdjustDragTimeline;
    private ReferenceAudioSynchronizer? _audioSegmentAdjustPreviewSynchronizer;

    public event EventHandler<EditorReferenceAudioSeekRequestedEventArgs>? SeekRequested;

    public event EventHandler<EditorReferenceAudioPlayRequestedEventArgs>? PlayRequested;

    public event EventHandler<EditorReferenceAudioSyncPointAddRequestedEventArgs>? SyncPointAddRequested;

    public event EventHandler<EditorReferenceAudioSyncPointEventArgs>? SyncPointSelected;

    public event EventHandler<EditorReferenceAudioSyncPointMoveRequestedEventArgs>? SyncPointMoveRequested;

    public event EventHandler<EditorReferenceAudioSyncPointEventArgs>? SyncPointDeleteRequested;

    public event EventHandler<EditorReferenceAudioSegmentAdjustRequestedEventArgs>? AudioSegmentAdjustRequested;

    public MusicScore? Score
    {
        get => _score;
        set
        {
            if (ReferenceEquals(_score, value))
            {
                return;
            }

            _score = value;
            InvalidateVisual();
        }
    }

    public ReferenceAudioSynchronizer? Synchronizer
    {
        get => _synchronizer;
        set
        {
            if (ReferenceEquals(
                    _synchronizer,
                    value))
            {
                return;
            }

            _synchronizer = value;
            if (_selectedSyncPoint is not null
                && !ContainsSyncPoint(
                    _selectedSyncPoint))
            {
                _selectedSyncPoint = null;
            }

            InvalidateVisual();
        }
    }

    public ReferenceAudioWaveform? Waveform
    {
        get => _waveform;
        set
        {
            if (ReferenceEquals(
                    _waveform,
                    value))
            {
                return;
            }

            _waveform = value;
            InvalidateVisual();
        }
    }

    public double CursorBeat
    {
        get => _cursorBeat;
        set
        {
            if (Math.Abs(
                    _cursorBeat - value)
                <= ScoreTiming.EventBeatTolerance)
            {
                return;
            }

            _cursorBeat = value;
            InvalidateVisual();
        }
    }

    public double VisibleBeats
    {
        get => _visibleBeats;
        set
        {
            var bounded = Math.Clamp(
                value,
                4d,
                64d);
            if (Math.Abs(
                    _visibleBeats - bounded)
                <= ScoreTiming.EventBeatTolerance)
            {
                return;
            }

            _visibleBeats = bounded;
            InvalidateVisual();
        }
    }

    public long GridTicks
    {
        get => _gridTicks;
        set
        {
            var bounded =
                Math.Max(
                    1L,
                    value);
            if (_gridTicks == bounded)
            {
                return;
            }

            _gridTicks = bounded;
            InvalidateVisual();
        }
    }

    public double? AudioPositionSeconds
    {
        get => _audioPositionSeconds;
        set
        {
            if (_audioPositionSeconds == value)
            {
                return;
            }

            _audioPositionSeconds = value;
            InvalidateVisual();
        }
    }

    public ReferenceAudioSyncPoint? SelectedSyncPoint
    {
        get => _selectedSyncPoint;
        set
        {
            if (ReferenceSyncPointEquals(
                    _selectedSyncPoint,
                    value))
            {
                return;
            }

            _selectedSyncPoint = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(
        DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var width = ActualWidth;
        var rollHeight = GetRollHeight();

        drawingContext.DrawRectangle(
            BackgroundBrush,
            null,
            new Rect(
                0d,
                0d,
                width,
                ActualHeight));

        if (_score is not null
            && DisplaySynchronizer is not null
            && _waveform is not null
            && _waveform.Peaks.Count > 0
            && width > 2d
            && rollHeight > 2d)
        {
            drawingContext.PushClip(
                new RectangleGeometry(
                    new Rect(
                        0d,
                        0d,
                        width,
                        rollHeight)));
            DrawTimelineGrid(
                drawingContext,
                width,
                rollHeight);
            DrawWaveform(
                drawingContext,
                width,
                rollHeight);
            DrawGaps(
                drawingContext,
                width,
                rollHeight);
            DrawSyncPoints(
                drawingContext,
                width,
                rollHeight);
            DrawScoreCursor(
                drawingContext,
                width,
                rollHeight);
            DrawAudioCursor(
                drawingContext,
                width,
                rollHeight);
            drawingContext.Pop();
        }

        DrawFooter(
            drawingContext,
            width,
            rollHeight);
    }

    protected override void OnMouseLeftButtonDown(
        MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!CanInteract())
        {
            return;
        }

        Focus();
        var point = e.GetPosition(this);
        var rollHeight = GetRollHeight();
        if (point.Y < 0d
            || point.Y > rollHeight)
        {
            return;
        }

        if (Keyboard.Modifiers
            == ModifierKeys.Shift)
        {
            BeginAudioSegmentAdjustDrag(
                point);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(
                ModifierKeys.Alt))
        {
            var scoreBeat =
                SnapBeatToGrid(
                    BeatAtY(
                        point.Y,
                        rollHeight));
            var audioSeconds =
                GetAudioSecondsAtPoint(
                    point,
                    scoreBeat,
                    rollHeight);
            SyncPointAddRequested?.Invoke(
                this,
                new EditorReferenceAudioSyncPointAddRequestedEventArgs(
                    scoreBeat,
                    audioSeconds));
            e.Handled = true;
            return;
        }

        var hit = HitTestSyncPoint(
            point,
            rollHeight);
        if (hit is not null)
        {
            SelectSyncPoint(
                hit,
                seekToPoint: true);

            if (e.ClickCount >= 2)
            {
                PlayRequested?.Invoke(
                    this,
                    new EditorReferenceAudioPlayRequestedEventArgs(
                        hit.ScoreBeat,
                        hit.AudioSeconds));
                e.Handled = true;
                return;
            }

            if (IsFixedOuterAnchor(
                    hit))
            {
                e.Handled = true;
                return;
            }

            _dragSyncPoint = hit;
            _dragPreviewScoreBeat =
                hit.ScoreBeat;
            _dragStartPoint = point;
            CaptureMouse();
            Cursor = Cursors.SizeNS;
            e.Handled = true;
            return;
        }

        SelectedSyncPoint = null;
        var beat = BeatAtY(
            point.Y,
            rollHeight);
        var seconds =
            GetAudioSecondsAtPoint(
                point,
                beat,
                rollHeight);
        SeekRequested?.Invoke(
            this,
            new EditorReferenceAudioSeekRequestedEventArgs(
                beat,
                seconds));

        if (e.ClickCount >= 2)
        {
            PlayRequested?.Invoke(
                this,
                new EditorReferenceAudioPlayRequestedEventArgs(
                    beat,
                    seconds));
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(
        MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        var rollHeight = GetRollHeight();

        if (_audioSegmentAdjustDragActive
            && IsMouseCaptured)
        {
            UpdateAudioSegmentAdjustDrag(
                point,
                rollHeight);
            Cursor = Cursors.SizeNS;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_dragSyncPoint is not null
            && IsMouseCaptured)
        {
            _dragPreviewScoreBeat =
                BeatAtY(
                    point.Y,
                    rollHeight);
            Cursor = Cursors.SizeNS;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (!CanInteract()
            || point.Y < 0d
            || point.Y > rollHeight)
        {
            Cursor = Cursors.Arrow;
            return;
        }

        if (Keyboard.Modifiers
            == ModifierKeys.Shift)
        {
            Cursor = Cursors.SizeNS;
        }
        else if (HitTestSyncPoint(
                     point,
                     rollHeight)
                 is ReferenceAudioSyncPoint syncPoint)
        {
            Cursor = IsFixedOuterAnchor(
                    syncPoint)
                ? Cursors.Hand
                : Cursors.SizeNS;
        }
        else if (Keyboard.Modifiers.HasFlag(
                     ModifierKeys.Alt))
        {
            Cursor = Cursors.Cross;
        }
        else
        {
            Cursor = Cursors.Hand;
        }
    }

    protected override void OnMouseLeftButtonUp(
        MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_audioSegmentAdjustDragActive)
        {
            CompleteAudioSegmentAdjustDrag(
                e.GetPosition(this));
            e.Handled = true;
            return;
        }

        if (_dragSyncPoint is null)
        {
            return;
        }

        var draggedPoint =
            _dragSyncPoint;
        var targetBeat =
            _dragPreviewScoreBeat
            ?? draggedPoint.ScoreBeat;
        var point =
            e.GetPosition(this);
        var moved =
            Math.Abs(
                point.Y
                - _dragStartPoint.Y)
            >= DragThresholdPixels;

        _dragSyncPoint = null;
        _dragPreviewScoreBeat = null;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        Cursor = Cursors.Arrow;
        InvalidateVisual();

        if (moved
            && Math.Abs(
                targetBeat
                - draggedPoint.ScoreBeat)
                > ScoreTiming.EventBeatTolerance)
        {
            SyncPointMoveRequested?.Invoke(
                this,
                new EditorReferenceAudioSyncPointMoveRequestedEventArgs(
                    draggedPoint,
                    targetBeat));
        }

        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(
        MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (!CanInteract())
        {
            return;
        }

        var point = e.GetPosition(this);
        var rollHeight = GetRollHeight();
        var hit = HitTestSyncPoint(
            point,
            rollHeight);
        if (hit is null)
        {
            return;
        }

        Focus();
        SelectSyncPoint(
            hit,
            seekToPoint: true);
        if (!IsFixedOuterAnchor(
                hit))
        {
            OpenSyncPointContextMenu(
                hit);
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(
        KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Delete
            && _selectedSyncPoint is not null
            && !IsFixedOuterAnchor(
                _selectedSyncPoint))
        {
            SyncPointDeleteRequested?.Invoke(
                this,
                new EditorReferenceAudioSyncPointEventArgs(
                    _selectedSyncPoint));
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(
        MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragSyncPoint = null;
        _dragPreviewScoreBeat = null;
        ResetAudioSegmentAdjustDrag();
        InvalidateVisual();
    }

    private void BeginAudioSegmentAdjustDrag(
        Point point)
    {
        _audioSegmentAdjustDragActive = true;
        _audioSegmentAdjustDragStartPoint =
            point;
        _audioSegmentAdjustDragStartBeat =
            BeatAtY(
                point.Y,
                GetRollHeight());
        _audioSegmentAdjustDragDeltaSeconds =
            0d;
        _audioSegmentAdjustDragTimeline =
            new ScoreTimeline(
                _score!);
        _audioSegmentAdjustPreviewSynchronizer =
            _synchronizer;
        SelectedSyncPoint = null;
        CaptureMouse();
        Cursor = Cursors.SizeNS;
    }

    private void UpdateAudioSegmentAdjustDrag(
        Point point,
        double rollHeight)
    {
        if (!_audioSegmentAdjustDragActive
            || _audioSegmentAdjustDragTimeline is null
            || _score is null
            || _synchronizer is null
            || rollHeight <= 1d)
        {
            return;
        }

        var beatDelta =
            (_audioSegmentAdjustDragStartPoint.Y
             - point.Y)
            * (_visibleBeats
               / rollHeight);
        var targetBeat =
            _audioSegmentAdjustDragStartBeat
            + beatDelta;
        var scoreDeltaSeconds =
            _audioSegmentAdjustDragTimeline
                .BeatToSeconds(
                    targetBeat)
            - _audioSegmentAdjustDragTimeline
                .BeatToSeconds(
                    _audioSegmentAdjustDragStartBeat);
        _audioSegmentAdjustDragDeltaSeconds =
            -scoreDeltaSeconds;

        var shiftedPoints =
            ReferenceAudioSynchronizer
                .AdjustAudioSegment(
                    _score,
                    _synchronizer.SyncPoints,
                    _waveform!.DurationSeconds,
                    _audioSegmentAdjustDragStartBeat,
                    _audioSegmentAdjustDragDeltaSeconds);
        _audioSegmentAdjustPreviewSynchronizer =
            new ReferenceAudioSynchronizer(
                _score,
                shiftedPoints);
    }

    private void CompleteAudioSegmentAdjustDrag(
        Point point)
    {
        var moved =
            Math.Abs(
                point.Y
                - _audioSegmentAdjustDragStartPoint.Y)
            >= DragThresholdPixels;
        var scoreBeat =
            _audioSegmentAdjustDragStartBeat;
        var deltaSeconds =
            _audioSegmentAdjustDragDeltaSeconds;

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        ResetAudioSegmentAdjustDrag();
        Cursor = Cursors.Arrow;
        InvalidateVisual();

        if (moved
            && Math.Abs(deltaSeconds)
                > ScoreTiming.EventBeatTolerance)
        {
            AudioSegmentAdjustRequested?.Invoke(
                this,
                new EditorReferenceAudioSegmentAdjustRequestedEventArgs(
                    scoreBeat,
                    deltaSeconds));
        }
    }

    private void ResetAudioSegmentAdjustDrag()
    {
        _audioSegmentAdjustDragActive =
            false;
        _audioSegmentAdjustDragDeltaSeconds =
            0d;
        _audioSegmentAdjustDragTimeline =
            null;
        _audioSegmentAdjustPreviewSynchronizer =
            null;
    }

    private ReferenceAudioSynchronizer? DisplaySynchronizer =>
        _audioSegmentAdjustPreviewSynchronizer
        ?? _synchronizer;

    private bool IsFixedOuterAnchor(
        ReferenceAudioSyncPoint point)
    {
        if (_score is null
            || _waveform is null)
        {
            return false;
        }

        var isSourceStart =
            Math.Abs(
                point.ScoreBeat)
                <= ScoreTiming.EventBeatTolerance
            && point.AudioSeconds
                <= ScoreTiming.EventBeatTolerance;
        var isSourceEnd =
            Math.Abs(
                point.ScoreBeat
                - _score.LengthBeats)
                <= ScoreTiming.EventBeatTolerance
            && Math.Abs(
                point.AudioSeconds
                - _waveform.DurationSeconds)
                <= 0.001d;
        return isSourceStart
               || isSourceEnd;
    }

    private bool CanInteract()
        => _score is not null
           && _synchronizer is not null
           && _waveform is not null;

    private void SelectSyncPoint(
        ReferenceAudioSyncPoint point,
        bool seekToPoint)
    {
        SelectedSyncPoint = point;
        if (seekToPoint)
        {
            SeekRequested?.Invoke(
                this,
                new EditorReferenceAudioSeekRequestedEventArgs(
                    point.ScoreBeat,
                    point.AudioSeconds));
        }

        SyncPointSelected?.Invoke(
            this,
            new EditorReferenceAudioSyncPointEventArgs(
                point));
    }

    private void OpenSyncPointContextMenu(
        ReferenceAudioSyncPoint point)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = this,
            Placement = PlacementMode.MousePoint
        };
        var deleteItem = new MenuItem
        {
            Header = "この同期ポイントを削除"
        };
        deleteItem.Click += (_, _) =>
            SyncPointDeleteRequested?.Invoke(
                this,
                new EditorReferenceAudioSyncPointEventArgs(
                    point));
        menu.Items.Add(deleteItem);
        menu.Closed += (_, _) =>
            Focus();
        menu.IsOpen = true;
    }

    private void DrawTimelineGrid(
        DrawingContext context,
        double width,
        double rollHeight)
    {
        if (_score is null)
        {
            return;
        }

        var cursorY =
            rollHeight
            * EditorPianoRollControl.CursorVerticalRatio;
        var pixelsPerBeat =
            rollHeight
            / _visibleBeats;
        var topBeat = Math.Clamp(
            _cursorBeat
            + cursorY / pixelsPerBeat,
            0d,
            _score.LengthBeats);
        var bottomBeat = Math.Clamp(
            _cursorBeat
            - (rollHeight - cursorY)
            / pixelsPerBeat,
            0d,
            _score.LengthBeats);

        var gridStep = Math.Max(
            EditableMusicScore.TickToBeat(1L),
            EditableMusicScore.TickToBeat(
                _gridTicks));
        var firstGridIndex =
            (long)Math.Floor(
                bottomBeat / gridStep);
        var lastGridIndex =
            (long)Math.Ceiling(
                topBeat / gridStep);
        for (var index = firstGridIndex;
             index <= lastGridIndex;
             index++)
        {
            var beat =
                index * gridStep;
            var nearestWholeBeat =
                Math.Round(
                    beat);
            if (Math.Abs(
                    beat
                    - nearestWholeBeat)
                <= ScoreTiming.EventBeatTolerance)
            {
                continue;
            }

            var gridY = BeatToY(
                beat,
                rollHeight);
            context.DrawLine(
                SubdivisionPen,
                new Point(0d, gridY),
                new Point(width, gridY));
        }

        var firstBeat =
            (int)Math.Floor(
                bottomBeat);
        var lastBeat =
            (int)Math.Ceiling(
                topBeat);
        for (var beat = firstBeat;
             beat <= lastBeat;
             beat++)
        {
            var y = BeatToY(
                beat,
                rollHeight);
            context.DrawLine(
                BeatPen,
                new Point(0d, y),
                new Point(width, y));
        }

        foreach (var measure
                 in _score.Measures)
        {
            if (measure.StartBeat
                    < bottomBeat
                    - ScoreTiming.EventBeatTolerance
                || measure.StartBeat
                    > topBeat
                    + ScoreTiming.EventBeatTolerance)
            {
                continue;
            }

            var y = BeatToY(
                measure.StartBeat,
                rollHeight);
            context.DrawLine(
                MeasurePen,
                new Point(0d, y),
                new Point(width, y));
        }
    }

    private void DrawWaveform(
        DrawingContext context,
        double width,
        double rollHeight)
    {
        var center =
            width / 2d;
        var half = Math.Max(
            4d,
            center - 7d);
        const double step = 2d;
        var synchronizer =
            DisplaySynchronizer!;

        for (var y = 0d;
             y < rollHeight;
             y += step)
        {
            var beat = BeatAtY(
                y,
                rollHeight);
            if (beat < 0d
                || beat > _score!.LengthBeats)
            {
                continue;
            }

            var seconds =
                synchronizer
                    .ScoreBeatToAudioSeconds(
                        beat);
            var amplitude =
                Math.Clamp(
                    GetPeakAt(
                        seconds),
                    0f,
                    1f)
                * half;
            context.DrawLine(
                WaveformPen,
                new Point(
                    center - amplitude,
                    y),
                new Point(
                    center + amplitude,
                    y));
        }
    }

    private void DrawSyncPoints(
        DrawingContext context,
        double width,
        double rollHeight)
    {
        foreach (var point
                 in DisplaySynchronizer!.SyncPoints)
        {
            var displayBeat =
                GetDisplayBeat(point);
            var y = BeatToY(
                displayBeat,
                rollHeight);
            if (y < -SyncHitRadius
                || y > rollHeight
                    + SyncHitRadius)
            {
                continue;
            }

            var selected =
                ReferenceSyncPointEquals(
                    _selectedSyncPoint,
                    point);
            var pen =
                selected
                    ? SelectedSyncPen
                    : SyncPen;
            var brush =
                selected
                    ? SelectedSyncBrush
                    : SyncBrush;
            context.DrawLine(
                pen,
                new Point(0d, y),
                new Point(width, y));

            var centerX =
                GetSyncHandleCenterX(
                    point,
                    width);
            context.DrawEllipse(
                selected
                    ? SyncBrush
                    : BackgroundBrush,
                pen,
                new Point(
                    centerX,
                    y),
                SyncHandleRadius,
                SyncHandleRadius);

            if (selected)
            {
                DrawSelectedSyncLabel(
                    context,
                    point,
                    y,
                    width);
            }
        }
    }

    private void DrawSelectedSyncLabel(
        DrawingContext context,
        ReferenceAudioSyncPoint point,
        double y,
        double width)
    {
        var text = new FormattedText(
            $"{point.AudioSeconds:0.000}s",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            9d,
            SelectedSyncBrush,
            VisualTreeHelper.GetDpi(this)
                .PixelsPerDip);
        var x = Math.Max(
            2d,
            width
            - text.Width
            - 4d);
        var top = Math.Clamp(
            y
            - text.Height
            - 5d,
            1d,
            Math.Max(
                1d,
                GetRollHeight()
                - text.Height
                - 1d));
        context.DrawText(
            text,
            new Point(
                x,
                top));
    }

    private void DrawGaps(
        DrawingContext context,
        double width,
        double rollHeight)
    {
        foreach (var gap
                 in DisplaySynchronizer!.AudioOnlyGaps)
        {
            var y = BeatToY(
                gap.ScoreBeat,
                rollHeight);
            if (y < -18d
                || y > rollHeight
                    + 18d)
            {
                continue;
            }

            context.DrawRectangle(
                GapBrush,
                null,
                new Rect(
                    0d,
                    y - 9d,
                    width,
                    18d));
            var text = new FormattedText(
                $"カット {gap.DurationSeconds:0.#}s",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                8.5d,
                SyncBrush,
                VisualTreeHelper.GetDpi(this)
                    .PixelsPerDip);
            context.DrawText(
                text,
                new Point(
                    Math.Max(
                        2d,
                        (width - text.Width)
                        / 2d),
                    y - text.Height / 2d));
        }
    }

    private static void DrawScoreCursor(
        DrawingContext context,
        double width,
        double rollHeight)
    {
        var y =
            rollHeight
            * EditorPianoRollControl
                .CursorVerticalRatio;
        context.DrawLine(
            ScoreCursorPen,
            new Point(0d, y),
            new Point(width, y));
    }

    private void DrawAudioCursor(
        DrawingContext context,
        double width,
        double rollHeight)
    {
        if (_audioPositionSeconds
            is not double audioSeconds)
        {
            return;
        }

        var gap = FindAudioOnlyGap(
            audioSeconds);
        if (gap is not null)
        {
            var y = BeatToY(
                gap.ScoreBeat,
                rollHeight);
            if (y < 0d
                || y > rollHeight)
            {
                return;
            }

            var ratio = gap.DurationSeconds <= 0d
                ? 0d
                : Math.Clamp(
                    (audioSeconds
                     - gap.StartAudioSeconds)
                    / gap.DurationSeconds,
                    0d,
                    1d);
            var x = Math.Clamp(
                ratio * width,
                2d,
                Math.Max(
                    2d,
                    width - 2d));
            context.DrawLine(
                AudioCursorPen,
                new Point(
                    x,
                    y - 8d),
                new Point(
                    x,
                    y + 8d));
            context.DrawEllipse(
                WaveformBrush,
                null,
                new Point(
                    x,
                    y),
                3d,
                3d);
            DrawAudioLabel(
                context,
                x,
                y,
                width,
                rollHeight);
            return;
        }

        var beat =
            DisplaySynchronizer!
                .AudioSecondsToScoreBeat(
                    audioSeconds);
        var lineY = BeatToY(
            beat,
            rollHeight);
        if (lineY < 0d
            || lineY > rollHeight)
        {
            return;
        }

        context.DrawLine(
            AudioCursorPen,
            new Point(0d, lineY),
            new Point(width, lineY));
        DrawAudioLabel(
            context,
            3d,
            lineY,
            width,
            rollHeight);
    }

    private void DrawAudioLabel(
        DrawingContext context,
        double x,
        double y,
        double width,
        double rollHeight)
    {
        var text = new FormattedText(
            "原音",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            8.5d,
            WaveformBrush,
            VisualTreeHelper.GetDpi(this)
                .PixelsPerDip);
        context.DrawText(
            text,
            new Point(
                Math.Clamp(
                    x + 3d,
                    2d,
                    Math.Max(
                        2d,
                        width
                        - text.Width
                        - 2d)),
                Math.Clamp(
                    y - text.Height - 2d,
                    0d,
                    Math.Max(
                        0d,
                        rollHeight
                        - text.Height))));
    }

    private void DrawFooter(
        DrawingContext context,
        double width,
        double rollHeight)
    {
        var footerHeight =
            Math.Max(
                0d,
                ActualHeight
                - rollHeight);
        if (footerHeight <= 0d)
        {
            return;
        }

        context.DrawRectangle(
            FooterBrush,
            FooterBorderPen,
            new Rect(
                0d,
                rollHeight,
                width,
                footerHeight));

        var title = new FormattedText(
            "元音源",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            9d,
            TextBrush,
            VisualTreeHelper.GetDpi(this)
                .PixelsPerDip);
        context.DrawText(
            title,
            new Point(
                5d,
                rollHeight + 5d));

        if (_audioPositionSeconds
            is double seconds)
        {
            var time = new FormattedText(
                FormatAudioTime(
                    seconds),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                8.5d,
                MutedTextBrush,
                VisualTreeHelper.GetDpi(this)
                    .PixelsPerDip);
            context.DrawText(
                time,
                new Point(
                    5d,
                    rollHeight
                    + 8d
                    + title.Height));
        }
    }

    private double GetAudioSecondsAtPoint(
        Point point,
        double scoreBeat,
        double rollHeight)
    {
        var gap = FindAudioOnlyGapAt(
            point,
            rollHeight);
        if (gap is not null)
        {
            var ratio = ActualWidth <= 1d
                ? 0d
                : Math.Clamp(
                    point.X
                    / ActualWidth,
                    0d,
                    1d);
            return gap.StartAudioSeconds
                + gap.DurationSeconds
                * ratio;
        }

        return Math.Clamp(
            _synchronizer!
                .ScoreBeatToAudioSeconds(
                    scoreBeat),
            0d,
            _waveform!.DurationSeconds);
    }

    private ReferenceAudioGap? FindAudioOnlyGapAt(
        Point point,
        double rollHeight)
        => _synchronizer
            ?.AudioOnlyGaps
            .Where(gap =>
                Math.Abs(
                    point.Y
                    - BeatToY(
                        gap.ScoreBeat,
                        rollHeight))
                <= 10d)
            .OrderBy(gap =>
                Math.Abs(
                    point.Y
                    - BeatToY(
                        gap.ScoreBeat,
                        rollHeight)))
            .FirstOrDefault();

    private ReferenceAudioGap? FindAudioOnlyGap(
        double audioSeconds)
        => DisplaySynchronizer
            ?.AudioOnlyGaps
            .FirstOrDefault(gap =>
                audioSeconds
                    >= gap.StartAudioSeconds
                    - 0.001d
                && audioSeconds
                    <= gap.EndAudioSeconds
                    + 0.001d);

    private ReferenceAudioSyncPoint? HitTestSyncPoint(
        Point point,
        double rollHeight)
    {
        if (_synchronizer is null)
        {
            return null;
        }

        ReferenceAudioSyncPoint? best = null;
        var bestDistance =
            double.PositiveInfinity;
        foreach (var syncPoint
                 in _synchronizer.SyncPoints)
        {
            var y = BeatToY(
                GetDisplayBeat(
                    syncPoint),
                rollHeight);
            var x =
                GetSyncHandleCenterX(
                    syncPoint,
                    ActualWidth);
            var dx =
                point.X - x;
            var dy =
                point.Y - y;
            if (Math.Abs(dx)
                    > SyncHitRadius
                || Math.Abs(dy)
                    > SyncHitRadius)
            {
                continue;
            }

            var distance =
                dx * dx
                + dy * dy;
            if (distance
                < bestDistance)
            {
                best = syncPoint;
                bestDistance =
                    distance;
            }
        }

        if (best is not null)
        {
            return best;
        }

        return _synchronizer.SyncPoints
            .Select(syncPoint =>
                new
                {
                    Point = syncPoint,
                    Distance = Math.Abs(
                        point.Y
                        - BeatToY(
                            GetDisplayBeat(
                                syncPoint),
                            rollHeight))
                })
            .Where(item =>
                item.Distance <= 4d)
            .OrderBy(item =>
                item.Distance)
            .ThenBy(item =>
                Math.Abs(
                    point.X
                    - GetSyncHandleCenterX(
                        item.Point,
                        ActualWidth)))
            .Select(item =>
                item.Point)
            .FirstOrDefault();
    }

    private double GetSyncHandleCenterX(
        ReferenceAudioSyncPoint point,
        double width)
    {
        var synchronizer =
            DisplaySynchronizer;
        if (synchronizer is null)
        {
            return Math.Max(
                SyncHandleRadius + 2d,
                width - SyncHandleRadius - 2d);
        }

        var sameBeat =
            synchronizer.SyncPoints
                .Where(item =>
                    Math.Abs(
                        item.ScoreBeat
                        - point.ScoreBeat)
                    <= ScoreTiming.EventBeatTolerance)
                .OrderBy(item =>
                    item.AudioSeconds)
                .ToArray();
        if (sameBeat.Length <= 1)
        {
            return Math.Max(
                SyncHandleRadius + 2d,
                width
                - SyncHandleRadius
                - 3d);
        }

        var index = Array.FindIndex(
            sameBeat,
            item =>
                ReferenceSyncPointEquals(
                    item,
                    point));
        index = Math.Max(
            0,
            index);
        var left =
            SyncHandleRadius
            + 3d;
        var right =
            Math.Max(
                left,
                width
                - SyncHandleRadius
                - 3d);
        return left
            + (right - left)
            * index
            / Math.Max(
                1d,
                sameBeat.Length - 1d);
    }

    private double GetDisplayBeat(
        ReferenceAudioSyncPoint point)
    {
        if (_dragSyncPoint is not null
            && _dragPreviewScoreBeat
                is double previewBeat
            && ReferenceSyncPointEquals(
                _dragSyncPoint,
                point))
        {
            return previewBeat;
        }

        return point.ScoreBeat;
    }

    private bool ContainsSyncPoint(
        ReferenceAudioSyncPoint point)
        => _synchronizer
            ?.SyncPoints
            .Any(item =>
                ReferenceSyncPointEquals(
                    item,
                    point))
            == true;

    private static bool ReferenceSyncPointEquals(
        ReferenceAudioSyncPoint? left,
        ReferenceAudioSyncPoint? right)
    {
        if (left is null
            || right is null)
        {
            return left is null
                   && right is null;
        }

        return Math.Abs(
                   left.ScoreBeat
                   - right.ScoreBeat)
               <= ScoreTiming.EventBeatTolerance
               && Math.Abs(
                   left.AudioSeconds
                   - right.AudioSeconds)
               <= 0.001d;
    }

    private float GetPeakAt(
        double audioSeconds)
    {
        if (_waveform is null
            || _waveform.DurationSeconds <= 0d
            || _waveform.Peaks.Count == 0)
        {
            return 0f;
        }

        var ratio = Math.Clamp(
            audioSeconds
            / _waveform.DurationSeconds,
            0d,
            1d);
        var index = (int)Math.Clamp(
            Math.Round(
                ratio
                * (_waveform.Peaks.Count - 1)),
            0d,
            _waveform.Peaks.Count - 1d);
        return _waveform.Peaks[index];
    }

    private double SnapBeatToGrid(
        double beat)
    {
        var gridTicks =
            Math.Max(
                1L,
                _gridTicks);
        var tick =
            EditableMusicScore.BeatToTick(
                beat);
        var snappedTick =
            checked(
                (long)Math.Round(
                    tick
                    / (double)gridTicks,
                    MidpointRounding.AwayFromZero)
                * gridTicks);
        var maximumTick =
            EditableMusicScore.BeatToTick(
                _score?.LengthBeats
                ?? Math.Max(
                    0d,
                    beat));
        return EditableMusicScore.TickToBeat(
            Math.Clamp(
                snappedTick,
                0L,
                maximumTick));
    }

    private double BeatAtY(
        double y,
        double rollHeight)
    {
        if (rollHeight <= 1d)
        {
            return _cursorBeat;
        }

        var cursorY =
            rollHeight
            * EditorPianoRollControl
                .CursorVerticalRatio;
        var beat =
            _cursorBeat
            + (cursorY - y)
            * (_visibleBeats
               / rollHeight);
        return Math.Clamp(
            beat,
            0d,
            _score?.LengthBeats
            ?? Math.Max(
                0d,
                beat));
    }

    private double BeatToY(
        double beat,
        double rollHeight)
    {
        var cursorY =
            rollHeight
            * EditorPianoRollControl
                .CursorVerticalRatio;
        return cursorY
            - (beat - _cursorBeat)
            * (rollHeight
               / _visibleBeats);
    }

    private double GetRollHeight()
        => Math.Max(
            0d,
            ActualHeight
            - EditorPianoRollControl
                .KeyboardHeight);

    private static string FormatAudioTime(
        double seconds)
    {
        var value =
            TimeSpan.FromSeconds(
                Math.Max(
                    0d,
                    seconds));
        return value.TotalHours >= 1d
            ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}"
            : $"{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
    }

    private static Brush CreateBrush(
        Color color)
    {
        var brush =
            new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(
        Brush brush,
        double thickness)
    {
        var pen =
            new Pen(
                brush,
                thickness);
        pen.Freeze();
        return pen;
    }
}
