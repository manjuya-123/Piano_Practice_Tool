using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PianoPracticeTool.Core;
using PianoPracticeTool.Core.Editing;
using PianoPracticeTool.Services.Settings;

namespace PianoPracticeTool.Controls;

public enum EditorTimelineCursorRequestKind
{
    Click,
    Drag,
    Inertia
}

public sealed class EditorTimelineCursorRequestedEventArgs : EventArgs
{
    public EditorTimelineCursorRequestedEventArgs(
        double beat,
        EditorTimelineCursorRequestKind kind)
    {
        if (double.IsNaN(beat) || double.IsInfinity(beat) || beat < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(beat));
        }

        Beat = beat;
        Kind = kind;
    }

    public double Beat { get; }

    public EditorTimelineCursorRequestKind Kind { get; }
}

public sealed class EditorHarmonyEditRequestedEventArgs : EventArgs
{
    public EditorHarmonyEditRequestedEventArgs(
        int measureNumber,
        double beat,
        double laneY)
    {
        MeasureNumber = measureNumber;
        Beat = beat;
        LaneY = laneY;
    }

    public int MeasureNumber { get; }

    public double Beat { get; }

    public double LaneY { get; }
}

public sealed class EditorMeasureLaneControl : FrameworkElement
{
    private const double InertiaDecelerationBeatsPerSecondSquared = 18d;
    private const double MaximumInertiaFrameSeconds = 0.05d;
    private const double MinimumInertiaVelocityBeatsPerSecond = 0.35d;
    private const double MaximumInertiaVelocityBeatsPerSecond = 28d;
    private const double MaximumInertiaReleaseDelaySeconds = 0.12d;
    private const double HarmonyEditRegionStartX = 22d;
    private const double HarmonyLabelLeft = 24d;
    private const double HarmonyLabelRightMargin = 3d;
    private const double HarmonyLabelVerticalGap = 2d;
    private const double HarmonyLabelHorizontalPadding = 4d;
    private const double HarmonyLabelVerticalPadding = 2d;
    private const long DefaultGridTicks = 240L;

    private static readonly Brush BackgroundBrush = CreateBrush(Color.FromRgb(12, 14, 20));
    private static readonly Brush SubdivisionBrush = CreateBrush(Color.FromArgb(15, 255, 255, 255));
    private static readonly Brush BeatBrush = CreateBrush(Color.FromArgb(32, 255, 255, 255));
    private static readonly Brush MeasureBrush = CreateBrush(Color.FromArgb(92, 255, 255, 255));
    private static readonly Brush MeasureNumberBrush = CreateBrush(Color.FromRgb(186, 192, 206));
    private static readonly Brush TempoBrush = CreateBrush(Color.FromRgb(111, 208, 255));
    private static readonly Brush TempoLabelBackgroundBrush = CreateBrush(Color.FromArgb(225, 18, 33, 43));
    private static readonly Brush HarmonyBrush = CreateBrush(Color.FromRgb(255, 209, 102));
    private static readonly Brush InferredHarmonyBrush = CreateBrush(Color.FromRgb(153, 137, 91));
    private static readonly Brush HarmonyLabelBackgroundBrush = CreateBrush(Color.FromArgb(205, 25, 29, 38));
    private static readonly Brush InferredHarmonyLabelBackgroundBrush = CreateBrush(Color.FromArgb(180, 25, 29, 38));
    private static readonly Brush HarmonyEditRangeBrush = CreateBrush(Color.FromArgb(34, 102, 118, 255));
    private static readonly Brush HarmonyEditBoundaryBrush = CreateBrush(Color.FromArgb(210, 174, 183, 255));
    private static readonly Brush CursorBrush = CreateBrush(Color.FromRgb(238, 241, 247));
    private static readonly Pen SeparatorPen = CreatePen(
        CreateBrush(Color.FromRgb(48, 53, 65)),
        1d);
    private static readonly Pen SubdivisionPen = CreatePen(SubdivisionBrush, 1d);
    private static readonly Pen BeatPen = CreatePen(BeatBrush, 1d);
    private static readonly Pen MeasurePen = CreatePen(MeasureBrush, 1.5d);
    private static readonly Pen TempoMarkerPen = CreatePen(TempoBrush, 1.5d);
    private static readonly Pen HarmonyStartPen = CreatePen(HarmonyBrush, 1.5d);
    private static readonly Pen InferredHarmonyStartPen = CreatePen(InferredHarmonyBrush, 1d);
    private static readonly Pen HarmonyEditBoundaryPen = CreatePen(HarmonyEditBoundaryBrush, 1.5d);
    private static readonly Pen CursorPen = CreatePen(CursorBrush, 2d);
    private static readonly Typeface LabelTypeface = new("Segoe UI");
    private static readonly Typeface HarmonyTypeface = new("Segoe UI Semibold");

