using System.Text;
using PianoPracticeTool.Core;

namespace PianoPracticeTool.Core.Editing;

public sealed record EditableClipboardNote(
    long RelativeStartTick,
    long DurationTick,
    int MidiNote,
    int Staff,
    string Voice,
    Hand Hand,
    int Finger);

public sealed class ScoreEditSession
{
    private readonly Stack<EditableMusicScore> _undoStack = new();
    private readonly Stack<EditableMusicScore> _redoStack = new();
    private string _savedSignature;

    public ScoreEditSession(
        EditableMusicScore score,
        EditableMusicScore? savedScore = null)
    {
        Score = score?.Clone()
            ?? throw new ArgumentNullException(
                nameof(score));
        Validate(Score);

        var baseline =
            savedScore?.Clone()
            ?? Score.Clone();
        Validate(baseline);
        _savedSignature =
            CreateSignature(
                baseline);
    }

    public EditableMusicScore Score { get; private set; }

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    public bool IsDirty => !string.Equals(
        _savedSignature,
        CreateSignature(Score),
        StringComparison.Ordinal);

    public string CurrentSignature =>
        CreateSignature(
            Score);

    public void MarkSaved()
        => _savedSignature = CreateSignature(Score);

    public bool Undo()
    {
        if (_undoStack.Count == 0)
        {
            return false;
        }

        _redoStack.Push(Score.Clone());
        Score = _undoStack.Pop();
        Validate(Score);
        return true;
    }

    public bool Redo()
    {
        if (_redoStack.Count == 0)
        {
            return false;
        }

        _undoStack.Push(Score.Clone());
        Score = _redoStack.Pop();
        Validate(Score);
        return true;
    }

    public int InsertNote(
        int midiNote,
        long startTick,
        long durationTick,
        Hand hand,
        string voice = "1")
    {
        return Execute(score =>
        {
            var id = NextNoteId(score);
            score.Notes.Add(new EditableNote
            {
                Id = id,
                MidiNote = midiNote,
                StartTick = startTick,
                DurationTick = durationTick,
                Staff = hand == Hand.Right ? 1 : 2,
                Voice = NormalizeVoice(voice),
                Hand = hand,
                Finger = 0
            });
            return id;
        });
    }

    public void InsertRest(
        long startTick,
        long durationTick,
        Hand hand,
        string voice = "1")
    {
        var normalizedVoice = NormalizeVoice(voice);
        Execute(score =>
        {
            var staff = hand == Hand.Right ? 1 : 2;
            score.Rests.RemoveAll(rest =>
                rest.StartTick == startTick
                && rest.Staff == staff
                && string.Equals(
                    NormalizeVoice(rest.Voice),
                    normalizedVoice,
                    StringComparison.Ordinal));
            score.Rests.Add(new EditableRest(
                startTick,
                durationTick,
                staff,
                normalizedVoice));
        });
    }

    public bool RemoveRestAtTick(long tick, Hand hand)
    {
        var staff = hand == Hand.Right ? 1 : 2;
        var target = Score.Rests
            .Where(rest =>
                rest.Staff == staff
                && rest.StartTick <= tick
                && rest.EndTick > tick)
            .OrderByDescending(rest => rest.StartTick)
            .FirstOrDefault();
        if (target is null)
        {
            return false;
        }

        Execute(score => score.Rests.Remove(target));
        return true;
    }

    public void SetKeySignature(
        long tick,
        int fifths,
        string mode,
        string? scaleType = null)
    {
        var normalizedMode = NormalizeKeyMode(mode);
        var normalizedScaleType = string.IsNullOrWhiteSpace(scaleType)
            ? null
            : scaleType.Trim().ToLowerInvariant();
        Execute(score =>
        {
            score.KeySignatureEvents.RemoveAll(item => item.Tick == tick);
            score.KeySignatureEvents.Add(new EditableKeySignatureEvent(
                tick,
                fifths,
                normalizedMode,
                normalizedScaleType));
        });
    }

    public void SetHarmony(long tick, string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException(
                "コード名を入力してください。",
                nameof(symbol));
        }

