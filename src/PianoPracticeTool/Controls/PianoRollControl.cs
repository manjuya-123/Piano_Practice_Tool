using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PianoPracticeTool.Core;

namespace PianoPracticeTool.Controls;

public sealed class PianoRollControl : FrameworkElement
{
    private const double KeyboardHeight = 86d;
    private const double StrikeZoneHeight = 14d;
    private const double BaseVisibleBeats = 8d;
    private const double PreRollBeats = 64d;
    private const double ScoreTileBeats = 16d;
    private const double BeatCoordinateTolerance = 0.000001d;
    private const int AttachedScoreTileCount = 6;
    private const int ScoreTileLookBehindCount = 2;

    private static readonly Brush BackgroundBrush = CreateFrozenBrush(Color.FromRgb(8, 10, 15));
    private static readonly Brush BeatGridBrush = CreateFrozenBrush(Color.FromArgb(28, 255, 255, 255));
    private static readonly Brush BarGridBrush = CreateFrozenBrush(Color.FromArgb(82, 255, 255, 255));
    private static readonly Brush MeasureLabelBrush = CreateFrozenBrush(Color.FromArgb(145, 220, 225, 235));
    private static readonly Brush ExpectedNoteBrush = CreateFrozenBrush(Color.FromRgb(255, 209, 102));
    private static readonly Brush BlackKeyExpectedNoteBrush = CreateFrozenBrush(Color.FromRgb(96, 67, 16));
    private static readonly Brush AccompanimentNoteBrush = CreateFrozenBrush(Color.FromRgb(55, 60, 70));
    private static readonly Brush BlackKeyAccompanimentNoteBrush = CreateFrozenBrush(Color.FromRgb(16, 18, 22));
    private static readonly Brush RightHandNoteBrush = CreateFrozenBrush(Color.FromRgb(76, 201, 240));
    private static readonly Brush BlackKeyRightHandNoteBrush = CreateFrozenBrush(Color.FromRgb(18, 72, 96));
    private static readonly Brush LeftHandNoteBrush = CreateFrozenBrush(Color.FromRgb(255, 107, 129));
    private static readonly Brush BlackKeyLeftHandNoteBrush = CreateFrozenBrush(Color.FromRgb(104, 25, 42));
    private static readonly Brush NeutralNoteBrush = CreateFrozenBrush(Color.FromRgb(102, 118, 255));
    private static readonly Brush BlackKeyNeutralNoteBrush = CreateFrozenBrush(Color.FromRgb(38, 48, 112));
    private static readonly Brush StrikeZoneBrush = CreateFrozenBrush(Color.FromRgb(20, 24, 33));
    private static readonly Brush ActiveKeyBrush = CreateFrozenBrush(Color.FromRgb(85, 214, 138));
    private static readonly Brush ExpectedKeyGuideBrush = CreateFrozenBrush(Color.FromArgb(28, 102, 118, 255));
    private static readonly Brush BlackKeyBrush = CreateFrozenBrush(Color.FromRgb(29, 32, 39));
    private static readonly Brush WhiteKeyBrush = CreateFrozenBrush(Color.FromRgb(231, 234, 239));
    private static readonly Brush UnavailableBlackKeyBrush = CreateFrozenBrush(Color.FromRgb(12, 14, 18));
    private static readonly Brush UnavailableWhiteKeyBrush = CreateFrozenBrush(Color.FromRgb(88, 93, 104));

    private static readonly Pen BeatGridPen = CreateFrozenPen(BeatGridBrush, 1d);
    private static readonly Pen BarGridPen = CreateFrozenPen(BarGridBrush, 1.4d);
    private static readonly Pen ExpectedNotePen = CreateFrozenPen(CreateFrozenBrush(Color.FromRgb(70, 52, 18)), 2d);
    private static readonly Pen NormalNotePen = CreateFrozenPen(CreateFrozenBrush(Color.FromArgb(90, 0, 0, 0)), 1d);
    private static readonly Pen TimingPen = CreateFrozenPen(CreateFrozenBrush(Color.FromRgb(238, 241, 247)), 2d);
    private static readonly Pen KeyboardEdgePen = CreateFrozenPen(CreateFrozenBrush(Color.FromRgb(74, 84, 104)), 2d);
    private static readonly Pen KeyBorderPen = CreateFrozenPen(CreateFrozenBrush(Color.FromRgb(72, 77, 88)), 1d);
    private static readonly Pen OctaveGuidePen = CreateFrozenPen(
        CreateFrozenBrush(Color.FromArgb(54, 164, 176, 204)),
        1.2d);
    private static readonly Pen PlayableRangeBoundaryPen = CreateFrozenPen(
        CreateFrozenBrush(Color.FromArgb(210, 102, 118, 255)),
        2d);
    private static readonly Pen ExpectedKeyGuidePen = CreateFrozenPen(
        CreateFrozenBrush(Color.FromArgb(176, 102, 118, 255)),
        1.5d);