    private readonly Stopwatch _motionClock = new();

    private MusicScore? _score;
    private IReadOnlyList<HarmonyTimelineSegment> _harmonyTimeline =
        Array.Empty<HarmonyTimelineSegment>();
    private EditorTimelineDragMode _dragMode = EditorTimelineDragMode.TouchScroll;
    private double _cursorBeat;
    private double _visibleBeats = 16d;
    private long _gridTicks = DefaultGridTicks;
    private Point _pointerDownPoint;
    private double _pointerDownCursorBeat;
    private double _pointerDownTargetBeat;
    private double _lastMotionBeat;
    private double _dragVelocityBeatsPerSecond;
    private double _inertiaVelocityBeatsPerSecond;
    private long _lastMotionTimestamp;
    private bool _pointerCaptured;
    private bool _dragging;
    private bool _externalPanActive;
    private bool _inertiaRenderingSubscribed;
    private bool _harmonyEditRequestPending;
    private int _pendingHarmonyMeasureNumber;
    private double _pendingHarmonyEditBeat;
    private double _pendingHarmonyLaneY;
    private double? _harmonyEditStartBeat;
    private double? _harmonyEditEndBeat;

    public EditorMeasureLaneControl()
    {
        Unloaded += EditorMeasureLaneControl_Unloaded;
    }

    public event EventHandler<EditorTimelineCursorRequestedEventArgs>? CursorBeatRequested;

    public event EventHandler<EditorHarmonyEditRequestedEventArgs>? HarmonyEditRequested;

    public ScoreMeasure? GetMeasureAtLaneY(double laneY)
    {
        if (_score is null
            || laneY < 0d
            || laneY > GetRollHeight())
        {
            return null;
        }

        return _score.GetMeasureAt(
            GetBeatAtY(laneY));
    }

    public EditorTimelineDragMode DragMode
    {
        get => _dragMode;
        set
        {
            _dragMode = value;
            if (value != EditorTimelineDragMode.TouchScroll)
            {
                StopInertia();
            }
        }
    }

    public MusicScore? Score
    {
        get => _score;
        set
        {
            if (ReferenceEquals(_score, value))
            {
                return;
            }

            StopInertia();
            _score = value;
            _harmonyTimeline = value is null
                ? Array.Empty<HarmonyTimelineSegment>()
                : MusicTheoryAnalyzer.CreateHarmonyTimeline(value);
            _cursorBeat = Math.Clamp(_cursorBeat, 0d, value?.LengthBeats ?? 0d);
            ClearPendingHarmonyEditRequest();
            ClearHarmonyEditTarget();
            InvalidateVisual();
        }
    }

    public double CursorBeat
    {
        get => _cursorBeat;
        set
        {
            var bounded = Math.Clamp(value, 0d, _score?.LengthBeats ?? Math.Max(0d, value));
            if (Math.Abs(_cursorBeat - bounded) <= ScoreTiming.EventBeatTolerance)
            {
                return;
            }

            _cursorBeat = bounded;
            InvalidateVisual();
        }
    }

    public double VisibleBeats
    {
        get => _visibleBeats;
        set
        {
            var bounded = Math.Clamp(value, 4d, 64d);
            if (Math.Abs(_visibleBeats - bounded) <= ScoreTiming.EventBeatTolerance)
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
            var bounded = Math.Max(1L, value);
            if (_gridTicks == bounded)
            {
                return;
            }

            _gridTicks = bounded;
            InvalidateVisual();
        }
    }

    public void ShowHarmonyEditTarget(
        double startBeat,
        double endBeat)
    {
        if (double.IsNaN(startBeat)
            || double.IsInfinity(startBeat)
            || double.IsNaN(endBeat)
            || double.IsInfinity(endBeat))
        {
            throw new ArgumentOutOfRangeException(nameof(startBeat));
        }

        var scoreEnd = _score?.LengthBeats ?? Math.Max(0d, endBeat);
        var boundedStart = Math.Clamp(startBeat, 0d, scoreEnd);
        var boundedEnd = Math.Clamp(endBeat, boundedStart, scoreEnd);
        _harmonyEditStartBeat = boundedStart;
        _harmonyEditEndBeat = boundedEnd;
        InvalidateVisual();
    }

