using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PianoPracticeTool.Core;
using PianoPracticeTool.Core.Editing;

namespace PianoPracticeTool.Controls;

public readonly record struct EditorNoteHit(int NoteId, bool IsResizeEdge);

public enum EditorPreviewBoundary
{
    Start,
    End
}

public sealed class EditorPianoRollControl : FrameworkElement
{
    internal const double KeyboardHeight = 74d;
    internal const double CursorVerticalRatio = 0.72d;
    private const double MinimumResizeHitPixels = 2d;
    private const double MaximumResizeHitPixels = 5d;
    private const double PreviewBoundaryHitPixels = 7d;
    private const int StandardPianoLowestMidi = 21;
    private const int StandardPianoHighestMidi = 108;
    private const int MinimumVisiblePitchCount = 12;
    private const int PitchContentPaddingSemitones = 2;
    private const double PitchZoomRatio = 1.25d;

    public static readonly DependencyProperty GridTicksProperty = DependencyProperty.Register(
        nameof(GridTicks),
        typeof(long),
        typeof(EditorPianoRollControl),
        new FrameworkPropertyMetadata(
            240L,
            FrameworkPropertyMetadataOptions.AffectsRender,
            null,
            CoerceGridTicks));

    private static readonly Brush BackgroundBrush = CreateBrush(Color.FromRgb(8, 10, 15));
    private static readonly Brush SubdivisionBrush = CreateBrush(Color.FromArgb(15, 255, 255, 255));
    private static readonly Brush BeatBrush = CreateBrush(Color.FromArgb(32, 255, 255, 255));
    private static readonly Brush MeasureBrush = CreateBrush(Color.FromArgb(92, 255, 255, 255));
    private static readonly Brush RightHandBrush = CreateBrush(Color.FromRgb(76, 201, 240));
    private static readonly Brush BlackKeyRightHandBrush = CreateBrush(Color.FromRgb(18, 72, 96));
    private static readonly Brush LeftHandBrush = CreateBrush(Color.FromRgb(255, 107, 129));
    private static readonly Brush BlackKeyLeftHandBrush = CreateBrush(Color.FromRgb(104, 25, 42));
    private static readonly Brush SelectedBrush = CreateBrush(Color.FromRgb(85, 214, 138));
    private static readonly Brush BlackKeySelectedBrush = CreateBrush(Color.FromRgb(20, 82, 47));
    private static readonly Brush CursorBrush = CreateBrush(Color.FromRgb(238, 241, 247));
    private static readonly Brush PreviewBrush = CreateBrush(Color.FromRgb(255, 209, 102));
    private static readonly Brush PreviewStartBrush = CreateBrush(Color.FromRgb(85, 214, 138));
    private static readonly Brush PreviewEndBrush = CreateBrush(Color.FromRgb(255, 107, 129));
    private static readonly Brush PreviewRangeBrush = CreateBrush(Color.FromArgb(18, 255, 209, 102));
    private static readonly Brush WhiteKeyBrush = CreateBrush(Color.FromRgb(231, 234, 239));
    private static readonly Brush BlackKeyBrush = CreateBrush(Color.FromRgb(29, 32, 39));
    private static readonly Brush UnavailableWhiteKeyBrush = CreateBrush(Color.FromRgb(88, 93, 104));
    private static readonly Brush UnavailableBlackKeyBrush = CreateBrush(Color.FromRgb(12, 14, 18));
    private static readonly Brush ActiveKeyBrush = CreateBrush(Color.FromRgb(85, 214, 138));
    private static readonly Brush DragTargetKeyBrush = CreateBrush(Color.FromRgb(255, 209, 102));
    private static readonly Brush DragPitchLaneBrush = CreateBrush(Color.FromArgb(30, 255, 209, 102));
    private static readonly Brush DragLabelBackgroundBrush = CreateBrush(Color.FromArgb(235, 22, 25, 32));
    private static readonly Brush DragLabelTextBrush = CreateBrush(Color.FromRgb(245, 247, 250));
    private static readonly Brush SelectionFillBrush = CreateBrush(Color.FromArgb(34, 102, 118, 255));
    private static readonly Brush ScaleToneGuideBrush = CreateBrush(Color.FromArgb(14, 102, 118, 255));
    private static readonly Brush ScaleRootGuideBrush = CreateBrush(Color.FromArgb(30, 102, 118, 255));
    private static readonly Brush ChordToneGuideBrush = CreateBrush(Color.FromArgb(44, 255, 209, 102));
    private static readonly Pen SubdivisionPen = CreatePen(SubdivisionBrush, 1d);
    private static readonly Pen BeatPen = CreatePen(BeatBrush, 1d);
    private static readonly Pen MeasurePen = CreatePen(MeasureBrush, 1.5d);
    private static readonly Pen PitchDivisionPen = CreatePen(CreateBrush(Color.FromArgb(24, 255, 255, 255)), 1d);
    private static readonly Pen OctaveDivisionPen = CreatePen(CreateBrush(Color.FromArgb(100, 102, 118, 255)), 1.5d);
    private static readonly Pen CursorPen = CreatePen(CursorBrush, 2d);
    private static readonly Pen PreviewPen = CreatePen(PreviewBrush, 2d);
    private static readonly Pen PreviewStartPen = CreatePen(PreviewStartBrush, 2d);
    private static readonly Pen PreviewEndPen = CreatePen(PreviewEndBrush, 2d);
    private static readonly Pen NotePen = CreatePen(CreateBrush(Color.FromArgb(110, 0, 0, 0)), 1d);
    private static readonly Pen ResizeHandlePen = CreatePen(CreateBrush(Color.FromArgb(220, 245, 247, 250)), 2d);
    private static readonly Pen SelectionPen = CreatePen(CreateBrush(Color.FromArgb(210, 102, 118, 255)), 1.5d);
    private static readonly Pen KeyPen = CreatePen(CreateBrush(Color.FromRgb(72, 77, 88)), 1d);
    private static readonly Pen KeyboardRangeBoundaryPen = CreatePen(CreateBrush(Color.FromArgb(220, 102, 118, 255)), 2d);
    private static readonly Pen DragLabelBorderPen = CreatePen(CreateBrush(Color.FromArgb(180, 255, 209, 102)), 1d);
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private MusicScore? _score;
    private IReadOnlyCollection<int> _selectedNoteIds = Array.Empty<int>();
    private IReadOnlyCollection<int> _activeMidiNotes = Array.Empty<int>();
    private PracticeMidiRange? _connectedKeyboardRange;
    private double _cursorBeat;
    private double _visibleBeats = 16d;
    private double? _previewBeat;
    private double? _previewReturnBeat;
    private double? _previewStartBeat;
    private double? _previewEndBeat;
    private Rect? _selectionRectangle;
    private bool _pitchViewportInitialized;
    private int _visiblePitchLowestMidi = StandardPianoLowestMidi;
    private int _visiblePitchHighestMidi = StandardPianoHighestMidi;
    private int? _dragPreviewMidiNote;
    private Point? _dragPreviewPoint;
    private IReadOnlyList<EditorScaleGuideRegion> _scaleGuideRegions =
        Array.Empty<EditorScaleGuideRegion>();
    private IReadOnlyList<EditorChordGuideRegion> _chordGuideRegions =
        Array.Empty<EditorChordGuideRegion>();