    private static readonly Typeface LabelTypeface = new("Segoe UI");
    private static readonly Typeface FingerTypeface = new("Segoe UI Semibold");

    private readonly Dictionary<int, FormattedText> _measureLabelTextCache = new();
    private readonly Dictionary<int, FormattedText> _fingerLightTextCache = new();
    private readonly Dictionary<int, FormattedText> _fingerDarkTextCache = new();
    private readonly Dictionary<int, FormattedText> _keyLightTextCache = new();
    private readonly Dictionary<int, FormattedText> _keyDarkTextCache = new();
    private readonly Dictionary<int, DrawingVisual> _scoreTileCache = new();
    private readonly ContainerVisual _rollViewportVisual = new();
    private readonly ContainerVisual _rollContentVisual = new();
    private readonly ContainerVisual _scoreTileLayerVisual = new();
    private readonly DrawingVisual _expectedNotesVisual = new();
    private readonly DrawingVisual _staticForegroundVisual = new();
    private readonly DrawingVisual _keyboardStateVisual = new();
    private readonly TranslateTransform _rollTransform = new();
    private readonly VisualCollection _visualChildren;

    private MusicScore? _score;
    private IReadOnlyList<ScoreNote> _renderNotes = Array.Empty<ScoreNote>();
    private IReadOnlyList<ScoreMeasure> _renderMeasures = Array.Empty<ScoreMeasure>();
    private IReadOnlyDictionary<int, string> _measureHarmonyLabels =
        new Dictionary<int, string>();
    private IReadOnlyCollection<int> _expectedMidiNotes = Array.Empty<int>();
    private IReadOnlyCollection<int> _activeMidiNotes = Array.Empty<int>();
    private PracticeMidiRange _playableMidiRange = PracticeMidiRange.Full;
    private double? _currentBeat;
    private double _visibleRangeMultiplier = 1d;
    private double _maximumNoteDurationBeats;
    private double _textPixelsPerDip = -1d;
    private double _cachedWidth = -1d;
    private double _cachedHeight = -1d;
    private double _cachedRollHeight = -1d;
    private double _cachedKeyboardTop = -1d;
    private double _cachedKeyWidth = -1d;
    private double _cachedVisibleBeats = -1d;
    private double _cachedPixelsPerBeat = -1d;
    private int _cachedMinPitch;
    private int _cachedMaxPitch;
    private int _minimumScoreTileIndex;
    private int _maximumScoreTileIndex = -1;
    private int _attachedFirstTileIndex = int.MaxValue;
    private int _attachedLastTileIndex = int.MinValue;
    private bool _scoreTilesInvalid = true;
    private bool _foregroundCacheInvalid = true;
    private bool _expectedNotesCacheInvalid = true;
    private bool _keyboardStateCacheInvalid = true;
    private bool _showHandColors = true;
    private bool _showFingering = true;
    private bool _showExpectedKeyboardGuide = true;
    private PracticeHandMode _handMode = PracticeHandMode.Both;

    public PianoRollControl()
    {
        _rollContentVisual.Children.Add(_scoreTileLayerVisual);
        _rollContentVisual.Children.Add(_expectedNotesVisual);
        _rollContentVisual.Transform = _rollTransform;
        _rollViewportVisual.Children.Add(_rollContentVisual);

        _visualChildren = new VisualCollection(this)
        {
            _rollViewportVisual,
            _staticForegroundVisual,
            _keyboardStateVisual
        };
    }

    protected override int VisualChildrenCount => _visualChildren.Count;

    public MusicScore? Score
    {
        get => _score;
        set
        {
            _score = value;

            if (value is null)
            {
                _renderNotes = Array.Empty<ScoreNote>();
                _renderMeasures = Array.Empty<ScoreMeasure>();
                _measureHarmonyLabels = new Dictionary<int, string>();
                _maximumNoteDurationBeats = 0d;
            }
            else
            {
                _renderNotes = value.Notes
                    .OrderBy(note => note.StartBeat)
                    .ThenBy(note => note.MidiNote)
                    .ToArray();
                _renderMeasures = value.Measures
                    .OrderBy(measure => measure.StartBeat)
                    .ToArray();
                _measureHarmonyLabels = MusicTheoryAnalyzer.CreateMeasureHarmonyLabels(value);
                _maximumNoteDurationBeats = _renderNotes.Count == 0
                    ? 0d
                    : _renderNotes.Max(note => Math.Max(0d, note.DurationBeat));
            }

            _measureLabelTextCache.Clear();
            InvalidateAllVisualLayers();
        }
    }