    public void ClearHarmonyEditTarget()
    {
        if (_harmonyEditStartBeat is null
            && _harmonyEditEndBeat is null)
        {
            return;
        }

        _harmonyEditStartBeat = null;
        _harmonyEditEndBeat = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(
            BackgroundBrush,
            null,
            new Rect(0d, 0d, ActualWidth, ActualHeight));

        var rollHeight = GetRollHeight();
        if (_score is null || rollHeight <= 1d)
        {
            return;
        }

        drawingContext.DrawLine(
            SeparatorPen,
            new Point(Math.Max(0d, ActualWidth - 0.5d), 0d),
            new Point(Math.Max(0d, ActualWidth - 0.5d), rollHeight));

        DrawTimelineGrid(drawingContext, rollHeight);
        DrawHarmonyEditTarget(drawingContext, rollHeight);

        var pixelsPerBeat = rollHeight / _visibleBeats;
        var cursorY = rollHeight * EditorPianoRollControl.CursorVerticalRatio;
        foreach (var measure in _score.Measures)
        {
            var y = cursorY - (measure.StartBeat - _cursorBeat) * pixelsPerBeat;
            if (y < -24d || y > rollHeight + 24d)
            {
                continue;
            }

            var numberText = new FormattedText(
                measure.Number.ToString(CultureInfo.CurrentCulture),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                10d,
                MeasureNumberBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(
                numberText,
                new Point(
                    6d,
                    y - numberText.Height - 2d));
        }

        foreach (var harmony in _harmonyTimeline)
        {
            DrawHarmonyLabel(
                drawingContext,
                harmony,
                rollHeight);
        }

        DrawTempoEvents(
            drawingContext,
            rollHeight);

        drawingContext.DrawLine(
            CursorPen,
            new Point(0d, cursorY),
            new Point(ActualWidth, cursorY));
    }

    protected override void OnMouseRightButtonDown(
        MouseButtonEventArgs e)
    {
        StopInertia();
        base.OnMouseRightButtonDown(e);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_score is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (point.Y < 0d || point.Y > GetRollHeight())
        {
            return;
        }

        if (e.ClickCount >= 2 && point.X >= HarmonyEditRegionStartX)
        {
            var editBeat = GetHarmonyEditBeat(point);
            var measure = _score.GetMeasureAt(editBeat);
            if (measure is not null)
            {
                StopInertia();
                _harmonyEditRequestPending = true;
                _pendingHarmonyMeasureNumber = measure.Number;
                _pendingHarmonyEditBeat = editBeat;
                _pendingHarmonyLaneY = point.Y;
                e.Handled = true;
                return;
            }
        }

        StopInertia();
        _pointerDownPoint = point;
        _pointerDownCursorBeat = _cursorBeat;
        _pointerDownTargetBeat = GetBeatAtY(point.Y);
        _lastMotionBeat = _pointerDownCursorBeat;
        _dragVelocityBeatsPerSecond = 0d;
        _lastMotionTimestamp = Stopwatch.GetTimestamp();
        _motionClock.Restart();
        _pointerCaptured = CaptureMouse();
        _dragging = false;
        e.Handled = true;
    }

    public void BeginExternalPan(double pointerY)
    {
        if (_score is null)
        {
            return;
        }

        StopInertia();
        _pointerDownPoint = new Point(0d, pointerY);
        _pointerDownCursorBeat = _cursorBeat;
        _lastMotionBeat = _cursorBeat;
        _dragVelocityBeatsPerSecond = 0d;
        _lastMotionTimestamp = Stopwatch.GetTimestamp();
        _motionClock.Restart();
        _externalPanActive = true;
        _dragging = true;
    }

    public void UpdateExternalPan(double pointerY)
    {
        if (!_externalPanActive)
        {
            return;
        }

        UpdatePanFromPointerY(pointerY);
    }

    public void EndExternalPan()
    {
        if (!_externalPanActive)
        {
            return;
        }

        _externalPanActive = false;
        _dragging = false;
        if (_dragMode == EditorTimelineDragMode.TouchScroll)
        {
            StartInertiaIfNeeded();
        }
    }

    public void CancelExternalPan()
    {
        _externalPanActive = false;
        _dragging = false;
        StopInertia();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_pointerCaptured || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (!_dragging)
        {
            var horizontalDistance = Math.Abs(point.X - _pointerDownPoint.X);
            var verticalDistance = Math.Abs(point.Y - _pointerDownPoint.Y);
            if (horizontalDistance < SystemParameters.MinimumHorizontalDragDistance
                && verticalDistance < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _dragging = true;
        }

        UpdatePanFromPointerY(point.Y);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_harmonyEditRequestPending)
        {
            var measureNumber = _pendingHarmonyMeasureNumber;
            var editBeat = _pendingHarmonyEditBeat;
            var laneY = _pendingHarmonyLaneY;
            ClearPendingHarmonyEditRequest();

            HarmonyEditRequested?.Invoke(
                this,
                new EditorHarmonyEditRequestedEventArgs(
                    measureNumber,
                    editBeat,
                    laneY));
            e.Handled = true;
            return;
        }

        if (!_pointerCaptured)
        {
            return;
        }

        var wasDragging = _dragging;
        _pointerCaptured = false;
        _dragging = false;
        _externalPanActive = false;
        ReleaseMouseCapture();

        if (!wasDragging)
        {
            if (_pointerDownPoint.X < HarmonyEditRegionStartX)
            {
                RaiseCursorRequest(
                    _pointerDownTargetBeat,
                    EditorTimelineCursorRequestKind.Click);
            }
        }
        else if (_dragMode == EditorTimelineDragMode.TouchScroll)
        {
            StartInertiaIfNeeded();
        }

        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _pointerCaptured = false;
        _dragging = false;
    }

    private void ClearPendingHarmonyEditRequest()
    {
        _harmonyEditRequestPending = false;
        _pendingHarmonyMeasureNumber = 0;
        _pendingHarmonyEditBeat = 0d;
        _pendingHarmonyLaneY = 0d;
    }

    private void UpdatePanFromPointerY(double pointerY)
    {
        var rollHeight = GetRollHeight();
        if (rollHeight <= 1d)
        {
            return;
        }

        var beatDelta =
            (pointerY - _pointerDownPoint.Y)
            * (_visibleBeats / rollHeight);
        var targetBeat = Math.Clamp(
            _pointerDownCursorBeat + beatDelta,
            0d,
            _score?.LengthBeats ?? 0d);

        UpdateDragVelocity(targetBeat);
        RaiseCursorRequest(targetBeat, EditorTimelineCursorRequestKind.Drag);
    }

    private void UpdateDragVelocity(double targetBeat)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsedSeconds = (now - _lastMotionTimestamp) / (double)Stopwatch.Frequency;
        if (elapsedSeconds <= 0.0001d)
        {
            return;
        }

        var instantaneousVelocity = (targetBeat - _lastMotionBeat) / elapsedSeconds;
        _dragVelocityBeatsPerSecond =
            _dragVelocityBeatsPerSecond * 0.65d
            + instantaneousVelocity * 0.35d;
        _dragVelocityBeatsPerSecond = Math.Clamp(
            _dragVelocityBeatsPerSecond,
            -MaximumInertiaVelocityBeatsPerSecond,
            MaximumInertiaVelocityBeatsPerSecond);
        _lastMotionBeat = targetBeat;
        _lastMotionTimestamp = now;
    }