    public event EventHandler? PitchViewportChanged;

    public event EventHandler? TimelineViewportChanged;

    public MusicScore? Score
    {
        get => _score;
        set
        {
            _score = value;
            var length = value?.LengthBeats ?? 0d;
            _cursorBeat = Math.Clamp(_cursorBeat, 0d, length);
            if (_previewReturnBeat is double returnBeat)
            {
                _previewReturnBeat = Math.Clamp(returnBeat, 0d, length);
            }

            if (_previewStartBeat is double startBeat)
            {
                _previewStartBeat = Math.Clamp(startBeat, 0d, length);
            }

            if (_previewEndBeat is double endBeat)
            {
                _previewEndBeat = Math.Clamp(endBeat, 0d, length);
            }

            ClampPitchViewportToUniverse();
            InvalidateVisual();
            TimelineViewportChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlyCollection<int> SelectedNoteIds
    {
        get => _selectedNoteIds;
        set
        {
            var next = value?.ToArray() ?? Array.Empty<int>();
            if (_selectedNoteIds.SequenceEqual(next))
            {
                return;
            }

            _selectedNoteIds = next;
            InvalidateVisual();
        }
    }

    public IReadOnlyCollection<int> ActiveMidiNotes
    {
        get => _activeMidiNotes;
        set
        {
            _activeMidiNotes = value?.ToArray() ?? Array.Empty<int>();
            InvalidateVisual();
        }
    }

    public IReadOnlyList<EditorScaleGuideRegion> ScaleGuideRegions
    {
        get => _scaleGuideRegions;
        set
        {
            _scaleGuideRegions = value?.ToArray()
                ?? Array.Empty<EditorScaleGuideRegion>();
            InvalidateVisual();
        }
    }

    public IReadOnlyList<EditorChordGuideRegion> ChordGuideRegions
    {
        get => _chordGuideRegions;
        set
        {
            _chordGuideRegions = value?.ToArray()
                ?? Array.Empty<EditorChordGuideRegion>();
            InvalidateVisual();
        }
    }

    public PracticeMidiRange? ConnectedKeyboardRange
    {
        get => _connectedKeyboardRange;
        set
        {
            if (_connectedKeyboardRange == value)
            {
                return;
            }

            _connectedKeyboardRange = value;
            ClampPitchViewportToUniverse();
            InvalidateVisual();
        }
    }

    public double PitchLaneWidth
    {
        get
        {
            EnsurePitchViewportInitialized();
            var visibleCount =
                _visiblePitchHighestMidi - _visiblePitchLowestMidi + 1;
            return visibleCount <= 0 || ActualWidth <= 0d
                ? 1d
                : ActualWidth / visibleCount;
        }
    }

    public PracticeMidiRange VisiblePitchRange
    {
        get
        {
            EnsurePitchViewportInitialized();
            return new PracticeMidiRange(_visiblePitchLowestMidi, _visiblePitchHighestMidi);
        }
    }

    public long GridTicks
    {
        get => (long)GetValue(GridTicksProperty);
        set => SetValue(GridTicksProperty, value);
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
            if (_previewBeat is null)
            {
                _previewReturnBeat = null;
            }

            InvalidateVisual();
            TimelineViewportChanged?.Invoke(this, EventArgs.Empty);
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
            TimelineViewportChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double? PreviewBeat
    {
        get => _previewBeat;
        set
        {
            if (value is not double previewBeat)
            {
                ClearPreviewBeat(restoreCursor: true);
                return;
            }

            if (_previewBeat is null)
            {
                _previewReturnBeat = _cursorBeat;
            }

            var bounded = Math.Clamp(
                previewBeat,
                0d,
                _score?.LengthBeats ?? Math.Max(0d, previewBeat));
            _previewBeat = bounded;
            _cursorBeat = bounded;
            InvalidateVisual();
            TimelineViewportChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ClearPreviewBeat(bool restoreCursor)
    {
        _previewBeat = null;
        if (restoreCursor && _previewReturnBeat is double returnBeat)
        {
            _cursorBeat = Math.Clamp(
                returnBeat,
                0d,
                _score?.LengthBeats ?? Math.Max(0d, returnBeat));
        }

        _previewReturnBeat = null;
        InvalidateVisual();
        TimelineViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    public double? PreviewStartBeat
    {
        get => _previewStartBeat;
        set
        {
            _previewStartBeat = value is double beat
                ? Math.Clamp(beat, 0d, _score?.LengthBeats ?? Math.Max(0d, beat))
                : null;
            InvalidateVisual();
        }
    }

    public double? PreviewEndBeat
    {
        get => _previewEndBeat;
        set
        {
            _previewEndBeat = value is double beat
                ? Math.Clamp(beat, 0d, _score?.LengthBeats ?? Math.Max(0d, beat))
                : null;
            InvalidateVisual();
        }
    }

    public Rect? SelectionRectangle
    {
        get => _selectionRectangle;
        set
        {
            _selectionRectangle = value;
            InvalidateVisual();
        }
    }

    public void ResetPitchViewport()
    {
        var universe = GetPitchUniverse();
        SetPitchViewport(universe.LowestMidi, universe.HighestMidi);
    }

    public void FitPitchViewportToScore()
    {
        if (_score is null
            || _score.Notes.Count == 0)
        {
            ResetPitchViewport();
            return;
        }

        var universe =
            GetPitchUniverse();
        var lowest =
            Math.Max(
                universe.LowestMidi,
                _score.MinMidiNote
                - PitchContentPaddingSemitones);
        var highest =
            Math.Min(
                universe.HighestMidi,
                _score.MaxMidiNote
                + PitchContentPaddingSemitones);
        var minimumVisibleCount =
            Math.Min(
                MinimumVisiblePitchCount,
                universe.HighestMidi
                - universe.LowestMidi
                + 1);
        var visibleCount =
            highest - lowest + 1;

        if (visibleCount
            < minimumVisibleCount)
        {
            var missing =
                minimumVisibleCount
                - visibleCount;
            var lowerExpansion =
                missing / 2;
            var upperExpansion =
                missing - lowerExpansion;
            lowest -=
                lowerExpansion;
            highest +=
                upperExpansion;

            if (lowest
                < universe.LowestMidi)
            {
                highest +=
                    universe.LowestMidi
                    - lowest;
                lowest =
                    universe.LowestMidi;
            }

            if (highest
                > universe.HighestMidi)
            {
                lowest -=
                    highest
                    - universe.HighestMidi;
                highest =
                    universe.HighestMidi;
            }

            lowest =
                Math.Max(
                    universe.LowestMidi,
                    lowest);
        }

        SetPitchViewport(
            lowest,
            highest);
    }

    public void RestorePitchViewport(
        int lowestMidi,
        int highestMidi)
    {
        var universe =
            GetPitchUniverse();
        var boundedLowest =
            Math.Clamp(
                lowestMidi,
                universe.LowestMidi,
                universe.HighestMidi);
        var boundedHighest =
            Math.Clamp(
                highestMidi,
                boundedLowest,
                universe.HighestMidi);
        SetPitchViewport(
            boundedLowest,
            boundedHighest);
    }

    public void ZoomPitchViewport(bool zoomIn, double anchorRatio = 0.5d)
    {
        EnsurePitchViewportInitialized();
        var universe = GetPitchUniverse();
        var universeCount = universe.HighestMidi - universe.LowestMidi + 1;
        var currentCount = _visiblePitchHighestMidi - _visiblePitchLowestMidi + 1;
        var minimumCount = Math.Min(MinimumVisiblePitchCount, universeCount);
        var targetCount = zoomIn
            ? (int)Math.Floor(currentCount / PitchZoomRatio)
            : (int)Math.Ceiling(currentCount * PitchZoomRatio);
        targetCount = Math.Clamp(targetCount, minimumCount, universeCount);

        if (zoomIn && targetCount >= currentCount && currentCount > minimumCount)
        {
            targetCount = currentCount - 1;
        }
        else if (!zoomIn && targetCount <= currentCount && currentCount < universeCount)
        {
            targetCount = currentCount + 1;
        }

        if (targetCount == currentCount)
        {
            return;
        }

        var boundedAnchor = Math.Clamp(anchorRatio, 0d, 1d);
        var anchorMidi = _visiblePitchLowestMidi + boundedAnchor * (currentCount - 1);
        var targetLowest = (int)Math.Round(
            anchorMidi - boundedAnchor * (targetCount - 1),
            MidpointRounding.AwayFromZero);
        var maximumLowest = universe.HighestMidi - targetCount + 1;
        targetLowest = Math.Clamp(targetLowest, universe.LowestMidi, maximumLowest);
        SetPitchViewport(targetLowest, targetLowest + targetCount - 1);
    }

    public void PanPitchViewport(int semitones)
    {
        if (semitones == 0)
        {
            return;
        }

        EnsurePitchViewportInitialized();
        var universe = GetPitchUniverse();
        var visibleCount = _visiblePitchHighestMidi - _visiblePitchLowestMidi + 1;
        var universeCount = universe.HighestMidi - universe.LowestMidi + 1;
        if (visibleCount >= universeCount)
        {
            return;
        }

        var maximumLowest = universe.HighestMidi - visibleCount + 1;
        var targetLowest = Math.Clamp(
            _visiblePitchLowestMidi + semitones,
            universe.LowestMidi,
            maximumLowest);
        SetPitchViewport(targetLowest, targetLowest + visibleCount - 1);
    }

    public void SetPitchViewportLowest(int lowestMidi)
    {
        EnsurePitchViewportInitialized();
        var visibleCount =
            _visiblePitchHighestMidi - _visiblePitchLowestMidi + 1;
        var universe = GetPitchUniverse();
        var maximumLowest =
            universe.HighestMidi - visibleCount + 1;
        var boundedLowest = Math.Clamp(
            lowestMidi,
            universe.LowestMidi,
            Math.Max(universe.LowestMidi, maximumLowest));
        SetPitchViewport(
            boundedLowest,
            boundedLowest + visibleCount - 1);
    }

    public void ShowDragPitchPreview(int midiNote, Point point)
    {
        _dragPreviewMidiNote = Math.Clamp(midiNote, 0, 127);
        _dragPreviewPoint = point;
        InvalidateVisual();
    }

    public void HideDragPitchPreview()
    {
        if (_dragPreviewMidiNote is null && _dragPreviewPoint is null)
        {
            return;
        }

        _dragPreviewMidiNote = null;
        _dragPreviewPoint = null;
        InvalidateVisual();
    }

    public bool TryHitTestNote(Point point, out EditorNoteHit hit)
    {
        hit = default;
        if (_score is null || !TryGetGeometry(out var geometry) || point.Y >= geometry.RollHeight)
        {
            return false;
        }

        foreach (var note in _score.Notes
                     .OrderByDescending(note => note.StartBeat)
                     .ThenByDescending(note => note.MidiNote))
        {
            var rect = GetNoteRect(note, geometry);
            if (!rect.Contains(point))
            {
                continue;
            }

            var resizeHitHeight = Math.Clamp(
                rect.Height * 0.25d,
                MinimumResizeHitPixels,
                MaximumResizeHitPixels);
            hit = new EditorNoteHit(
                note.Id,
                point.Y <= rect.Top + resizeHitHeight);
            return true;
        }

        return false;
    }

    public bool TryHitTestPreviewBoundary(Point point, out EditorPreviewBoundary boundary)
    {
        boundary = default;
        if (!TryGetGeometry(out var geometry) || point.Y < 0d || point.Y >= geometry.RollHeight)
        {
            return false;
        }

        var startDistance = _previewStartBeat is double startBeat
            ? Math.Abs(point.Y - BeatToY(startBeat, geometry))
            : double.PositiveInfinity;
        var endDistance = _previewEndBeat is double endBeat
            ? Math.Abs(point.Y - BeatToY(endBeat, geometry))
            : double.PositiveInfinity;
        var minimum = Math.Min(startDistance, endDistance);
        if (minimum > PreviewBoundaryHitPixels)
        {
            return false;
        }

        boundary = startDistance <= endDistance
            ? EditorPreviewBoundary.Start
            : EditorPreviewBoundary.End;
        return true;
    }

    public double PointToBeat(Point point)
    {
        if (!TryGetGeometry(out var geometry))
        {
            return _cursorBeat;
        }

        var beat = _cursorBeat + (geometry.CursorY - point.Y) / geometry.PixelsPerBeat;
        return Math.Clamp(beat, 0d, _score?.LengthBeats ?? Math.Max(0d, beat));
    }

    public int PointToMidi(Point point)
    {
        if (!TryGetGeometry(out var geometry))
        {
            return 60;
        }

        var pitch = geometry.MinPitch + (int)Math.Floor(point.X / geometry.KeyWidth);
        return Math.Clamp(pitch, geometry.MinPitch, geometry.MaxPitch);
    }

    public IReadOnlyList<int> GetNoteIdsInRectangle(Rect rectangle)
    {
        if (_score is null || !TryGetGeometry(out var geometry))
        {
            return Array.Empty<int>();
        }

        return _score.Notes
            .Where(note => rectangle.IntersectsWith(GetNoteRect(note, geometry)))
            .Select(note => note.Id)
            .ToArray();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(BackgroundBrush, null, new Rect(0d, 0d, ActualWidth, ActualHeight));
        if (_score is null || !TryGetGeometry(out var geometry))
        {
            return;
        }

        DrawPitchGuides(drawingContext, geometry);
        DrawGrid(drawingContext, geometry);
        DrawNotes(drawingContext, geometry);
        DrawTransport(drawingContext, geometry);
        DrawKeyboard(drawingContext, geometry);

        if (_selectionRectangle is Rect selection)
        {
            drawingContext.DrawRectangle(SelectionFillBrush, SelectionPen, selection);
        }

        DrawDragPreviewLabel(drawingContext, geometry);
    }

    private void DrawPitchGuides(DrawingContext context, EditorGeometry geometry)
    {
        for (var midi = geometry.MinPitch; midi <= geometry.MaxPitch; midi++)
        {
            var x = (midi - geometry.MinPitch) * geometry.KeyWidth;
            var pitchClass = PositiveModulo(midi, 12);

            foreach (var region in _scaleGuideRegions)
            {
                if (!region.PitchClasses.Contains(pitchClass))
                {
                    continue;
                }

                DrawTimedPitchGuide(
                    context,
                    geometry,
                    x,
                    region.StartBeat,
                    region.EndBeat,
                    region.RootPitchClass == pitchClass
                        ? ScaleRootGuideBrush
                        : ScaleToneGuideBrush);
            }

            foreach (var region in _chordGuideRegions)
            {
                if (!region.PitchClasses.Contains(pitchClass))
                {
                    continue;
                }

                DrawTimedPitchGuide(
                    context,
                    geometry,
                    x,
                    region.StartBeat,
                    region.EndBeat,
                    ChordToneGuideBrush);
            }

            if (_dragPreviewMidiNote == midi)
            {
                context.DrawRectangle(
                    DragPitchLaneBrush,
                    null,
                    new Rect(x, 0d, geometry.KeyWidth, geometry.RollHeight));
            }

            if (midi <= geometry.MinPitch)
            {
                continue;
            }

            var pen = midi % 12 == 0 ? OctaveDivisionPen : PitchDivisionPen;
            context.DrawLine(pen, new Point(x, 0d), new Point(x, geometry.RollHeight));
        }
    }

    private static void DrawTimedPitchGuide(
        DrawingContext context,
        EditorGeometry geometry,
        double x,
        double startBeat,
        double endBeat,
        Brush brush)
    {
        var startY =
            geometry.CursorY - (startBeat - geometry.CursorBeat) * geometry.PixelsPerBeat;
        var endY =
            geometry.CursorY - (endBeat - geometry.CursorBeat) * geometry.PixelsPerBeat;
        var top = Math.Clamp(Math.Min(startY, endY), 0d, geometry.RollHeight);
        var bottom = Math.Clamp(Math.Max(startY, endY), 0d, geometry.RollHeight);
        if (bottom <= top)
        {
            return;
        }

        context.DrawRectangle(
            brush,
            null,
            new Rect(x, top, geometry.KeyWidth, bottom - top));
    }

    private void DrawGrid(DrawingContext context, EditorGeometry geometry)
    {
        var topBeat = PointToBeat(new Point(0d, 0d));
        var bottomBeat = PointToBeat(new Point(0d, geometry.RollHeight));
        var gridStep = Math.Max(
            EditableMusicScore.TickToBeat(1L),
            EditableMusicScore.TickToBeat(GridTicks));
        var firstGridIndex = (long)Math.Floor(bottomBeat / gridStep);
        var lastGridIndex = (long)Math.Ceiling(topBeat / gridStep);
        for (var index = firstGridIndex; index <= lastGridIndex; index++)
        {
            var beat = index * gridStep;
            var nearestWholeBeat = Math.Round(beat);
            if (Math.Abs(beat - nearestWholeBeat) <= ScoreTiming.EventBeatTolerance)
            {
                continue;
            }

            var y = BeatToY(beat, geometry);
            context.DrawLine(SubdivisionPen, new Point(0d, y), new Point(ActualWidth, y));
        }

        var firstBeat = (int)Math.Floor(bottomBeat);
        var lastBeat = (int)Math.Ceiling(topBeat);
        for (var beat = firstBeat; beat <= lastBeat; beat++)
        {
            var y = BeatToY(beat, geometry);
            context.DrawLine(BeatPen, new Point(0d, y), new Point(ActualWidth, y));
        }

        foreach (var measure in _score!.Measures)
        {
            if (measure.StartBeat < bottomBeat - ScoreTiming.EventBeatTolerance
                || measure.StartBeat > topBeat + ScoreTiming.EventBeatTolerance)
            {
                continue;
            }

            var y = BeatToY(measure.StartBeat, geometry);
            context.DrawLine(MeasurePen, new Point(0d, y), new Point(ActualWidth, y));
        }
    }

    private void DrawNotes(DrawingContext context, EditorGeometry geometry)
    {
        foreach (var note in _score!.Notes)
        {
            if (note.MidiNote < geometry.MinPitch || note.MidiNote > geometry.MaxPitch)
            {
                continue;
            }

            var rect = GetNoteRect(note, geometry);
            if (rect.Bottom < 0d || rect.Top > geometry.RollHeight)
            {
                continue;
            }

            var isBlackKey = PianoKeyVisualHelper.IsBlackKey(note.MidiNote);
            var selected = _selectedNoteIds.Contains(note.Id);
            var brush = GetNoteBrush(note, selected, isBlackKey);
            context.DrawRoundedRectangle(brush, NotePen, rect, 3d, 3d);

            if (selected && rect.Width >= 6d)
            {
                context.DrawLine(
                    ResizeHandlePen,
                    new Point(rect.Left + 2d, rect.Top + 1d),
                    new Point(rect.Right - 2d, rect.Top + 1d));
            }

            if (note.Finger is >= 1 and <= 5 && rect.Height >= 14d && rect.Width >= 12d)
            {
                var text = new FormattedText(
                    note.Finger.ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    9d,
                    isBlackKey ? Brushes.White : BackgroundBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                context.DrawText(
                    text,
                    new Point(
                        rect.X + Math.Max(1d, (rect.Width - text.Width) / 2d),
                        rect.Y + 2d));
            }
        }
    }

    private void DrawTransport(DrawingContext context, EditorGeometry geometry)
    {
        if (_previewStartBeat is double startBeat && _previewEndBeat is double endBeat)
        {
            var startY = BeatToY(startBeat, geometry);
            var endY = BeatToY(endBeat, geometry);
            var top = Math.Clamp(Math.Min(startY, endY), 0d, geometry.RollHeight);
            var bottom = Math.Clamp(Math.Max(startY, endY), 0d, geometry.RollHeight);
            if (bottom > top)
            {
                context.DrawRectangle(
                    PreviewRangeBrush,
                    null,
                    new Rect(0d, top, ActualWidth, bottom - top));
            }
        }

        DrawPreviewBoundary(context, geometry, _previewStartBeat, "A", PreviewStartPen, PreviewStartBrush);
        DrawPreviewBoundary(context, geometry, _previewEndBeat, "B", PreviewEndPen, PreviewEndBrush);

        context.DrawLine(
            CursorPen,
            new Point(0d, geometry.CursorY),
            new Point(ActualWidth, geometry.CursorY));

        if (_previewBeat is double previewBeat)
        {
            var y = BeatToY(previewBeat, geometry);
            if (y >= 0d && y <= geometry.RollHeight)
            {
                context.DrawLine(PreviewPen, new Point(0d, y), new Point(ActualWidth, y));
            }
        }
    }

    private void DrawPreviewBoundary(
        DrawingContext context,
        EditorGeometry geometry,
        double? beat,
        string label,
        Pen pen,
        Brush brush)
    {
        if (beat is not double boundaryBeat)
        {
            return;
        }

        var y = BeatToY(boundaryBeat, geometry);
        if (y < 0d || y > geometry.RollHeight)
        {
            return;
        }

        context.DrawLine(pen, new Point(0d, y), new Point(ActualWidth, y));
        var text = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            10d,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(
            text,
            new Point(
                Math.Max(2d, ActualWidth - text.Width - 6d),
                Math.Max(0d, y - text.Height - 2d)));
    }

    private void DrawKeyboard(DrawingContext context, EditorGeometry geometry)
    {
        var top = geometry.RollHeight;
        for (var midi = geometry.MinPitch; midi <= geometry.MaxPitch; midi++)
        {
            var x = (midi - geometry.MinPitch) * geometry.KeyWidth;
            var rect = new Rect(x, top, Math.Ceiling(geometry.KeyWidth) + 0.5d, KeyboardHeight);
            var isPlayable = _connectedKeyboardRange?.Contains(midi) ?? true;
            var isBlack = IsBlackKey(midi);
            var keyBrush = _dragPreviewMidiNote == midi
                ? DragTargetKeyBrush
                : _activeMidiNotes.Contains(midi)
                    ? ActiveKeyBrush
                    : isPlayable
                        ? isBlack ? BlackKeyBrush : WhiteKeyBrush
                        : isBlack ? UnavailableBlackKeyBrush : UnavailableWhiteKeyBrush;
            context.DrawRectangle(keyBrush, KeyPen, rect);

            if (midi % 12 == 0 && geometry.KeyWidth >= 18d)
            {
                var text = new FormattedText(
                    MidiPitch.ToName(midi),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    9d,
                    isPlayable ? BackgroundBrush : MeasureBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                context.DrawText(text, new Point(rect.X + 2d, rect.Bottom - text.Height - 3d));
            }
        }

        if (_connectedKeyboardRange is PracticeMidiRange range)
        {
            DrawKeyboardRangeBoundary(context, geometry, range.LowestMidi, top);
            DrawKeyboardRangeBoundary(context, geometry, range.HighestMidi + 1, top);

            var label = new FormattedText(
                $"接続鍵盤 {MidiPitch.ToName(range.LowestMidi)}–{MidiPitch.ToName(range.HighestMidi)}",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                10d,
                MeasureBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            context.DrawText(label, new Point(6d, top + 4d));
        }
    }

    private void DrawDragPreviewLabel(DrawingContext context, EditorGeometry geometry)
    {
        if (_dragPreviewMidiNote is not int midiNote
            || _dragPreviewPoint is not Point point
            || midiNote < geometry.MinPitch
            || midiNote > geometry.MaxPitch)
        {
            return;
        }

        var text = new FormattedText(
            $"{MidiPitch.ToName(midiNote)}  MIDI {midiNote}",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            11d,
            DragLabelTextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        const double horizontalPadding = 7d;
        const double verticalPadding = 4d;
        var width = text.Width + horizontalPadding * 2d;
        var height = text.Height + verticalPadding * 2d;
        var x = Math.Clamp(point.X + 12d, 2d, Math.Max(2d, ActualWidth - width - 2d));
        var y = Math.Clamp(point.Y - height - 10d, 2d, Math.Max(2d, geometry.RollHeight - height - 2d));
        var rect = new Rect(x, y, width, height);
        context.DrawRoundedRectangle(
            DragLabelBackgroundBrush,
            DragLabelBorderPen,
            rect,
            5d,
            5d);
        context.DrawText(text, new Point(x + horizontalPadding, y + verticalPadding));
    }

    private static void DrawKeyboardRangeBoundary(
        DrawingContext context,
        EditorGeometry geometry,
        int boundaryMidi,
        double keyboardTop)
    {
        if (boundaryMidi < geometry.MinPitch || boundaryMidi > geometry.MaxPitch + 1)
        {
            return;
        }

        var x = (boundaryMidi - geometry.MinPitch) * geometry.KeyWidth;
        context.DrawLine(
            KeyboardRangeBoundaryPen,
            new Point(x, keyboardTop),
            new Point(x, keyboardTop + KeyboardHeight));
    }

    private Rect GetNoteRect(ScoreNote note, EditorGeometry geometry)
    {
        var x = (note.MidiNote - geometry.MinPitch) * geometry.KeyWidth + 1d;
        var endY = BeatToY(note.EndBeat, geometry);
        var startY = BeatToY(note.StartBeat, geometry);
        var rawHeight = Math.Max(5d, startY - endY);
        var gap = rawHeight >= 8d ? 1d : 0d;
        return new Rect(
            x,
            endY + gap / 2d,
            Math.Max(2d, geometry.KeyWidth - 2d),
            Math.Max(5d, rawHeight - gap));
    }

    private double BeatToY(double beat, EditorGeometry geometry)
        => geometry.CursorY - (beat - _cursorBeat) * geometry.PixelsPerBeat;

    private bool TryGetGeometry(out EditorGeometry geometry)
    {
        geometry = default;
        if (ActualWidth <= 0d || ActualHeight <= KeyboardHeight + 20d)
        {
            return false;
        }

        EnsurePitchViewportInitialized();
        var pitchCount = _visiblePitchHighestMidi - _visiblePitchLowestMidi + 1;
        if (pitchCount <= 0)
        {
            return false;
        }

        var rollHeight = ActualHeight - KeyboardHeight;
        geometry = new EditorGeometry(
            _visiblePitchLowestMidi,
            _visiblePitchHighestMidi,
            ActualWidth / pitchCount,
            rollHeight,
            rollHeight * CursorVerticalRatio,
            rollHeight / _visibleBeats,
            _cursorBeat);
        return true;
    }

    private PracticeMidiRange GetPitchUniverse()
    {
        var scoreMinPitch = Math.Max(0, (_score?.MinMidiNote ?? 60) - 2);
        var scoreMaxPitch = Math.Min(127, (_score?.MaxMidiNote ?? 72) + 2);
        var minPitch = Math.Min(StandardPianoLowestMidi, scoreMinPitch);
        var maxPitch = Math.Max(StandardPianoHighestMidi, scoreMaxPitch);
        if (_connectedKeyboardRange is PracticeMidiRange range)
        {
            minPitch = Math.Min(minPitch, range.LowestMidi);
            maxPitch = Math.Max(maxPitch, range.HighestMidi);
        }

        minPitch = Math.Clamp(minPitch, 0, 127);
        maxPitch = Math.Clamp(maxPitch, minPitch, 127);
        return new PracticeMidiRange(minPitch, maxPitch);
    }

    private void EnsurePitchViewportInitialized()
    {
        if (_pitchViewportInitialized)
        {
            return;
        }

        var universe = GetPitchUniverse();
        _visiblePitchLowestMidi = universe.LowestMidi;
        _visiblePitchHighestMidi = universe.HighestMidi;
        _pitchViewportInitialized = true;
    }

    private void ClampPitchViewportToUniverse()
    {
        if (!_pitchViewportInitialized)
        {
            return;
        }

        var universe = GetPitchUniverse();
        var universeCount = universe.HighestMidi - universe.LowestMidi + 1;
        var currentCount = Math.Clamp(
            _visiblePitchHighestMidi - _visiblePitchLowestMidi + 1,
            1,
            universeCount);
        var maximumLowest = universe.HighestMidi - currentCount + 1;
        var lowest = Math.Clamp(_visiblePitchLowestMidi, universe.LowestMidi, maximumLowest);
        SetPitchViewport(lowest, lowest + currentCount - 1);
    }

    private void SetPitchViewport(int lowestMidi, int highestMidi)
    {
        var universe = GetPitchUniverse();
        var lowest = Math.Clamp(lowestMidi, universe.LowestMidi, universe.HighestMidi);
        var highest = Math.Clamp(highestMidi, lowest, universe.HighestMidi);
        if (_pitchViewportInitialized
            && _visiblePitchLowestMidi == lowest
            && _visiblePitchHighestMidi == highest)
        {
            return;
        }

        _visiblePitchLowestMidi = lowest;
        _visiblePitchHighestMidi = highest;
        _pitchViewportInitialized = true;
        InvalidateVisual();
        PitchViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Brush GetNoteBrush(
        ScoreNote note,
        bool selected,
        bool isBlackKey)
    {
        if (selected)
        {
            return isBlackKey
                ? BlackKeySelectedBrush
                : SelectedBrush;
        }

        if (note.Hand == Hand.Right)
        {
            return isBlackKey
                ? BlackKeyRightHandBrush
                : RightHandBrush;
        }

        return isBlackKey
            ? BlackKeyLeftHandBrush
            : LeftHandBrush;
    }

    private static object CoerceGridTicks(DependencyObject d, object baseValue)
    {
        var ticks = baseValue is long value ? value : 240L;
        return Math.Clamp(ticks, 1L, EditableMusicScore.TicksPerQuarter * 4L);
    }

    private static bool IsBlackKey(int midiNote)
        => midiNote % 12 is 1 or 3 or 6 or 8 or 10;

    private static int PositiveModulo(int value, int modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

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

    private readonly record struct EditorGeometry(
        int MinPitch,
        int MaxPitch,
        double KeyWidth,
        double RollHeight,
        double CursorY,
        double PixelsPerBeat,
        double CursorBeat);
}