    public IReadOnlyCollection<int> ExpectedMidiNotes
    {
        get => _expectedMidiNotes;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (MidiCollectionsEqual(_expectedMidiNotes, value))
            {
                return;
            }

            _expectedMidiNotes = value.ToArray();
            if (HasCachedGeometry())
            {
                DrawExpectedNotesLayer();
                DrawKeyboardStateLayer();
                _expectedNotesCacheInvalid = false;
                _keyboardStateCacheInvalid = false;
                return;
            }

            _expectedNotesCacheInvalid = true;
            _keyboardStateCacheInvalid = true;
            InvalidateVisual();
        }
    }

    public IReadOnlyCollection<int> ActiveMidiNotes
    {
        get => _activeMidiNotes;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (MidiCollectionsEqual(_activeMidiNotes, value))
            {
                return;
            }

            _activeMidiNotes = value.ToArray();
            if (HasCachedGeometry())
            {
                DrawKeyboardStateLayer();
                _keyboardStateCacheInvalid = false;
                return;
            }

            _keyboardStateCacheInvalid = true;
            InvalidateVisual();
        }
    }

    public PracticeMidiRange PlayableMidiRange
    {
        get => _playableMidiRange;
        set
        {
            if (_playableMidiRange == value)
            {
                return;
            }

            _playableMidiRange = value;
            InvalidateAllVisualLayers();
        }
    }

    public double? CurrentBeat
    {
        get => _currentBeat;
        set
        {
            if (_currentBeat == value)
            {
                return;
            }

            _currentBeat = value;
            UpdateRollTransform();
            UpdateAttachedScoreTileWindow();
        }
    }

    public double VisibleRangeMultiplier
    {
        get => _visibleRangeMultiplier;
        set
        {
            if (value is < 0.5d or > 2d)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (Math.Abs(_visibleRangeMultiplier - value) <= double.Epsilon)
            {
                return;
            }

            _visibleRangeMultiplier = value;
            InvalidateAllVisualLayers();
        }
    }

    public bool ShowHandColors
    {
        get => _showHandColors;
        set
        {
            if (_showHandColors == value)
            {
                return;
            }

            _showHandColors = value;
            InvalidateScoreDrawing();
        }
    }

    public bool ShowFingering
    {
        get => _showFingering;
        set
        {
            if (_showFingering == value)
            {
                return;
            }

            _showFingering = value;
            InvalidateScoreDrawing();
        }
    }

    public bool ShowExpectedKeyboardGuide
    {
        get => _showExpectedKeyboardGuide;
        set
        {
            if (_showExpectedKeyboardGuide == value)
            {
                return;
            }

            _showExpectedKeyboardGuide = value;
            if (HasCachedGeometry())
            {
                DrawKeyboardStateLayer();
                _keyboardStateCacheInvalid = false;
                return;
            }

            _keyboardStateCacheInvalid = true;
            InvalidateVisual();
        }
    }

    public PracticeHandMode HandMode
    {
        get => _handMode;
        set
        {
            if (_handMode == value)
            {
                return;
            }

            _handMode = value;
            InvalidateScoreDrawing();
        }
    }

    public void InvalidateScoreDrawing()
    {
        _scoreTilesInvalid = true;
        _expectedNotesCacheInvalid = true;
        InvalidateVisual();
    }

    protected override Visual GetVisualChild(int index)
    {
        if ((uint)index >= (uint)_visualChildren.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _visualChildren[index];
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        EnsureTextCacheDpi();

        drawingContext.DrawRectangle(
            BackgroundBrush,
            null,
            new Rect(0d, 0d, ActualWidth, ActualHeight));

        if (_score is null
            || _renderNotes.Count == 0
            || ActualWidth <= 0d
            || ActualHeight <= KeyboardHeight + StrikeZoneHeight)
        {
            ClearVisualLayers();
            return;
        }

        EnsureVisualLayers();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateAllVisualLayers();
    }

    private void EnsureVisualLayers()
    {
        var minPitch = Math.Max(
            0,
            Math.Min(_score!.MinMidiNote - 2, _playableMidiRange.LowestMidi));
        var maxPitch = Math.Min(
            127,
            Math.Max(_score.MaxMidiNote + 2, _playableMidiRange.HighestMidi));
        var pitchCount = Math.Max(1, maxPitch - minPitch + 1);
        var keyWidth = ActualWidth / pitchCount;
        var rollHeight = Math.Max(1d, ActualHeight - KeyboardHeight - StrikeZoneHeight);
        var keyboardTop = rollHeight + StrikeZoneHeight;

        // Tempo and playback speed control how quickly CurrentBeat advances.
        // They must not change how much of the score fits on screen.
        var visibleBeats = BaseVisibleBeats * _visibleRangeMultiplier;
        var pixelsPerBeat = rollHeight / visibleBeats;

        var geometryChanged = Math.Abs(_cachedWidth - ActualWidth) > 0.1d
            || Math.Abs(_cachedHeight - ActualHeight) > 0.1d
            || Math.Abs(_cachedPixelsPerBeat - pixelsPerBeat) > 0.001d
            || _cachedMinPitch != minPitch
            || _cachedMaxPitch != maxPitch;

        if (geometryChanged)
        {
            _cachedWidth = ActualWidth;
            _cachedHeight = ActualHeight;
            _cachedRollHeight = rollHeight;
            _cachedKeyboardTop = keyboardTop;
            _cachedKeyWidth = keyWidth;
            _cachedVisibleBeats = visibleBeats;
            _cachedPixelsPerBeat = pixelsPerBeat;
            _cachedMinPitch = minPitch;
            _cachedMaxPitch = maxPitch;
            _scoreTilesInvalid = true;
            _foregroundCacheInvalid = true;
            _expectedNotesCacheInvalid = true;
            _keyboardStateCacheInvalid = true;

            var rollClip = new RectangleGeometry(new Rect(0d, 0d, ActualWidth, rollHeight));
            if (rollClip.CanFreeze)
            {
                rollClip.Freeze();
            }

            _rollViewportVisual.Clip = rollClip;
        }

        if (_scoreTilesInvalid)
        {
            BuildScoreTileCache();
            _scoreTilesInvalid = false;
        }

        if (_foregroundCacheInvalid)
        {
            DrawStaticForegroundLayer();
            _foregroundCacheInvalid = false;
        }

        if (_expectedNotesCacheInvalid)
        {
            DrawExpectedNotesLayer();
            _expectedNotesCacheInvalid = false;
        }

        if (_keyboardStateCacheInvalid)
        {
            DrawKeyboardStateLayer();
            _keyboardStateCacheInvalid = false;
        }

        UpdateRollTransform();
        UpdateAttachedScoreTileWindow(force: true);
    }

    private void BuildScoreTileCache()
    {
        _scoreTileLayerVisual.Children.Clear();
        _scoreTileCache.Clear();
        _attachedFirstTileIndex = int.MaxValue;
        _attachedLastTileIndex = int.MinValue;

        if (_score is null || !HasCachedGeometry())
        {
            return;
        }

        _minimumScoreTileIndex = BeatToTileIndex(-PreRollBeats);
        var lastScoreBeat = Math.Max(
            0d,
            _score.LengthBeats - BeatCoordinateTolerance);
        _maximumScoreTileIndex = BeatToTileIndex(lastScoreBeat);

        for (var tileIndex = _minimumScoreTileIndex; tileIndex <= _maximumScoreTileIndex; tileIndex++)
        {
            var visual = new DrawingVisual();
            DrawScoreTile(visual, tileIndex);
            _scoreTileCache.Add(tileIndex, visual);
        }
    }

    private void UpdateAttachedScoreTileWindow(bool force = false)
    {
        if (!HasCachedGeometry() || _scoreTileCache.Count == 0)
        {
            return;
        }

        var viewportStartBeat = GetViewportStartBeat();
        var viewportEndBeat = viewportStartBeat + _cachedVisibleBeats;
        var firstVisibleTileIndex = BeatToTileIndex(viewportStartBeat);
        var lastVisibleTileIndex = BeatToTileIndex(
            Math.Max(
                viewportStartBeat,
                viewportEndBeat - BeatCoordinateTolerance));

        if (!force
            && _attachedFirstTileIndex <= _attachedLastTileIndex
            && firstVisibleTileIndex >= _attachedFirstTileIndex + 1
            && lastVisibleTileIndex <= _attachedLastTileIndex - 1)
        {
            return;
        }

        var desiredFirstTileIndex = Math.Clamp(
            firstVisibleTileIndex - ScoreTileLookBehindCount,
            _minimumScoreTileIndex,
            Math.Max(
                _minimumScoreTileIndex,
                _maximumScoreTileIndex - AttachedScoreTileCount + 1));
        var desiredLastTileIndex = Math.Min(
            _maximumScoreTileIndex,
            desiredFirstTileIndex + AttachedScoreTileCount - 1);

        if (!force
            && desiredFirstTileIndex == _attachedFirstTileIndex
            && desiredLastTileIndex == _attachedLastTileIndex)
        {
            return;
        }

        if (force || _attachedFirstTileIndex > _attachedLastTileIndex)
        {
            _scoreTileLayerVisual.Children.Clear();
            for (var tileIndex = desiredFirstTileIndex; tileIndex <= desiredLastTileIndex; tileIndex++)
            {
                _scoreTileLayerVisual.Children.Add(_scoreTileCache[tileIndex]);
            }
        }
        else
        {
            while (_attachedFirstTileIndex < desiredFirstTileIndex)
            {
                _scoreTileLayerVisual.Children.Remove(_scoreTileCache[_attachedFirstTileIndex]);
                _attachedFirstTileIndex++;
            }

            while (_attachedFirstTileIndex > desiredFirstTileIndex)
            {
                _attachedFirstTileIndex--;
                _scoreTileLayerVisual.Children.Add(_scoreTileCache[_attachedFirstTileIndex]);
            }

            while (_attachedLastTileIndex > desiredLastTileIndex)
            {
                _scoreTileLayerVisual.Children.Remove(_scoreTileCache[_attachedLastTileIndex]);
                _attachedLastTileIndex--;
            }

            while (_attachedLastTileIndex < desiredLastTileIndex)
            {
                _attachedLastTileIndex++;
                _scoreTileLayerVisual.Children.Add(_scoreTileCache[_attachedLastTileIndex]);
            }
        }

        _attachedFirstTileIndex = desiredFirstTileIndex;
        _attachedLastTileIndex = desiredLastTileIndex;
    }

    private void DrawScoreTile(DrawingVisual visual, int tileIndex)
    {
        var tileStartBeat = tileIndex * ScoreTileBeats;
        var tileEndBeat = tileStartBeat + ScoreTileBeats;
        var tileTop = -tileEndBeat * _cachedPixelsPerBeat;
        var tileBottom = -tileStartBeat * _cachedPixelsPerBeat;
        var tileClip = new RectangleGeometry(
            new Rect(
                0d,
                tileTop - 2d,
                ActualWidth,
                Math.Max(1d, tileBottom - tileTop + 4d)));

        using var context = visual.RenderOpen();
        context.PushClip(tileClip);

        var firstGridBeat = (int)Math.Ceiling(
            tileStartBeat - ScoreTiming.EventBeatTolerance);
        for (var beat = firstGridBeat; beat < tileEndBeat - BeatCoordinateTolerance; beat++)
        {
            if (beat < tileStartBeat - BeatCoordinateTolerance)
            {
                continue;
            }

            var y = -beat * _cachedPixelsPerBeat;
            context.DrawLine(BeatGridPen, new Point(0d, y), new Point(ActualWidth, y));
        }

        var measureIndex = FindFirstMeasureIndex(
            tileStartBeat - BeatCoordinateTolerance);
        for (; measureIndex < _renderMeasures.Count; measureIndex++)
        {
            var measure = _renderMeasures[measureIndex];
            if (measure.StartBeat >= tileEndBeat - BeatCoordinateTolerance)
            {
                break;
            }

            if (measure.StartBeat < tileStartBeat - BeatCoordinateTolerance)
            {
                continue;
            }

            var y = -measure.StartBeat * _cachedPixelsPerBeat;
            context.DrawLine(BarGridPen, new Point(0d, y), new Point(ActualWidth, y));
            DrawMeasureLabel(context, measure, y);
        }

        var noteSearchStartBeat = tileStartBeat
            - _maximumNoteDurationBeats
            - BeatCoordinateTolerance;
        var noteIndex = FindFirstNoteIndex(noteSearchStartBeat);
        for (; noteIndex < _renderNotes.Count; noteIndex++)
        {
            var note = _renderNotes[noteIndex];
            if (note.StartBeat >= tileEndBeat + BeatCoordinateTolerance)
            {
                break;
            }

            if (note.EndBeat <= tileStartBeat - BeatCoordinateTolerance)
            {
                continue;
            }

            var rect = GetScoreNoteRect(note);
            var isPracticedHand = IsPracticedHand(note.Hand);
            context.DrawRoundedRectangle(
                GetBaseNoteBrush(note, isPracticedHand),
                NormalNotePen,
                rect,
                3d,
                3d);

            if (isPracticedHand)
            {
                DrawFingerHint(
                    context,
                    note,
                    rect,
                    _cachedKeyWidth,
                    rect.Height,
                    useDarkText: false);
            }
        }

        context.Pop();
    }

    private void DrawExpectedNotesLayer()
    {
        using var context = _expectedNotesVisual.RenderOpen();
        if (_currentBeat is null
            || _expectedMidiNotes.Count == 0
            || !HasCachedGeometry())
        {
            return;
        }

        var earliestStartBeat = _currentBeat.Value - ScoreTiming.BeatGroupingTolerance;
        var noteIndex = FindFirstNoteIndex(earliestStartBeat);
        for (; noteIndex < _renderNotes.Count; noteIndex++)
        {
            var note = _renderNotes[noteIndex];
            if (note.StartBeat > _currentBeat.Value + ScoreTiming.BeatGroupingTolerance)
            {
                break;
            }

            if (!_expectedMidiNotes.Contains(note.MidiNote)
                || !IsPracticedHand(note.Hand))
            {
                continue;
            }

            var rect = GetScoreNoteRect(note);
            var isBlackKey = PianoKeyVisualHelper.IsBlackKey(note.MidiNote);
            context.DrawRoundedRectangle(
                isBlackKey ? BlackKeyExpectedNoteBrush : ExpectedNoteBrush,
                ExpectedNotePen,
                rect,
                3d,
                3d);
            DrawFingerHint(
                context,
                note,
                rect,
                _cachedKeyWidth,
                rect.Height,
                useDarkText: !isBlackKey);
        }
    }

    private void DrawStaticForegroundLayer()
    {
        using var context = _staticForegroundVisual.RenderOpen();
        context.DrawRectangle(
            StrikeZoneBrush,
            null,
            new Rect(0d, _cachedRollHeight, ActualWidth, StrikeZoneHeight));
        context.DrawLine(
            TimingPen,
            new Point(0d, _cachedRollHeight),
            new Point(ActualWidth, _cachedRollHeight));
        context.DrawLine(
            KeyboardEdgePen,
            new Point(0d, _cachedKeyboardTop),
            new Point(ActualWidth, _cachedKeyboardTop));

        for (var midi = _cachedMinPitch; midi <= _cachedMaxPitch; midi++)
        {
            if (midi % 12 != 0)
            {
                continue;
            }

            var octaveX = (midi - _cachedMinPitch) * _cachedKeyWidth;
            context.DrawLine(
                OctaveGuidePen,
                new Point(octaveX, 0d),
                new Point(octaveX, _cachedRollHeight));
        }

        for (var midi = _cachedMinPitch; midi <= _cachedMaxPitch; midi++)
        {
            var x = (midi - _cachedMinPitch) * _cachedKeyWidth;
            var isBlack = IsBlackKey(midi);
            var isPlayable = _playableMidiRange.Contains(midi);
            var rect = new Rect(
                x,
                _cachedKeyboardTop,
                Math.Ceiling(_cachedKeyWidth) + 0.5d,
                KeyboardHeight);
            var keyBrush = isPlayable
                ? isBlack ? BlackKeyBrush : WhiteKeyBrush
                : isBlack ? UnavailableBlackKeyBrush : UnavailableWhiteKeyBrush;
            context.DrawRectangle(keyBrush, KeyBorderPen, rect);

            if (midi % 12 == 0 && _cachedKeyWidth >= 18d)
            {
                DrawKeyLabel(context, midi, rect, useLightText: false);
            }
        }

        DrawPlayableRangeBoundaries(context);
    }

    private void DrawPlayableRangeBoundaries(DrawingContext context)
    {
        if (_playableMidiRange.LowestMidi > _cachedMinPitch
            && _playableMidiRange.LowestMidi <= _cachedMaxPitch)
        {
            var x = (_playableMidiRange.LowestMidi - _cachedMinPitch) * _cachedKeyWidth;
            context.DrawLine(
                PlayableRangeBoundaryPen,
                new Point(x, _cachedKeyboardTop),
                new Point(x, ActualHeight));
        }

        var highestBoundaryMidi = _playableMidiRange.HighestMidi + 1;
        if (highestBoundaryMidi > _cachedMinPitch
            && highestBoundaryMidi <= _cachedMaxPitch)
        {
            var x = (highestBoundaryMidi - _cachedMinPitch) * _cachedKeyWidth;
            context.DrawLine(
                PlayableRangeBoundaryPen,
                new Point(x, _cachedKeyboardTop),
                new Point(x, ActualHeight));
        }
    }

    private void DrawKeyboardStateLayer()
    {
        using var context = _keyboardStateVisual.RenderOpen();
        var hasExpectedGuide = _showExpectedKeyboardGuide && _expectedMidiNotes.Count > 0;
        if (!HasCachedGeometry()
            || (!hasExpectedGuide && _activeMidiNotes.Count == 0))
        {
            return;
        }

        for (var midi = _cachedMinPitch; midi <= _cachedMaxPitch; midi++)
        {
            var isActive = _activeMidiNotes.Contains(midi);
            var isExpected = _showExpectedKeyboardGuide && _expectedMidiNotes.Contains(midi);
            if (!isActive && !isExpected)
            {
                continue;
            }

            var x = (midi - _cachedMinPitch) * _cachedKeyWidth;
            var rect = new Rect(
                x,
                _cachedKeyboardTop,
                Math.Ceiling(_cachedKeyWidth) + 0.5d,
                KeyboardHeight);

            if (isActive)
            {
                context.DrawRectangle(ActiveKeyBrush, KeyBorderPen, rect);
            }
            else
            {
                var horizontalInset = Math.Min(3d, Math.Max(1d, rect.Width * 0.15d));
                var guideRect = new Rect(
                    rect.X + horizontalInset,
                    rect.Y + 3d,
                    Math.Max(1d, rect.Width - horizontalInset * 2d),
                    Math.Max(1d, rect.Height - 6d));
                context.DrawRoundedRectangle(
                    ExpectedKeyGuideBrush,
                    ExpectedKeyGuidePen,
                    guideRect,
                    2d,
                    2d);
            }

            if (midi % 12 == 0 && _cachedKeyWidth >= 18d)
            {
                DrawKeyLabel(context, midi, rect, useLightText: isActive);
            }
        }
    }

    private Rect GetScoreNoteRect(ScoreNote note)
    {
        var x = (note.MidiNote - _cachedMinPitch) * _cachedKeyWidth;
        var top = -note.EndBeat * _cachedPixelsPerBeat;
        var bottom = -note.StartBeat * _cachedPixelsPerBeat;
        var noteHeight = Math.Max(5d, bottom - top);
        return new Rect(
            x + 1d,
            top,
            Math.Max(2d, _cachedKeyWidth - 2d),
            noteHeight);
    }

    private void UpdateRollTransform()
    {
        if (!HasCachedGeometry())
        {
            return;
        }

        _rollTransform.Y = _cachedRollHeight
            + GetViewportStartBeat() * _cachedPixelsPerBeat;
    }

    private double GetViewportStartBeat()
        => _currentBeat ?? -_cachedVisibleBeats / 3d;

    private void InvalidateAllVisualLayers()
    {
        _cachedWidth = -1d;
        _cachedHeight = -1d;
        _cachedRollHeight = -1d;
        _cachedKeyboardTop = -1d;
        _cachedKeyWidth = -1d;
        _cachedVisibleBeats = -1d;
        _cachedPixelsPerBeat = -1d;
        _scoreTilesInvalid = true;
        _foregroundCacheInvalid = true;
        _expectedNotesCacheInvalid = true;
        _keyboardStateCacheInvalid = true;
        InvalidateVisual();
    }

    private void ClearVisualLayers()
    {
        _scoreTileLayerVisual.Children.Clear();
        _scoreTileCache.Clear();
        _attachedFirstTileIndex = int.MaxValue;
        _attachedLastTileIndex = int.MinValue;
        ClearDrawingVisual(_expectedNotesVisual);
        ClearDrawingVisual(_staticForegroundVisual);
        ClearDrawingVisual(_keyboardStateVisual);
    }

    private bool HasCachedGeometry()
        => _score is not null
            && _cachedRollHeight > 0d
            && _cachedKeyWidth > 0d
            && _cachedVisibleBeats > 0d
            && _cachedPixelsPerBeat > 0d;

    private static int BeatToTileIndex(double beat)
        => (int)Math.Floor(beat / ScoreTileBeats);

    private static bool MidiCollectionsEqual(
        IReadOnlyCollection<int> left,
        IReadOnlyCollection<int> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var midiNote in left)
        {
            if (!right.Contains(midiNote))
            {
                return false;
            }
        }

        return true;
    }

    private static void ClearDrawingVisual(DrawingVisual visual)
    {
        using var context = visual.RenderOpen();
    }

    private void DrawMeasureLabel(
        DrawingContext drawingContext,
        ScoreMeasure measure,
        double y)
    {
        var label = _measureHarmonyLabels.TryGetValue(measure.Number, out var harmony)
            && !string.IsNullOrWhiteSpace(harmony)
                ? $"{measure.Number}  {harmony}"
                : measure.Number.ToString(CultureInfo.InvariantCulture);
        var text = GetOrCreateFormattedText(
            _measureLabelTextCache,
            measure.Number,
            label,
            LabelTypeface,
            9d,
            MeasureLabelBrush);
        drawingContext.DrawText(
            text,
            new Point(
                5d,
                y - text.Height - 2d));
    }

    private void DrawFingerHint(
        DrawingContext drawingContext,
        ScoreNote note,
        Rect rect,
        double keyWidth,
        double noteHeight,
        bool useDarkText)
    {
        if (!_showFingering || keyWidth < 15d || noteHeight < 14d || note.Finger <= 0)
        {
            return;
        }

        var cache = useDarkText ? _fingerDarkTextCache : _fingerLightTextCache;
        var foreground = useDarkText ? Brushes.Black : Brushes.White;
        var text = GetOrCreateFormattedText(
            cache,
            note.Finger,
            note.Finger.ToString(CultureInfo.InvariantCulture),
            FingerTypeface,
            10d,
            foreground);
        drawingContext.DrawText(
            text,
            new Point(
                rect.X + Math.Max(2d, (rect.Width - text.Width) / 2d),
                rect.Bottom - text.Height - 2d));
    }

    private void DrawKeyLabel(
        DrawingContext drawingContext,
        int midi,
        Rect rect,
        bool useLightText)
    {
        var cache = useLightText ? _keyLightTextCache : _keyDarkTextCache;
        var foreground = useLightText ? Brushes.White : Brushes.DimGray;
        var text = GetOrCreateFormattedText(
            cache,
            midi,
            MidiPitch.ToName(midi),
            LabelTypeface,
            9d,
            foreground);
        drawingContext.DrawText(
            text,
            new Point(
                rect.X + Math.Max(2d, (rect.Width - text.Width) / 2d),
                rect.Bottom - text.Height - 5d));
    }

    private FormattedText GetOrCreateFormattedText(
        Dictionary<int, FormattedText> cache,
        int key,
        string text,
        Typeface typeface,
        double emSize,
        Brush foreground)
    {
        if (cache.TryGetValue(key, out var formattedText))
        {
            return formattedText;
        }

        formattedText = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            emSize,
            foreground,
            _textPixelsPerDip);
        cache[key] = formattedText;
        return formattedText;
    }

    private void EnsureTextCacheDpi()
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (Math.Abs(_textPixelsPerDip - pixelsPerDip) <= double.Epsilon)
        {
            return;
        }

        _textPixelsPerDip = pixelsPerDip;
        _measureLabelTextCache.Clear();
        _fingerLightTextCache.Clear();
        _fingerDarkTextCache.Clear();
        _keyLightTextCache.Clear();
        _keyDarkTextCache.Clear();
        InvalidateAllVisualLayers();
    }

    private int FindFirstNoteIndex(double minimumStartBeat)
    {
        var low = 0;
        var high = _renderNotes.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_renderNotes[middle].StartBeat < minimumStartBeat)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private int FindFirstMeasureIndex(double minimumStartBeat)
    {
        var low = 0;
        var high = _renderMeasures.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_renderMeasures[middle].StartBeat < minimumStartBeat)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private Brush GetBaseNoteBrush(ScoreNote note, bool isPracticedHand)
    {
        var isBlackKey = PianoKeyVisualHelper.IsBlackKey(note.MidiNote);
        if (!isPracticedHand)
        {
            return isBlackKey
                ? BlackKeyAccompanimentNoteBrush
                : AccompanimentNoteBrush;
        }

        if (!_showHandColors)
        {
            return isBlackKey
                ? BlackKeyNeutralNoteBrush
                : NeutralNoteBrush;
        }

        if (note.Hand == Hand.Right)
        {
            return isBlackKey
                ? BlackKeyRightHandNoteBrush
                : RightHandNoteBrush;
        }

        return isBlackKey
            ? BlackKeyLeftHandNoteBrush
            : LeftHandNoteBrush;
    }

    private bool IsPracticedHand(Hand hand)
    {
        return _handMode == PracticeHandMode.Both
            || (_handMode == PracticeHandMode.Right && hand == Hand.Right)
            || (_handMode == PracticeHandMode.Left && hand == Hand.Left);
    }

    private static bool IsBlackKey(int midi)
        => midi % 12 is 1 or 3 or 6 or 8 or 10;

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreateFrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