    private void StartInertiaIfNeeded()
    {
        var releaseDelaySeconds =
            (Stopwatch.GetTimestamp() - _lastMotionTimestamp)
            / (double)Stopwatch.Frequency;
        if (releaseDelaySeconds > MaximumInertiaReleaseDelaySeconds)
        {
            _inertiaVelocityBeatsPerSecond = 0d;
            return;
        }

        _inertiaVelocityBeatsPerSecond = _dragVelocityBeatsPerSecond;
        if (Math.Abs(_inertiaVelocityBeatsPerSecond) < MinimumInertiaVelocityBeatsPerSecond)
        {
            _inertiaVelocityBeatsPerSecond = 0d;
            return;
        }

        _motionClock.Restart();
        SubscribeInertiaRendering();
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        if (_score is null)
        {
            StopInertia();
            return;
        }

        var elapsedSeconds = Math.Min(
            _motionClock.Elapsed.TotalSeconds,
            MaximumInertiaFrameSeconds);
        _motionClock.Restart();
        if (elapsedSeconds <= 0d)
        {
            return;
        }

        var currentVelocity = _inertiaVelocityBeatsPerSecond;
        var velocityMagnitude = Math.Abs(currentVelocity);
        var direction = Math.Sign(currentVelocity);
        var deceleration = InertiaDecelerationBeatsPerSecondSquared;
        var timeToStop = velocityMagnitude / deceleration;
        var movementSeconds = Math.Min(elapsedSeconds, timeToStop);
        var distance =
            currentVelocity * movementSeconds
            - direction * 0.5d * deceleration * movementSeconds * movementSeconds;

        var nextBeat = Math.Clamp(
            _cursorBeat + distance,
            0d,
            _score.LengthBeats);
        RaiseCursorRequest(nextBeat, EditorTimelineCursorRequestKind.Inertia);

        var nextVelocityMagnitude = Math.Max(
            0d,
            velocityMagnitude - deceleration * elapsedSeconds);
        _inertiaVelocityBeatsPerSecond = direction * nextVelocityMagnitude;

        if (nextVelocityMagnitude < MinimumInertiaVelocityBeatsPerSecond
            || nextBeat <= ScoreTiming.EventBeatTolerance
            || nextBeat >= _score.LengthBeats - ScoreTiming.EventBeatTolerance)
        {
            StopInertia();
        }
    }