        var normalizedSymbol = symbol.Trim();
        Execute(score =>
        {
            score.HarmonyEvents.RemoveAll(item => item.Tick == tick);
            score.HarmonyEvents.Add(new EditableHarmonyEvent(
                tick,
                normalizedSymbol));
        });
    }

    public bool RemoveHarmony(long tick)
    {
        if (!Score.HarmonyEvents.Any(item => item.Tick == tick))
        {
            return false;
        }

        Execute(score => score.HarmonyEvents.RemoveAll(item => item.Tick == tick));
        return true;
    }

    public void DeleteNotes(IReadOnlyCollection<int> noteIds)
    {
        ArgumentNullException.ThrowIfNull(noteIds);
        if (noteIds.Count == 0)
        {
            return;
        }

        Execute(score =>
        {
            score.Notes.RemoveAll(note => noteIds.Contains(note.Id));
        });
    }

    public void MoveNotes(
        IReadOnlyCollection<int> noteIds,
        long deltaTicks,
        int deltaMidi)
    {
        ArgumentNullException.ThrowIfNull(noteIds);
        if (noteIds.Count == 0 || (deltaTicks == 0L && deltaMidi == 0))
        {
            return;
        }

        Execute(score =>
        {
            foreach (var note in score.Notes.Where(note => noteIds.Contains(note.Id)))
            {
                note.StartTick += deltaTicks;
                note.MidiNote += deltaMidi;
            }
        });
    }

    public void MoveNotesByScaleStep(
        IReadOnlyCollection<int> noteIds,
        int direction)
    {
        ArgumentNullException.ThrowIfNull(
            noteIds);
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction),
                "Scale step direction must be -1 or 1.");
        }

        if (noteIds.Count == 0)
        {
            return;
        }

        Execute(score =>
        {
            var analysisScore =
                score.ToMusicScore();
            foreach (var note in score.Notes.Where(note =>
                         noteIds.Contains(note.Id)))
            {
                var beat =
                    EditableMusicScore.TickToBeat(
                        note.StartTick);
                var key =
                    MusicTheoryAnalyzer.AnalyzeKeyAt(
                        analysisScore,
                        beat)
                    ?? throw new InvalidOperationException(
                        "この位置のScaleを確認できません。先にScaleを設定してください。");
                var targetMidi =
                    FindAdjacentScalePitch(
                        note.MidiNote,
                        key.PitchClasses,
                        direction);
                note.MidiNote =
                    targetMidi;
            }
        });
    }

    public void ResizeNotes(
        IReadOnlyCollection<int> noteIds,
        long durationDeltaTicks)
    {
        ArgumentNullException.ThrowIfNull(noteIds);
        if (noteIds.Count == 0 || durationDeltaTicks == 0L)
        {
            return;
        }

        Execute(score =>
        {
            foreach (var note in score.Notes.Where(note => noteIds.Contains(note.Id)))
            {
                note.DurationTick += durationDeltaTicks;
            }
        });
    }

    public void UpdateNote(
        int noteId,
        int midiNote,
        long startTick,
        long durationTick,
        Hand hand,
        string voice,
        int finger)
    {
        Execute(score =>
        {
            var note = score.Notes.FirstOrDefault(item => item.Id == noteId)
                ?? throw new InvalidOperationException("選択ノートが見つかりません。");
            note.MidiNote = midiNote;
            note.StartTick = startTick;
            note.DurationTick = durationTick;
            note.Hand = hand;
            note.Staff = hand == Hand.Right ? 1 : 2;
            note.Voice = NormalizeVoice(voice);
            note.Finger = finger;
        });
    }

    public IReadOnlyList<int> SplitNotes(
        IReadOnlyCollection<int> noteIds,
        int parts)
    {
        ArgumentNullException.ThrowIfNull(noteIds);
        if (parts is not (2 or 3))
        {
            throw new ArgumentOutOfRangeException(nameof(parts));
        }

        if (noteIds.Count == 0)
        {
            return Array.Empty<int>();
        }

        return Execute(score =>
        {
            var selected = score.Notes
                .Where(note => noteIds.Contains(note.Id))
                .OrderBy(note => note.StartTick)
                .ThenBy(note => note.MidiNote)
                .ToArray();
            var resultIds = new List<int>();
            var nextId = NextNoteId(score);

            foreach (var note in selected)
            {
                if (note.DurationTick < EditableMusicScore.MinimumDurationTicks * parts)
                {
                    throw new InvalidOperationException(
                        "選択ノートが短すぎるため、この分割数では分割できません。");
                }

                var totalDuration = note.DurationTick;
                var baseDuration = totalDuration / parts;
                var remainder = totalDuration % parts;
                var cursor = note.StartTick;

                for (var partIndex = 0; partIndex < parts; partIndex++)
                {
                    var duration = baseDuration + (partIndex < remainder ? 1L : 0L);
                    if (partIndex == 0)
                    {
                        note.StartTick = cursor;
                        note.DurationTick = duration;
                        resultIds.Add(note.Id);
                    }
                    else
                    {
                        var split = note.Clone();
                        split.Id = nextId++;
                        split.StartTick = cursor;
                        split.DurationTick = duration;
                        score.Notes.Add(split);
                    }

                    cursor += duration;
                }
            }

            return (IReadOnlyList<int>)resultIds;
        });
    }

    public IReadOnlyList<EditableClipboardNote> CopyNotes(IReadOnlyCollection<int> noteIds)
    {
        ArgumentNullException.ThrowIfNull(noteIds);
        var selected = Score.Notes
            .Where(note => noteIds.Contains(note.Id))
            .OrderBy(note => note.StartTick)
            .ThenBy(note => note.MidiNote)
            .ToArray();
        if (selected.Length == 0)
        {
            return Array.Empty<EditableClipboardNote>();
        }

        var anchor = selected.Min(note => note.StartTick);
        return selected.Select(note => new EditableClipboardNote(
                note.StartTick - anchor,
                note.DurationTick,
                note.MidiNote,
                note.Staff,
                note.Voice,
                note.Hand,
                note.Finger))
            .ToArray();
    }

    public IReadOnlyList<int> PasteNotes(
        IReadOnlyList<EditableClipboardNote> clipboard,
        long targetTick)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        if (clipboard.Count == 0)
        {
            return Array.Empty<int>();
        }

        return Execute(score =>
        {
            var nextId = NextNoteId(score);
            var ids = new List<int>(clipboard.Count);
            foreach (var source in clipboard)
            {
                var note = new EditableNote
                {
                    Id = nextId++,
                    MidiNote = source.MidiNote,
                    StartTick = targetTick + source.RelativeStartTick,
                    DurationTick = source.DurationTick,
                    Staff = source.Staff,
                    Voice = NormalizeVoice(source.Voice),
                    Hand = source.Hand,
                    Finger = source.Finger
                };
                score.Notes.Add(note);
                ids.Add(note.Id);
            }

            return (IReadOnlyList<int>)ids;
        });
    }

    public void SetHand(IReadOnlyCollection<int> noteIds, Hand hand)
    {
        ArgumentNullException.ThrowIfNull(noteIds);
        if (noteIds.Count == 0)
        {
            return;
        }

        Execute(score =>
        {
            foreach (var note in score.Notes.Where(note => noteIds.Contains(note.Id)))
            {
                note.Hand = hand;
                note.Staff = hand == Hand.Right ? 1 : 2;
            }
        });
    }

    public void InsertMeasureBefore(int measureNumber)
        => InsertMeasure(measureNumber, insertAfter: false);

    public void InsertMeasureAfter(int measureNumber)
        => InsertMeasure(measureNumber, insertAfter: true);

    public void SetMeasureTimeSignature(
        int measureNumber,
        int beats,
        int beatType)
    {
        var newDurationTick =
            CalculateFullMeasureDurationTicks(
                beats,
                beatType);

        Execute(score =>
        {
            var target = FindMeasure(
                score,
                measureNumber);
            var oldEndTick = target.EndTick;
            var hasFollowingMeasure =
                score.Measures.Any(measure =>
                    measure.StartTick >= oldEndTick);
            var newEndTick = checked(
                target.StartTick + newDurationTick);
            var deltaTicks =
                newDurationTick - target.DurationTick;

            if (deltaTicks > 0L)
            {
                ExpandMeasureTimeline(
                    score,
                    oldEndTick,
                    deltaTicks);
            }
            else if (deltaTicks < 0L)
            {
                var tempoAtOldEnd =
                    FindTempoStateAtOrBefore(
                        score.TempoEvents,
                        oldEndTick);
                var keyAtOldEnd =
                    FindKeyStateAtOrBefore(
                        score.KeySignatureEvents,
                        oldEndTick);
                var harmonyAtOldEnd =
                    FindHarmonyStateAtOrBefore(
                        score.HarmonyEvents,
                        oldEndTick);

                DeleteTimelineSpan(
                    score,
                    newEndTick,
                    oldEndTick,
                    -deltaTicks);

                if (hasFollowingMeasure)
                {
                    PreserveTempoStateAtSeam(
                        score,
                        newEndTick,
                        tempoAtOldEnd);
                    PreserveKeyStateAtSeam(
                        score,
                        newEndTick,
                        keyAtOldEnd);
                    PreserveHarmonyStateAtSeam(
                        score,
                        newEndTick,
                        harmonyAtOldEnd);
                }
            }

            var targetIndex =
                score.Measures.FindIndex(
                    measure =>
                        measure.StartTick
                        == target.StartTick);
            if (targetIndex < 0)
            {
                throw new InvalidOperationException(
                    "拍子を変更する小節を確認できません。");
            }

            score.Measures[targetIndex] =
                score.Measures[targetIndex] with
                {
                    DurationTick =
                        newDurationTick,
                    Beats = beats,
                    BeatType = beatType
                };
            NormalizeMeasures(
                score.Measures);
        });
    }

    public void SetTempoAtMeasure(
        int measureNumber,
        double beatsPerMinute)
    {
        if (double.IsNaN(beatsPerMinute)
            || double.IsInfinity(beatsPerMinute)
            || beatsPerMinute <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(beatsPerMinute),
                "テンポは0より大きい値を指定してください。");
        }

        Execute(score =>
        {
            var target = FindMeasure(
                score,
                measureNumber);
            score.TempoEvents.RemoveAll(item =>
                item.Tick == target.StartTick);
            score.TempoEvents.Add(
                new EditableTempoEvent(
                    target.StartTick,
                    beatsPerMinute));
        });
    }

    public bool RemoveTempoAtMeasure(
        int measureNumber)
    {
        var target = FindMeasure(
            Score,
            measureNumber);
        if (target.StartTick == 0L
            || !Score.TempoEvents.Any(item =>
                item.Tick == target.StartTick))
        {
            return false;
        }

        Execute(score =>
        {
            var measure = FindMeasure(
                score,
                measureNumber);
            score.TempoEvents.RemoveAll(item =>
                item.Tick == measure.StartTick);
        });
        return true;
    }

    public void DeleteMeasure(int measureNumber)
    {
        Execute(score =>
        {
            if (score.Measures.Count <= 1)
            {
                throw new InvalidOperationException(
                    "最後の1小節は削除できません。");
            }

            var target = FindMeasure(
                score,
                measureNumber);
            var cutStart = target.StartTick;
            var cutEnd = target.EndTick;
            var cutDuration = target.DurationTick;

            var tempoAtEnd = FindTempoStateAtOrBefore(
                score.TempoEvents,
                cutEnd);
            var keyAtEnd = FindKeyStateAtOrBefore(
                score.KeySignatureEvents,
                cutEnd);
            var harmonyAtEnd = FindHarmonyStateAtOrBefore(
                score.HarmonyEvents,
                cutEnd);

            DeleteTimelineSpan(
                score,
                cutStart,
                cutEnd,
                cutDuration);

            if (cutStart < score.LengthTicks)
            {
                PreserveTempoStateAtSeam(
                    score,
                    cutStart,
                    tempoAtEnd);
                PreserveKeyStateAtSeam(
                    score,
                    cutStart,
                    keyAtEnd);
                PreserveHarmonyStateAtSeam(
                    score,
                    cutStart,
                    harmonyAtEnd);
            }
            else if (score.TempoEvents.Count == 0)
            {
                score.TempoEvents.Add(
                    new EditableTempoEvent(
                        0L,
                        tempoAtEnd?.BeatsPerMinute
                            ?? 120d));
            }
        });
    }

    public static void Validate(EditableMusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        ValidateMeasures(score.Measures);
        ValidateTempoEvents(score.TempoEvents);

        var scoreLength = score.LengthTicks;
        ValidateKeySignatureEvents(score.KeySignatureEvents, scoreLength);
        ValidateHarmonyEvents(score.HarmonyEvents, scoreLength);
        if (score.Notes.Select(note => note.Id).Distinct().Count() != score.Notes.Count)
        {
            throw new InvalidOperationException("ノートIDが重複しています。");
        }

        foreach (var note in score.Notes)
        {
            if (note.MidiNote is < 0 or > 127)
            {
                throw new InvalidOperationException("MIDIノート番号は0から127の範囲である必要があります。");
            }

            if (note.StartTick < 0L)
            {
                throw new InvalidOperationException("ノート開始位置を曲頭より前へ移動できません。");
            }

            if (note.DurationTick < EditableMusicScore.MinimumDurationTicks)
            {
                throw new InvalidOperationException("ノート長が短すぎます。");
            }

            if (note.EndTick > scoreLength)
            {
                throw new InvalidOperationException("ノートを既存の最終小節より後へ配置できません。");
            }

            if (note.Staff is < 0 or > 2)
            {
                throw new InvalidOperationException("ノートのstaff番号が不正です。");
            }

            if (note.Finger is < 0 or > 5)
            {
                throw new InvalidOperationException("指番号は0（未指定）または1から5の範囲で指定してください。");
            }
        }

        foreach (var rest in score.Rests)
        {
            if (rest.StartTick < 0L || rest.DurationTick <= 0L)
            {
                throw new InvalidOperationException("休符の開始位置または長さが不正です。");
            }

            if (rest.EndTick > scoreLength)
            {
                throw new InvalidOperationException("休符を既存の最終小節より後へ配置できません。");
            }

            if (rest.Staff is < 0 or > 2)
            {
                throw new InvalidOperationException("休符のstaff番号が不正です。");
            }

            if (string.IsNullOrWhiteSpace(rest.Voice))
            {
                throw new InvalidOperationException("休符のvoiceが空です。");
            }
        }

        foreach (var group in score.Notes.GroupBy(note => (note.MidiNote, Voice: NormalizeVoice(note.Voice))))
        {
            EditableNote? previous = null;
            foreach (var note in group.OrderBy(note => note.StartTick).ThenBy(note => note.EndTick))
            {
                if (previous is not null && note.StartTick < previous.EndTick)
                {
                    throw new InvalidOperationException(
                        $"{MidiPitch.ToName(note.MidiNote)} の同一voiceノートが時間的に重複しています。");
                }

                previous = note;
            }
        }
    }

    private static void ValidateMeasures(IReadOnlyList<EditableMeasure> measures)
    {
        if (measures.Count == 0)
        {
            throw new InvalidOperationException("有効な小節情報がありません。");
        }

        var expectedStart = 0L;
        foreach (var measure in measures.OrderBy(item => item.StartTick))
        {
            if (measure.StartTick != expectedStart
                || measure.DurationTick <= 0L
                || measure.Beats <= 0
                || measure.BeatType <= 0)
            {
                throw new InvalidOperationException("小節情報が不正、または時系列が連続していません。");
            }

            expectedStart = measure.EndTick;
        }
    }

    private static void ValidateTempoEvents(IReadOnlyList<EditableTempoEvent> tempoEvents)
    {
        if (tempoEvents.Count == 0)
        {
            throw new InvalidOperationException("有効なテンポ情報がありません。");
        }

        foreach (var tempoEvent in tempoEvents)
        {
            if (tempoEvent.Tick < 0L || tempoEvent.BeatsPerMinute <= 0d)
            {
                throw new InvalidOperationException("テンポ情報が不正です。");
            }
        }

        if (tempoEvents.Select(item => item.Tick).Distinct().Count() != tempoEvents.Count)
        {
            throw new InvalidOperationException("同一位置に複数のテンポイベントがあります。");
        }
    }

    private static void ValidateKeySignatureEvents(
        IReadOnlyList<EditableKeySignatureEvent> keySignatureEvents,
        long scoreLength)
    {
        var ticks = new HashSet<long>();
        foreach (var keyEvent in keySignatureEvents)
        {
            if (keyEvent.Tick < 0L
                || keyEvent.Tick >= scoreLength
                || keyEvent.Fifths is < -7 or > 7)
            {
                throw new InvalidOperationException("調号情報が不正です。");
            }

            if (!ticks.Add(keyEvent.Tick))
            {
                throw new InvalidOperationException("同一位置に複数の調号変更があります。");
            }
        }
    }

    private static void ValidateHarmonyEvents(
        IReadOnlyList<EditableHarmonyEvent> harmonyEvents,
        long scoreLength)
    {
        foreach (var harmony in harmonyEvents)
        {
            if (harmony.Tick < 0L
                || harmony.Tick >= scoreLength
                || string.IsNullOrWhiteSpace(harmony.Symbol))
            {
                throw new InvalidOperationException("コード情報が不正です。");
            }
        }
    }

    private void InsertMeasure(
        int measureNumber,
        bool insertAfter)
    {
        Execute(score =>
        {
            var target = FindMeasure(
                score,
                measureNumber);
            var insertionTick = insertAfter
                ? target.EndTick
                : target.StartTick;
            var durationTick = CalculateFullMeasureDurationTicks(
                target.Beats,
                target.BeatType);

            ShiftNotesForInsertion(
                score.Notes,
                insertionTick,
                durationTick);
            ShiftRestsForInsertion(
                score.Rests,
                insertionTick,
                durationTick);
            ReplaceItems(
                score.HarmonyEvents,
                score.HarmonyEvents.Select(item =>
                    item.Tick >= insertionTick
                        ? item with
                        {
                            Tick = checked(
                                item.Tick + durationTick)
                        }
                        : item));

            var shiftStateEventAtBoundary = insertAfter;
            ReplaceItems(
                score.TempoEvents,
                score.TempoEvents.Select(item =>
                    ShouldShiftStateEvent(
                        item.Tick,
                        insertionTick,
                        shiftStateEventAtBoundary)
                        ? item with
                        {
                            Tick = checked(
                                item.Tick + durationTick)
                        }
                        : item));
            ReplaceItems(
                score.KeySignatureEvents,
                score.KeySignatureEvents.Select(item =>
                    ShouldShiftStateEvent(
                        item.Tick,
                        insertionTick,
                        shiftStateEventAtBoundary)
                        ? item with
                        {
                            Tick = checked(
                                item.Tick + durationTick)
                        }
                        : item));

            ReplaceItems(
                score.Measures,
                score.Measures.Select(measure =>
                    measure.StartTick >= insertionTick
                        ? measure with
                        {
                            StartTick = checked(
                                measure.StartTick + durationTick)
                        }
                        : measure));
            ShiftReferenceSyncPointsForInsertion(
                score.ReferenceAudioSyncPoints,
                insertionTick,
                durationTick);

            score.Measures.Add(new EditableMeasure(
                0,
                insertionTick,
                durationTick,
                target.Beats,
                target.BeatType));
            NormalizeMeasures(score.Measures);
        });
    }

    private static long CalculateFullMeasureDurationTicks(
        int beats,
        int beatType)
    {
        if (beats <= 0
            || beatType <= 0
            || (beatType & (beatType - 1)) != 0)
        {
            throw new InvalidOperationException(
                "拍子は 4/4、3/4、6/8 のように指定してください。");
        }

        var quarterNoteCount =
            beats * 4d / beatType;
        return Math.Max(
            1L,
            checked((long)Math.Round(
                quarterNoteCount
                * EditableMusicScore.TicksPerQuarter,
                MidpointRounding.AwayFromZero)));
    }

    private static void ExpandMeasureTimeline(
        EditableMusicScore score,
        long boundaryTick,
        long deltaTicks)
    {
        foreach (var note in score.Notes)
        {
            if (note.StartTick >= boundaryTick)
            {
                note.StartTick = checked(
                    note.StartTick + deltaTicks);
            }
        }

        ReplaceItems(
            score.Rests,
            score.Rests.Select(rest =>
                rest.StartTick >= boundaryTick
                    ? rest with
                    {
                        StartTick = checked(
                            rest.StartTick
                            + deltaTicks)
                    }
                    : rest));
        ReplaceItems(
            score.TempoEvents,
            score.TempoEvents.Select(item =>
                item.Tick >= boundaryTick
                    ? item with
                    {
                        Tick = checked(
                            item.Tick
                            + deltaTicks)
                    }
                    : item));
        ReplaceItems(
            score.KeySignatureEvents,
            score.KeySignatureEvents.Select(item =>
                item.Tick >= boundaryTick
                    ? item with
                    {
                        Tick = checked(
                            item.Tick
                            + deltaTicks)
                    }
                    : item));
        ReplaceItems(
            score.HarmonyEvents,
            score.HarmonyEvents.Select(item =>
                item.Tick >= boundaryTick
                    ? item with
                    {
                        Tick = checked(
                            item.Tick
                            + deltaTicks)
                    }
                    : item));
        ReplaceItems(
            score.Measures,
            score.Measures.Select(measure =>
                measure.StartTick >= boundaryTick
                    ? measure with
                    {
                        StartTick = checked(
                            measure.StartTick
                            + deltaTicks)
                    }
                    : measure));
        ShiftReferenceSyncPointsForInsertion(
            score.ReferenceAudioSyncPoints,
            boundaryTick,
            deltaTicks);
    }

    private static EditableMeasure FindMeasure(
        EditableMusicScore score,
        int measureNumber)
        => score.Measures.FirstOrDefault(
                measure => measure.Number == measureNumber)
            ?? throw new InvalidOperationException(
                $"小節 {measureNumber} が見つかりません。");

    private static bool ShouldShiftStateEvent(
        long eventTick,
        long insertionTick,
        bool includeBoundary)
        => includeBoundary
            ? eventTick >= insertionTick
            : eventTick > insertionTick;

    private static void ShiftNotesForInsertion(
        List<EditableNote> notes,
        long insertionTick,
        long durationTick)
    {
        for (var index = notes.Count - 1;
             index >= 0;
             index--)
        {
            var note = notes[index];
            if (note.StartTick >= insertionTick)
            {
                note.StartTick = checked(
                    note.StartTick + durationTick);
                continue;
            }

            if (note.EndTick <= insertionTick)
            {
                continue;
            }

            var newDuration =
                insertionTick - note.StartTick;
            if (newDuration < EditableMusicScore.MinimumDurationTicks)
            {
                notes.RemoveAt(index);
            }
            else
            {
                note.DurationTick = newDuration;
            }
        }
    }

    private static void ShiftRestsForInsertion(
        List<EditableRest> rests,
        long insertionTick,
        long durationTick)
    {
        var adjusted = new List<EditableRest>(
            rests.Count);
        foreach (var rest in rests)
        {
            if (rest.StartTick >= insertionTick)
            {
                adjusted.Add(rest with
                {
                    StartTick = checked(
                        rest.StartTick + durationTick)
                });
                continue;
            }

            if (rest.EndTick <= insertionTick)
            {
                adjusted.Add(rest);
                continue;
            }

            var newDuration =
                insertionTick - rest.StartTick;
            if (newDuration > 0L)
            {
                adjusted.Add(rest with
                {
                    DurationTick = newDuration
                });
            }
        }

        ReplaceItems(
            rests,
            adjusted);
    }

    private static void DeleteTimelineSpan(
        EditableMusicScore score,
        long cutStart,
        long cutEnd,
        long cutDuration)
    {
        for (var index = score.Notes.Count - 1;
             index >= 0;
             index--)
        {
            var note = score.Notes[index];
            if (note.StartTick >= cutEnd)
            {
                note.StartTick -= cutDuration;
                continue;
            }

            if (note.StartTick >= cutStart)
            {
                score.Notes.RemoveAt(index);
                continue;
            }

            if (note.EndTick <= cutStart)
            {
                continue;
            }

            var newEnd = note.EndTick <= cutEnd
                ? cutStart
                : note.EndTick - cutDuration;
            var newDuration = newEnd - note.StartTick;
            if (newDuration < EditableMusicScore.MinimumDurationTicks)
            {
                score.Notes.RemoveAt(index);
                continue;
            }

            note.DurationTick = newDuration;
        }

        var rests = new List<EditableRest>(
            score.Rests.Count);
        foreach (var rest in score.Rests)
        {
            if (rest.StartTick >= cutEnd)
            {
                rests.Add(rest with
                {
                    StartTick = rest.StartTick - cutDuration
                });
                continue;
            }

            if (rest.StartTick >= cutStart)
            {
                continue;
            }

            if (rest.EndTick <= cutStart)
            {
                rests.Add(rest);
                continue;
            }

            var newEnd = rest.EndTick <= cutEnd
                ? cutStart
                : rest.EndTick - cutDuration;
            var newDuration = newEnd - rest.StartTick;
            if (newDuration > 0L)
            {
                rests.Add(rest with
                {
                    DurationTick = newDuration
                });
            }
        }

        ReplaceItems(
            score.Rests,
            rests);
        ReplaceItems(
            score.TempoEvents,
            CutEvents(
                score.TempoEvents,
                cutStart,
                cutEnd,
                cutDuration,
                item => item.Tick,
                (item, tick) => item with { Tick = tick }));
        ReplaceItems(
            score.KeySignatureEvents,
            CutEvents(
                score.KeySignatureEvents,
                cutStart,
                cutEnd,
                cutDuration,
                item => item.Tick,
                (item, tick) => item with { Tick = tick }));
        ReplaceItems(
            score.HarmonyEvents,
            CutEvents(
                score.HarmonyEvents,
                cutStart,
                cutEnd,
                cutDuration,
                item => item.Tick,
                (item, tick) => item with { Tick = tick }));
        CutReferenceSyncPoints(
            score.ReferenceAudioSyncPoints,
            cutStart,
            cutEnd,
            cutDuration);

        ReplaceItems(
            score.Measures,
            score.Measures
                .Where(measure =>
                    measure.StartTick != cutStart)
                .Select(measure =>
                    measure.StartTick >= cutEnd
                        ? measure with
                        {
                            StartTick =
                                measure.StartTick - cutDuration
                        }
                        : measure));
        NormalizeMeasures(score.Measures);
    }

    private static void ShiftReferenceSyncPointsForInsertion(
        List<ReferenceAudioSyncPoint> points,
        long insertionTick,
        long durationTick)
    {
        if (points.Count == 0
            || durationTick == 0L)
        {
            return;
        }

        var insertionBeat =
            EditableMusicScore.TickToBeat(
                insertionTick);
        var durationBeat =
            EditableMusicScore.TickToBeat(
                durationTick);
        var preserveSourceStart =
            insertionBeat
                <= ScoreTiming.EventBeatTolerance
            && points.Any(point =>
                point.ScoreBeat
                    <= ScoreTiming.EventBeatTolerance
                && point.AudioSeconds
                    <= ScoreTiming.EventBeatTolerance);

        for (var index = 0;
             index < points.Count;
             index++)
        {
            var point = points[index];
            if (point.ScoreBeat
                >= insertionBeat
                - ScoreTiming.EventBeatTolerance)
            {
                points[index] = point with
                {
                    ScoreBeat =
                        point.ScoreBeat
                        + durationBeat
                };
            }
        }

        if (preserveSourceStart)
        {
            points.Add(
                new ReferenceAudioSyncPoint(
                    0d,
                    0d));
        }
    }

    private static void CutReferenceSyncPoints(
        List<ReferenceAudioSyncPoint> points,
        long cutStart,
        long cutEnd,
        long cutDuration)
    {
        if (points.Count == 0)
        {
            return;
        }

        var startBeat =
            EditableMusicScore.TickToBeat(
                cutStart);
        var endBeat =
            EditableMusicScore.TickToBeat(
                cutEnd);
        var durationBeat =
            EditableMusicScore.TickToBeat(
                cutDuration);
        var adjusted =
            new List<ReferenceAudioSyncPoint>(
                points.Count);

        foreach (var point in points)
        {
            if (point.ScoreBeat
                < startBeat
                - ScoreTiming.EventBeatTolerance)
            {
                adjusted.Add(point);
                continue;
            }

            if (point.ScoreBeat
                >= endBeat
                - ScoreTiming.EventBeatTolerance)
            {
                adjusted.Add(
                    point with
                    {
                        ScoreBeat = Math.Max(
                            0d,
                            point.ScoreBeat
                            - durationBeat)
                    });
            }
        }

        points.Clear();
        points.AddRange(adjusted);
    }

    private static IEnumerable<T> CutEvents<T>(
        IEnumerable<T> source,
        long cutStart,
        long cutEnd,
        long cutDuration,
        Func<T, long> getTick,
        Func<T, long, T> withTick)
    {
        foreach (var item in source)
        {
            var tick = getTick(item);
            if (tick < cutStart)
            {
                yield return item;
            }
            else if (tick >= cutEnd)
            {
                yield return withTick(
                    item,
                    tick - cutDuration);
            }
        }
    }

    private static EditableTempoEvent? FindTempoStateAtOrBefore(
        IEnumerable<EditableTempoEvent> events,
        long tick)
        => events
            .Where(item => item.Tick <= tick)
            .OrderByDescending(item => item.Tick)
            .FirstOrDefault();

    private static EditableKeySignatureEvent? FindKeyStateAtOrBefore(
        IEnumerable<EditableKeySignatureEvent> events,
        long tick)
        => events
            .Where(item => item.Tick <= tick)
            .OrderByDescending(item => item.Tick)
            .FirstOrDefault();

    private static EditableHarmonyEvent? FindHarmonyStateAtOrBefore(
        IEnumerable<EditableHarmonyEvent> events,
        long tick)
        => events
            .Where(item => item.Tick <= tick)
            .OrderByDescending(item => item.Tick)
            .FirstOrDefault();

    private static void PreserveTempoStateAtSeam(
        EditableMusicScore score,
        long seamTick,
        EditableTempoEvent? desired)
    {
        if (desired is null)
        {
            if (score.TempoEvents.Count == 0)
            {
                score.TempoEvents.Add(
                    new EditableTempoEvent(
                        0L,
                        120d));
            }

            return;
        }

        var current = score.TempoEvents
            .Where(item => item.Tick <= seamTick)
            .OrderByDescending(item => item.Tick)
            .FirstOrDefault();
        if (current is not null
            && Math.Abs(
                current.BeatsPerMinute
                - desired.BeatsPerMinute) <= 0.000001d)
        {
            return;
        }

        score.TempoEvents.RemoveAll(item =>
            item.Tick == seamTick);
        score.TempoEvents.Add(
            desired with
            {
                Tick = seamTick
            });
    }

    private static void PreserveKeyStateAtSeam(
        EditableMusicScore score,
        long seamTick,
        EditableKeySignatureEvent? desired)
    {
        if (desired is null)
        {
            return;
        }

        var current = score.KeySignatureEvents
            .Where(item => item.Tick <= seamTick)
            .OrderByDescending(item => item.Tick)
            .FirstOrDefault();
        if (current is not null
            && current.Fifths == desired.Fifths
            && string.Equals(
                current.Mode,
                desired.Mode,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                current.ScaleType,
                desired.ScaleType,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        score.KeySignatureEvents.RemoveAll(item =>
            item.Tick == seamTick);
        score.KeySignatureEvents.Add(
            desired with
            {
                Tick = seamTick
            });
    }

    private static void PreserveHarmonyStateAtSeam(
        EditableMusicScore score,
        long seamTick,
        EditableHarmonyEvent? desired)
    {
        if (desired is null)
        {
            return;
        }

        var current = score.HarmonyEvents
            .Where(item => item.Tick <= seamTick)
            .OrderByDescending(item => item.Tick)
            .FirstOrDefault();
        if (current is not null
            && string.Equals(
                current.Symbol,
                desired.Symbol,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        score.HarmonyEvents.RemoveAll(item =>
            item.Tick == seamTick);
        score.HarmonyEvents.Add(
            desired with
            {
                Tick = seamTick
            });
    }

    private static void NormalizeMeasures(
        List<EditableMeasure> measures)
    {
        var ordered = measures
            .OrderBy(measure => measure.StartTick)
            .ToArray();
        measures.Clear();
        for (var index = 0;
             index < ordered.Length;
             index++)
        {
            measures.Add(
                ordered[index] with
                {
                    Number = index + 1
                });
        }
    }

    private static void ReplaceItems<T>(
        List<T> target,
        IEnumerable<T> source)
    {
        var values = source.ToArray();
        target.Clear();
        target.AddRange(values);
    }

    private T Execute<T>(Func<EditableMusicScore, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var before = Score.Clone();
        try
        {
            var result = action(Score);
            Validate(Score);
            _undoStack.Push(before);
            _redoStack.Clear();
            return result;
        }
        catch
        {
            Score = before;
            throw;
        }
    }

    private void Execute(Action<EditableMusicScore> action)
        => Execute(score =>
        {
            action(score);
            return true;
        });

    private static int FindAdjacentScalePitch(
        int midiNote,
        IReadOnlyList<int> pitchClasses,
        int direction)
    {
        if (pitchClasses.Count == 0)
        {
            throw new InvalidOperationException(
                "この位置のScale構成音を確認できません。");
        }

        for (var candidate = midiNote + direction;
             candidate is >= 0 and <= 127;
             candidate += direction)
        {
            var pitchClass =
                ((candidate % 12) + 12) % 12;
            if (pitchClasses.Contains(
                    pitchClass))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            direction > 0
                ? "これ以上高いScale構成音へ移動できません。"
                : "これ以上低いScale構成音へ移動できません。");
    }

    private static int NextNoteId(EditableMusicScore score)
        => score.Notes.Count == 0 ? 0 : checked(score.Notes.Max(note => note.Id) + 1);

    private static string NormalizeVoice(string? voice)
        => string.IsNullOrWhiteSpace(voice) ? "1" : voice.Trim();

    private static string NormalizeKeyMode(string? mode)
    {
        var normalized = mode?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "major" => "major",
            "minor" => "minor",
            "ionian" => "ionian",
            "dorian" => "dorian",
            "phrygian" => "phrygian",
            "lydian" => "lydian",
            "mixolydian" => "mixolydian",
            "aeolian" => "aeolian",
            "locrian" => "locrian",
            "none" => "none",
            _ => throw new ArgumentException(
                "対応していないKey modeです。",
                nameof(mode))
        };
    }

    private static string CreateSignature(EditableMusicScore score)
    {
        var builder = new StringBuilder();
        builder.Append(score.Title).Append('|');
        foreach (var note in score.Notes.OrderBy(note => note.Id))
        {
            builder.Append('N').Append(note.Id).Append(':')
                .Append(note.MidiNote).Append(':')
                .Append(note.StartTick).Append(':')
                .Append(note.DurationTick).Append(':')
                .Append(note.Staff).Append(':')
                .Append(NormalizeVoice(note.Voice)).Append(':')
                .Append((int)note.Hand).Append(':')
                .Append(note.Finger).Append(';');
        }

        foreach (var rest in score.Rests
                     .OrderBy(rest => rest.StartTick)
                     .ThenBy(rest => rest.Staff)
                     .ThenBy(rest => NormalizeVoice(rest.Voice), StringComparer.Ordinal)
                     .ThenBy(rest => rest.DurationTick))
        {
            builder.Append('R').Append(rest.StartTick).Append(':')
                .Append(rest.DurationTick).Append(':')
                .Append(rest.Staff).Append(':')
                .Append(NormalizeVoice(rest.Voice)).Append(';');
        }

        foreach (var measure in score.Measures.OrderBy(measure => measure.StartTick))
        {
            builder.Append('M').Append(measure.Number).Append(':')
                .Append(measure.StartTick).Append(':')
                .Append(measure.DurationTick).Append(':')
                .Append(measure.Beats).Append(':')
                .Append(measure.BeatType).Append(';');
        }

        foreach (var tempoEvent in score.TempoEvents.OrderBy(item => item.Tick))
        {
            builder.Append('T').Append(tempoEvent.Tick).Append(':')
                .Append(tempoEvent.BeatsPerMinute.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                .Append(';');
        }

        foreach (var keyEvent in score.KeySignatureEvents.OrderBy(item => item.Tick))
        {
            builder.Append('K').Append(keyEvent.Tick).Append(':')
                .Append(keyEvent.Fifths).Append(':')
                .Append(keyEvent.Mode?.Trim().ToLowerInvariant() ?? string.Empty).Append(':')
                .Append(keyEvent.ScaleType?.Trim().ToLowerInvariant() ?? string.Empty)
                .Append(';');
        }

        foreach (var harmony in score.HarmonyEvents.OrderBy(item => item.Tick))
        {
            builder.Append('H').Append(harmony.Tick).Append(':')
                .Append(harmony.Symbol.Trim())
                .Append(';');
        }

        return builder.ToString();
    }
}