    private void SubscribeInertiaRendering()
    {
        if (_inertiaRenderingSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering += CompositionTarget_Rendering;
        _inertiaRenderingSubscribed = true;
    }

    private void StopInertia()
    {
        if (_inertiaRenderingSubscribed)
        {
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            _inertiaRenderingSubscribed = false;
        }

        _inertiaVelocityBeatsPerSecond = 0d;
        _motionClock.Reset();
    }

    private void EditorMeasureLaneControl_Unloaded(object sender, RoutedEventArgs e)
    {
        ClearPendingHarmonyEditRequest();
        StopInertia();
    }

    private void DrawTempoEvents(
        DrawingContext context,
        double rollHeight)
    {
        if (_score is null)
        {
            return;
        }

        foreach (var tempo in _score.TempoEvents
                     .OrderBy(item => item.Beat))
        {
            var y = BeatToY(
                tempo.Beat,
                rollHeight);
            if (y < -20d
                || y > rollHeight + 20d)
            {
                continue;
            }

            var text = new FormattedText(
                $"{tempo.BeatsPerMinute:0.##} BPM",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                9d,
                TempoBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var paddingX = 4d;
            var paddingY = 2d;
            var width =
                text.Width + paddingX * 2d;
            var height =
                text.Height + paddingY * 2d;
            var left = Math.Max(
                HarmonyEditRegionStartX + 2d,
                ActualWidth - width - 4d);
            var top =
                y - height / 2d;

            context.DrawLine(
                TempoMarkerPen,
                new Point(
                    HarmonyEditRegionStartX,
                    y),
                new Point(
                    ActualWidth,
                    y));
            context.DrawRectangle(
                TempoLabelBackgroundBrush,
                TempoMarkerPen,
                new Rect(
                    left,
                    top,
                    width,
                    height));
            context.DrawText(
                text,
                new Point(
                    left + paddingX,
                    top + paddingY));
        }
    }

    private void DrawHarmonyLabel(
        DrawingContext context,
        HarmonyTimelineSegment harmony,
        double rollHeight)
    {
        var boundaryY = BeatToY(
            harmony.StartBeat,
            rollHeight);
        if (boundaryY < -32d || boundaryY > rollHeight + 32d)
        {
            return;
        }

        var labelRect = GetHarmonyLabelRect(
            harmony,
            rollHeight);
        var startPen = harmony.IsInferred
            ? InferredHarmonyStartPen
            : HarmonyStartPen;
        var endY = BeatToY(
            harmony.EndBeat,
            rollHeight);
        context.DrawLine(
            startPen,
            new Point(HarmonyEditRegionStartX, boundaryY),
            new Point(ActualWidth, boundaryY));
        context.DrawLine(
            startPen,
            new Point(
                HarmonyEditRegionStartX + 1.5d,
                boundaryY),
            new Point(
                HarmonyEditRegionStartX + 1.5d,
                endY));

        if (labelRect.Height <= 1d
            || labelRect.Width <= 1d)
        {
            return;
        }

        context.DrawRectangle(
            harmony.IsInferred
                ? InferredHarmonyLabelBackgroundBrush
                : HarmonyLabelBackgroundBrush,
            null,
            labelRect);

        var harmonyText = CreateHarmonyText(harmony);
        var textY =
            labelRect.Top
            + Math.Max(
                0d,
                (labelRect.Height - harmonyText.Height) / 2d);
        context.PushClip(new RectangleGeometry(labelRect));
        context.DrawText(
            harmonyText,
            new Point(
                labelRect.Left + HarmonyLabelHorizontalPadding,
                textY));
        context.Pop();
    }

    private Rect GetHarmonyLabelRect(
        HarmonyTimelineSegment harmony,
        double rollHeight)
    {
        var startY = BeatToY(
            harmony.StartBeat,
            rollHeight);
        var endY = BeatToY(
            harmony.EndBeat,
            rollHeight);
        var harmonyText = CreateHarmonyText(harmony);
        var desiredHeight =
            harmonyText.Height
            + HarmonyLabelVerticalPadding * 2d;

        // Time advances upward in this editor. Keep the label entirely on
        // the active side of the harmony start boundary.
        var activeTop = Math.Min(startY, endY);
        var activeBottom = Math.Max(startY, endY);
        var bottom =
            Math.Min(
                startY - HarmonyLabelVerticalGap,
                activeBottom);
        var top = Math.Max(
            activeTop,
            bottom - desiredHeight);
        if (bottom <= top)
        {
            top = bottom - desiredHeight;
        }

        var width = Math.Max(
            0d,
            ActualWidth
            - HarmonyLabelLeft
            - HarmonyLabelRightMargin);
        return new Rect(
            HarmonyLabelLeft,
            top,
            width,
            Math.Max(0d, bottom - top));
    }

    private FormattedText CreateHarmonyText(
        HarmonyTimelineSegment harmony)
        => new(
            harmony.Symbol,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            HarmonyTypeface,
            10d,
            harmony.IsInferred
                ? InferredHarmonyBrush
                : HarmonyBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(
                1d,
                ActualWidth
                - HarmonyLabelLeft
                - HarmonyLabelRightMargin
                - HarmonyLabelHorizontalPadding * 2d),
            Trimming = TextTrimming.CharacterEllipsis
        };

    private void DrawHarmonyEditTarget(
        DrawingContext context,
        double rollHeight)
    {
        if (_harmonyEditStartBeat is not double startBeat
            || _harmonyEditEndBeat is not double endBeat
            || endBeat - startBeat <= ScoreTiming.EventBeatTolerance)
        {
            return;
        }

        var startY = BeatToY(startBeat, rollHeight);
        var endY = BeatToY(endBeat, rollHeight);
        var top = Math.Clamp(
            Math.Min(startY, endY),
            0d,
            rollHeight);
        var bottom = Math.Clamp(
            Math.Max(startY, endY),
            0d,
            rollHeight);
        if (bottom <= top)
        {
            return;
        }

        context.DrawRectangle(
            HarmonyEditRangeBrush,
            null,
            new Rect(
                HarmonyEditRegionStartX,
                top,
                Math.Max(0d, ActualWidth - HarmonyEditRegionStartX),
                bottom - top));
        context.DrawLine(
            HarmonyEditBoundaryPen,
            new Point(HarmonyEditRegionStartX, startY),
            new Point(ActualWidth, startY));
        context.DrawLine(
            HarmonyEditBoundaryPen,
            new Point(HarmonyEditRegionStartX, endY),
            new Point(ActualWidth, endY));
    }

    private double GetHarmonyEditBeat(Point point)
    {
        var rollHeight = GetRollHeight();
        if (rollHeight > 1d)
        {
            foreach (var harmony in _harmonyTimeline)
            {
                var labelRect = GetHarmonyLabelRect(
                    harmony,
                    rollHeight);
                var boundaryY = BeatToY(
                    harmony.StartBeat,
                    rollHeight);
                if (labelRect.Contains(point)
                    || point.X >= HarmonyEditRegionStartX
                        && Math.Abs(point.Y - boundaryY) <= 3d)
                {
                    return harmony.StartBeat;
                }
            }
        }

        return SnapBeatToGrid(GetBeatAtY(point.Y));
    }

    private double BeatToY(
        double beat,
        double rollHeight)
    {
        var cursorY =
            rollHeight * EditorPianoRollControl.CursorVerticalRatio;
        var pixelsPerBeat = rollHeight / _visibleBeats;
        return cursorY - (beat - _cursorBeat) * pixelsPerBeat;
    }

    private void DrawTimelineGrid(
        DrawingContext context,
        double rollHeight)
    {
        if (_score is null || rollHeight <= 1d)
        {
            return;
        }

        var cursorY =
            rollHeight * EditorPianoRollControl.CursorVerticalRatio;
        var pixelsPerBeat = rollHeight / _visibleBeats;
        var topBeat = Math.Clamp(
            _cursorBeat + cursorY / pixelsPerBeat,
            0d,
            _score.LengthBeats);
        var bottomBeat = Math.Clamp(
            _cursorBeat - (rollHeight - cursorY) / pixelsPerBeat,
            0d,
            _score.LengthBeats);

        var gridStep = Math.Max(
            EditableMusicScore.TickToBeat(1L),
            EditableMusicScore.TickToBeat(_gridTicks));
        var firstGridIndex = (long)Math.Floor(bottomBeat / gridStep);
        var lastGridIndex = (long)Math.Ceiling(topBeat / gridStep);
        for (var index = firstGridIndex; index <= lastGridIndex; index++)
        {
            var beat = index * gridStep;
            var nearestWholeBeat = Math.Round(beat);
            if (Math.Abs(beat - nearestWholeBeat)
                <= ScoreTiming.EventBeatTolerance)
            {
                continue;
            }

            DrawGridLine(
                context,
                rollHeight,
                beat,
                SubdivisionPen);
        }

        var firstBeat = (int)Math.Floor(bottomBeat);
        var lastBeat = (int)Math.Ceiling(topBeat);
        for (var beat = firstBeat; beat <= lastBeat; beat++)
        {
            DrawGridLine(
                context,
                rollHeight,
                beat,
                BeatPen);
        }

        foreach (var measure in _score.Measures)
        {
            if (measure.StartBeat
                    < bottomBeat - ScoreTiming.EventBeatTolerance
                || measure.StartBeat
                    > topBeat + ScoreTiming.EventBeatTolerance)
            {
                continue;
            }

            DrawGridLine(
                context,
                rollHeight,
                measure.StartBeat,
                MeasurePen);
        }
    }

    private void DrawGridLine(
        DrawingContext context,
        double rollHeight,
        double beat,
        Pen pen)
    {
        var cursorY =
            rollHeight * EditorPianoRollControl.CursorVerticalRatio;
        var pixelsPerBeat = rollHeight / _visibleBeats;
        var y =
            cursorY - (beat - _cursorBeat) * pixelsPerBeat;
        context.DrawLine(
            pen,
            new Point(0d, y),
            new Point(ActualWidth, y));
    }

    private double SnapBeatToGrid(double beat)
    {
        var gridTick = Math.Max(1L, _gridTicks);
        var tick = EditableMusicScore.BeatToTick(beat);
        var snappedTick = checked(
            (long)Math.Round(
                tick / (double)gridTick,
                MidpointRounding.AwayFromZero)
            * gridTick);
        var maximumTick = Math.Max(
            0L,
            EditableMusicScore.BeatToTick(
                _score?.LengthBeats ?? 0d) - 1L);
        return EditableMusicScore.TickToBeat(
            Math.Clamp(
                snappedTick,
                0L,
                maximumTick));
    }

    private double GetBeatAtY(double y)
    {
        var rollHeight = GetRollHeight();
        if (rollHeight <= 1d)
        {
            return _cursorBeat;
        }

        var cursorY = rollHeight * EditorPianoRollControl.CursorVerticalRatio;
        var pixelsPerBeat = rollHeight / _visibleBeats;
        var beat = _cursorBeat + (cursorY - y) / pixelsPerBeat;
        return Math.Clamp(beat, 0d, _score?.LengthBeats ?? 0d);
    }

    private void RaiseCursorRequest(
        double beat,
        EditorTimelineCursorRequestKind kind)
    {
        var bounded = Math.Clamp(beat, 0d, _score?.LengthBeats ?? 0d);
        CursorBeatRequested?.Invoke(
            this,
            new EditorTimelineCursorRequestedEventArgs(bounded, kind));
    }

    private double GetRollHeight()
        => Math.Max(0d, ActualHeight - EditorPianoRollControl.KeyboardHeight);

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
