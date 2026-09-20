using System.Text.Json;
using System.Xml.Linq;
using PianoPracticeTool.Core;
using PianoPracticeTool.Core.Editing;
using PianoPracticeTool.Services.Editor;
using PianoPracticeTool.Services.Midi;
using PianoPracticeTool.Services.Practice;
using PianoPracticeTool.Services.Settings;
using PianoPracticeTool.Services.Songs;

namespace PianoPracticeTool.Validation;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("Editable score preserves rests and key signatures", EditableScorePreservesStructuralData),
            ("Score edit session undo redo", ScoreEditSessionUndoRedo),
            ("Score edit session changes selected hands as one undo step", ScoreEditSessionBulkHandChange),
            ("Score edit session moves notes by active scale steps", ScoreEditSessionScaleStepMove),
            ("Measure insertion shifts timeline atomically", MeasureInsertionShiftsTimelineAtomically),
            ("Measure deletion splices timeline atomically", MeasureDeletionSplicesTimelineAtomically),
            ("Measure structure edits support undo redo", MeasureStructureEditsSupportUndoRedo),
            ("Measure structure edits survive MusicXML round trip", MeasureStructureEditsSurviveMusicXmlRoundTrip),
            ("Per-measure meter edits shift the timeline", PerMeasureMeterEditsShiftTimeline),
            ("Per-measure tempo edits replace and remove changes", PerMeasureTempoEditsReplaceAndRemoveChanges),
            ("Measure meter and tempo survive MusicXML round trip", MeasureMeterAndTempoSurviveMusicXmlRoundTrip),
            ("Editor metadata edits participate in dirty state", EditorMetadataEditsParticipateInDirtyState),
            ("Editor workspaces round trip without changing MusicXML", EditorWorkspaceRoundTrip),
            ("Editor session can resume against an older saved baseline", EditorSessionResumeUsesSavedBaseline),
            ("MusicXML emits triplet notation", MusicXmlEmitsTripletNotation),
            ("Triplet note and rest round trip", TripletNoteAndRestRoundTrip),
            ("Mid-measure key change is not duplicated", MidMeasureKeyChangeIsNotDuplicated),
            ("Meter change does not duplicate key signature", MeterChangeDoesNotDuplicateKeySignature),
            ("Editor MusicXML round trip preserves semantic score", MusicXmlRoundTripPreservesScore),
            ("Editor loader preserves harmony during rest voice enrichment", EditorLoaderPreservesHarmonyDuringRestVoiceEnrichment),
            ("Validation MusicXML survives editor round trip", ValidationMusicXmlRoundTrip),
            ("Music theory metadata produces scale and harmony labels", MusicTheoryMetadataProducesLabels),
            ("Scale variants and timed chord guides", ScaleVariantsAndTimedChordGuides),
            ("Explicit harmony timeline preserves intra-measure changes", ExplicitHarmonyTimelinePreservesIntraMeasureChanges),
            ("Custom scale metadata survives MusicXML round trip", CustomScaleMetadataRoundTrip),
            ("Extended chord kinds survive MusicXML round trip", ExtendedChordKindsRoundTrip),
            ("Portable app paths survive folder move", PortableAppPathsSurviveFolderMove),
            ("Song catalog cache keys by path and content hash", SongCatalogCacheKeysByPathAndHash),
            ("Legacy note flow setting migrates to visible range", LegacyNoteFlowSettingMigratesToVisibleRange),
            ("Octave shift moves the effective keyboard range", OctaveShiftMovesEffectiveKeyboardRange),
            ("Song library compatibility prefers uncompressed hand practice", SongLibraryCompatibilityPrefersUncompressedHandPractice),
            ("Song library compatibility falls back to compression", SongLibraryCompatibilityFallsBackToCompression),
            ("Practice hand assignment corrects crossing staff notes", PracticeHandAssignmentCorrectsCrossingStaffNotes),
            ("Practice hand assignment avoids a rapid isolated octave jump", PracticeHandAssignmentAvoidsRapidIsolatedOctaveJump),
            ("Fingering avoids black-key thumb when an easy alternative exists", FingeringAvoidsBlackKeyThumbWhenAlternativeExists),
            ("Fingering keeps thumb for a black-key octave", FingeringKeepsThumbForBlackKeyOctave),
            ("Fingering respects fingers held across later notes", FingeringRespectsHeldFingerPositions),
            ("Fingering preserves explicit score annotations", FingeringPreservesExplicitScoreAnnotations),
            ("Practice score detects and corrects repeated key overlap", PracticeScoreDetectsAndCorrectsRepeatedKeyOverlap),
            ("Practice score warns when one hand exceeds five keys", PracticeScoreWarnsWhenOneHandExceedsFiveKeys),
            ("Listen memorization navigation returns to replay start", ListenMemorizationNavigationReturnsToReplayStart),
            ("Song octave compression is independent from device shift", SongOctaveCompressionIsIndependentFromDeviceShift),
            ("Song metadata update preserves score content", SongMetadataUpdatePreservesScoreContent),
            ("Synchronized score preview retriggers adjacent repeated notes", SynchronizedScorePreviewRetriggersAdjacentRepeatedNotes),
            ("Reference audio sync supports source-only gaps", ReferenceAudioSyncSupportsSourceOnlyGaps),
            ("Reference audio start adjusts before the first sync point", ReferenceAudioStartAdjustsBeforeFirstSyncPoint),
            ("Reference audio segment adjustment preserves sync boundaries", ReferenceAudioSegmentAdjustmentPreservesSyncBoundaries),
            ("Reference audio end adjustment creates an outro gap", ReferenceAudioEndAdjustmentCreatesOutroGap),
            ("Measure edits keep reference audio sync aligned", MeasureEditsKeepReferenceAudioSyncAligned),
            ("Reference audio project sidecar round trip", ReferenceAudioProjectSidecarRoundTrip),
            ("SoundFont preset support follows SF2 bank rules", SoundFontPresetSupportFollowsSf2BankRules),
            ("SoundFont reverb creates an audible tail", SoundFontReverbCreatesTail),
            ("MIDI output selection is deterministic", MidiOutputSelectionIsDeterministic)
        };

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                test.Run();
                Console.WriteLine($"PASS  {test.Name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine($"FAIL  {test.Name}");
                Console.Error.WriteLine(ex);
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} validation checks passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void EditableScorePreservesStructuralData()
    {
        var source = CreateReferenceScore();
        var editable = EditableMusicScore.FromMusicScore(source);
        AssertEqual(source.Rests.Count, editable.Rests.Count, "editable rest count");
        AssertEqual(source.KeySignatureEvents.Count, editable.KeySignatureEvents.Count, "editable key signature count");
        AssertEqual(source.HarmonyEvents.Count, editable.HarmonyEvents.Count, "editable harmony count");

        var converted = editable.ToMusicScore();
        AssertEqual(source.Composer, converted.Composer, "converted composer");
        AssertEqual(source.Rests.Count, converted.Rests.Count, "converted rest count");
        for (var index = 0; index < source.Rests.Count; index++)
        {
            AssertClose(source.Rests[index].StartBeat, converted.Rests[index].StartBeat, "rest start");
            AssertClose(source.Rests[index].DurationBeat, converted.Rests[index].DurationBeat, "rest duration");
            AssertEqual(source.Rests[index].Staff, converted.Rests[index].Staff, "rest staff");
            AssertEqual(source.Rests[index].Voice, converted.Rests[index].Voice, "rest voice");
        }

        AssertEqual(source.KeySignatureEvents.Count, converted.KeySignatureEvents.Count, "converted key signature count");
        for (var index = 0; index < source.KeySignatureEvents.Count; index++)
        {
            AssertClose(source.KeySignatureEvents[index].Beat, converted.KeySignatureEvents[index].Beat, "key signature beat");
            AssertEqual(source.KeySignatureEvents[index].Fifths, converted.KeySignatureEvents[index].Fifths, "key signature fifths");
            AssertEqual(source.KeySignatureEvents[index].Mode, converted.KeySignatureEvents[index].Mode, "key signature mode");
            AssertEqual(source.KeySignatureEvents[index].ScaleType, converted.KeySignatureEvents[index].ScaleType, "key signature scale type");
        }

        AssertEqual(source.HarmonyEvents.Count, converted.HarmonyEvents.Count, "converted harmony count");
        for (var index = 0; index < source.HarmonyEvents.Count; index++)
        {
            AssertClose(source.HarmonyEvents[index].Beat, converted.HarmonyEvents[index].Beat, "harmony beat");
            AssertEqual(source.HarmonyEvents[index].Symbol, converted.HarmonyEvents[index].Symbol, "harmony symbol");
        }
    }

    private static void ScoreEditSessionUndoRedo()
    {
        var editable = EditableMusicScore.FromMusicScore(CreateReferenceScore());
        var session = new ScoreEditSession(editable);
        var target = session.Score.Notes.Single(note => note.Id == 0);
        var originalStart = target.StartTick;

        session.MoveNotes(new[] { target.Id }, EditableMusicScore.TicksPerQuarter, 2);
        AssertTrue(session.IsDirty, "session should be dirty after move");
        AssertEqual(originalStart + EditableMusicScore.TicksPerQuarter, session.Score.Notes.Single(note => note.Id == 0).StartTick, "moved start");

        AssertTrue(session.Undo(), "undo should succeed");
        AssertEqual(originalStart, session.Score.Notes.Single(note => note.Id == 0).StartTick, "undo start");

        AssertTrue(session.Redo(), "redo should succeed");
        AssertEqual(originalStart + EditableMusicScore.TicksPerQuarter, session.Score.Notes.Single(note => note.Id == 0).StartTick, "redo start");
    }

    private static void ScoreEditSessionBulkHandChange()
    {
        var session =
            new ScoreEditSession(
                EditableMusicScore.FromMusicScore(
                    CreateReferenceScore()));
        var selectedIds =
            new[]
            {
                0,
                1,
                3
            };

        session.SetHand(
            selectedIds,
            Hand.Left);

        var changedNotes =
            session.Score.Notes
                .Where(note =>
                    selectedIds.Contains(
                        note.Id))
                .ToArray();
        AssertTrue(
            changedNotes.All(note =>
                note.Hand == Hand.Left),
            "bulk hand edit should update every selected note hand");
        AssertTrue(
            changedNotes.All(note =>
                note.Staff == 2),
            "bulk hand edit should update every selected note staff");

        AssertTrue(
            session.Undo(),
            "bulk hand edit undo should succeed");
        var restoredNotes =
            session.Score.Notes
                .Where(note =>
                    selectedIds.Contains(
                        note.Id))
                .ToArray();
        AssertTrue(
            restoredNotes.All(note =>
                note.Hand == Hand.Right),
            "bulk hand edit undo should restore every selected note hand");
        AssertTrue(
            restoredNotes.All(note =>
                note.Staff == 1),
            "bulk hand edit undo should restore every selected note staff");
        AssertTrue(
            !session.Undo(),
            "bulk hand edit should create one undo step");

        AssertTrue(
            session.Redo(),
            "bulk hand edit redo should succeed");
        var redoneNotes =
            session.Score.Notes
                .Where(note =>
                    selectedIds.Contains(
                        note.Id))
                .ToArray();
        AssertTrue(
            redoneNotes.All(note =>
                note.Hand == Hand.Left),
            "bulk hand edit redo should update every selected note hand");
        AssertTrue(
            redoneNotes.All(note =>
                note.Staff == 2),
            "bulk hand edit redo should update every selected note staff");
    }

    private static void ScoreEditSessionScaleStepMove()
    {
        var session =
            new ScoreEditSession(
                EditableMusicScore.FromMusicScore(
                    CreateReferenceScore()));

        session.MoveNotesByScaleStep(
            new[]
            {
                0,
                3
            },
            1);

        AssertEqual(
            62,
            session.Score.Notes.Single(note =>
                note.Id == 0).MidiNote,
            "G major should move C4 up to D4");
        AssertEqual(
            66,
            session.Score.Notes.Single(note =>
                note.Id == 3).MidiNote,
            "G major should move E4 up to F#4");

        session.MoveNotesByScaleStep(
            new[]
            {
                1
            },
            1);
        AssertEqual(
            69,
            session.Score.Notes.Single(note =>
                note.Id == 1).MidiNote,
            "F major at the later key change should move G4 up to A4");

        AssertTrue(
            session.Undo(),
            "later scale step should be undoable");
        AssertEqual(
            67,
            session.Score.Notes.Single(note =>
                note.Id == 1).MidiNote,
            "undo should restore later scale step");

        AssertTrue(
            session.Undo(),
            "first scale step should be undoable");
        AssertEqual(
            60,
            session.Score.Notes.Single(note =>
                note.Id == 0).MidiNote,
            "undo should restore C4");
        AssertEqual(
            64,
            session.Score.Notes.Single(note =>
                note.Id == 3).MidiNote,
            "undo should restore E4");
    }

    private static void MeasureInsertionShiftsTimelineAtomically()
    {
        var session = new ScoreEditSession(
            EditableMusicScore.FromMusicScore(
                CreateReferenceScore()));

        session.InsertMeasureAfter(1);

        AssertEqual(3, session.Score.Measures.Count, "inserted measure count");
        AssertEqual(0L, session.Score.Measures[0].StartTick, "measure 1 start");
        AssertEqual(3840L, session.Score.Measures[1].StartTick, "inserted measure start");
        AssertEqual(7680L, session.Score.Measures[2].StartTick, "shifted measure start");
        AssertEqual(1, session.Score.Measures[0].Number, "measure 1 number");
        AssertEqual(2, session.Score.Measures[1].Number, "inserted measure number");
        AssertEqual(3, session.Score.Measures[2].Number, "shifted measure number");

        AssertEqual(
            8640L,
            session.Score.Notes.Single(note => note.Id == 2).StartTick,
            "note after insertion point should shift");
        AssertEqual(
            3360L,
            session.Score.Notes.Single(note => note.Id == 1).StartTick,
            "note starting before insertion point should keep its onset");
        AssertEqual(
            480L,
            session.Score.Notes.Single(note => note.Id == 1).DurationTick,
            "note crossing insertion boundary should end at the blank measure");
        AssertEqual(
            7680L,
            session.Score.TempoEvents.Single(item => Math.Abs(item.BeatsPerMinute - 90d) < 0.000001d).Tick,
            "tempo event at following measure should shift");
        AssertEqual(
            7680L,
            session.Score.KeySignatureEvents.Single(item => item.Fifths == 0).Tick,
            "key event at following measure should shift");
        AssertEqual(
            7680L,
            session.Score.HarmonyEvents.Single(item => item.Symbol == "C").Tick,
            "harmony event at following measure should shift");

        var partialEnding = new MusicScore
        {
            Title = "Partial Ending",
            TempoBpm = 120d,
            Notes = new[]
            {
                CreateValidationNote(
                    0,
                    60,
                    4.5d,
                    0.5d)
            },
            TempoEvents = new[]
            {
                new ScoreTempoEvent(
                    0d,
                    120d)
            },
            Measures = new[]
            {
                new ScoreMeasure(1, 0d, 4d, 4, 4),
                new ScoreMeasure(2, 4d, 1.5d, 4, 4)
            }
        };
        var partialSession = new ScoreEditSession(
            EditableMusicScore.FromMusicScore(
                partialEnding));
        partialSession.InsertMeasureAfter(2);

        AssertEqual(3, partialSession.Score.Measures.Count, "partial ending inserted measure count");
        AssertEqual(
            5280L,
            partialSession.Score.Measures[2].StartTick,
            "measure added after partial ending should start at original end");
        AssertEqual(
            3840L,
            partialSession.Score.Measures[2].DurationTick,
            "measure added after partial ending should use full 4/4 length");
    }

    private static void MeasureDeletionSplicesTimelineAtomically()
    {
        var session = new ScoreEditSession(
            EditableMusicScore.FromMusicScore(
                CreateReferenceScore()));

        session.DeleteMeasure(1);

        AssertEqual(1, session.Score.Measures.Count, "deleted measure count");
        AssertEqual(0L, session.Score.Measures[0].StartTick, "remaining measure start");
        AssertEqual(1, session.Score.Measures[0].Number, "remaining measure number");

        AssertEqual(1, session.Score.Notes.Count, "deleted span note count");
        var remainingNote = session.Score.Notes.Single();
        AssertEqual(2, remainingNote.Id, "remaining note id");
        AssertEqual(960L, remainingNote.StartTick, "remaining note shifted start");

        AssertEqual(1, session.Score.Rests.Count, "deleted span rest count");
        AssertEqual(1920L, session.Score.Rests[0].StartTick, "remaining rest shifted start");

        AssertEqual(1, session.Score.TempoEvents.Count, "tempo state count after delete");
        AssertEqual(0L, session.Score.TempoEvents[0].Tick, "tempo shifted to song start");
        AssertClose(90d, session.Score.TempoEvents[0].BeatsPerMinute, "tempo state after delete");

        AssertEqual(1, session.Score.KeySignatureEvents.Count, "key state count after delete");
        AssertEqual(0L, session.Score.KeySignatureEvents[0].Tick, "key shifted to song start");
        AssertEqual(0, session.Score.KeySignatureEvents[0].Fifths, "key state after delete");

        AssertEqual(1, session.Score.HarmonyEvents.Count, "harmony state count after delete");
        AssertEqual(0L, session.Score.HarmonyEvents[0].Tick, "harmony shifted to song start");
        AssertEqual("C", session.Score.HarmonyEvents[0].Symbol, "harmony state after delete");

        var tailSession = new ScoreEditSession(
            EditableMusicScore.FromMusicScore(
                CreateReferenceScore()));
        tailSession.DeleteMeasure(2);

        AssertEqual(1, tailSession.Score.Measures.Count, "tail delete measure count");
        AssertTrue(
            tailSession.Score.Notes.All(note => note.Id != 2),
            "notes starting in deleted tail measure should be removed");
        AssertEqual(
            480L,
            tailSession.Score.Notes.Single(note => note.Id == 1).DurationTick,
            "note crossing into deleted tail measure should be trimmed");
        AssertTrue(
            tailSession.Score.KeySignatureEvents.All(item =>
                item.Tick < tailSession.Score.LengthTicks),
            "tail deletion must not leave key events at score end");
        AssertTrue(
            tailSession.Score.HarmonyEvents.All(item =>
                item.Tick < tailSession.Score.LengthTicks),
            "tail deletion must not leave harmony events at score end");
    }

    private static void MeasureStructureEditsSupportUndoRedo()
    {
        var session = new ScoreEditSession(
            EditableMusicScore.FromMusicScore(
                CreateReferenceScore()));

        session.InsertMeasureBefore(2);
        AssertEqual(3, session.Score.Measures.Count, "measure count after insert");
        AssertTrue(session.IsDirty, "measure insertion should mark session dirty");

        AssertTrue(session.Undo(), "measure insertion undo should succeed");
        AssertEqual(2, session.Score.Measures.Count, "measure count after insert undo");
        AssertEqual(
            4800L,
            session.Score.Notes.Single(note => note.Id == 2).StartTick,
            "note position after insert undo");

        AssertTrue(session.Redo(), "measure insertion redo should succeed");
        AssertEqual(3, session.Score.Measures.Count, "measure count after insert redo");
        AssertEqual(
            8640L,
            session.Score.Notes.Single(note => note.Id == 2).StartTick,
            "note position after insert redo");

        session.DeleteMeasure(2);
        AssertEqual(2, session.Score.Measures.Count, "measure count after delete");
        AssertTrue(session.Undo(), "measure deletion undo should succeed");
        AssertEqual(3, session.Score.Measures.Count, "measure count after delete undo");
    }

    private static void MeasureStructureEditsSurviveMusicXmlRoundTrip()
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "PianoPracticeTool.Validation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var insertSession = new ScoreEditSession(
                EditableMusicScore.FromMusicScore(
                    CreateReferenceScore()));
            insertSession.InsertMeasureAfter(1);
            var insertedPath = Path.Combine(
                tempDirectory,
                "measure-insert.musicxml");
            EditorMusicXmlSaveService.SaveValidated(
                insertedPath,
                insertSession.Score.ToMusicScore(),
                SongDifficulty.Intermediate);
            var inserted = EditorScoreLoader.Load(
                insertedPath).Score;

            AssertEqual(3, inserted.Measures.Count, "insert round-trip measure count");
            AssertClose(0d, inserted.Measures[0].StartBeat, "insert round-trip measure 1 start");
            AssertClose(4d, inserted.Measures[1].StartBeat, "insert round-trip blank measure start");
            AssertClose(8d, inserted.Measures[2].StartBeat, "insert round-trip shifted measure start");
            AssertClose(
                9d,
                inserted.Notes.Single(note => note.MidiNote == 48).StartBeat,
                "insert round-trip shifted note start");

            var deleteSession = new ScoreEditSession(
                EditableMusicScore.FromMusicScore(
                    CreateReferenceScore()));
            deleteSession.DeleteMeasure(1);
            var deletedPath = Path.Combine(
                tempDirectory,
                "measure-delete.musicxml");
            EditorMusicXmlSaveService.SaveValidated(
                deletedPath,
                deleteSession.Score.ToMusicScore(),
                SongDifficulty.Intermediate);
            var deleted = EditorScoreLoader.Load(
                deletedPath).Score;

            AssertEqual(1, deleted.Measures.Count, "delete round-trip measure count");
            AssertClose(0d, deleted.Measures[0].StartBeat, "delete round-trip remaining measure start");
            AssertEqual(1, deleted.Notes.Count, "delete round-trip note count");
            AssertClose(1d, deleted.Notes[0].StartBeat, "delete round-trip shifted note start");
            AssertEqual("C", deleted.HarmonyEvents.Single().Symbol, "delete round-trip harmony state");
        }
        finally
        {
            DeleteDirectoryBestEffort(tempDirectory);
        }
    }

    private static void PerMeasureMeterEditsShiftTimeline()
    {
        var expanded = new ScoreEditSession(
            EditableMusicScore.FromMusicScore(
                CreateReferenceScore()));
        expanded.SetMeasureTimeSignature(
            1,
            5,
            4);

        AssertEqual(5, expanded.Score.Measures[0].Beats, "expanded meter beats");
        AssertEqual(4, expanded.Score.Measures[0].BeatType, "expanded meter beat type");
        AssertEqual(4800L, expanded.Score.Measures[0].DurationTick, "expanded measure duration");
        AssertEqual(4800L, expanded.Score.Measures[1].StartTick, "following measure after expansion");
        AssertEqual(
            5760L,
            expanded.Score.Notes.Single(note => note.Id == 2).StartTick,
            "note after expanded measure should shift");
        AssertEqual(
            4800L,
            expanded.Score.TempoEvents.Single(item =>
                Math.Abs(item.BeatsPerMinute - 90d) < 0.000001d).Tick,
            "tempo after expanded measure should shift");
        AssertEqual(
            4800L,
            expanded.Score.HarmonyEvents.Single(item => item.Symbol == "C").Tick,
            "harmony after expanded measure should shift");

        var shortened = new ScoreEditSession(
            EditableMusicScore.FromMusicScore(
                CreateReferenceScore()));
        shortened.SetMeasureTimeSignature(
            1,
            3,
            4);

        AssertEqual(2880L, shortened.Score.Measures[0].DurationTick, "shortened measure duration");
        AssertEqual(2880L, shortened.Score.Measures[1].StartTick, "following measure after shortening");
        AssertTrue(
            shortened.Score.Notes.All(note => note.Id != 1),
            "note beginning in removed meter tail should be removed");
        AssertEqual(
            3840L,
            shortened.Score.Notes.Single(note => note.Id == 2).StartTick,
            "later note should shift after shortening");
        AssertEqual(
            2880L,
            shortened.Score.TempoEvents.Single(item =>
                Math.Abs(item.BeatsPerMinute - 90d) < 0.000001d).Tick,
            "tempo state should move to shortened boundary");
        AssertEqual(
            2880L,
            shortened.Score.HarmonyEvents.Single(item => item.Symbol == "C").Tick,
            "harmony state should move to shortened boundary");
    }

    private static void PerMeasureTempoEditsReplaceAndRemoveChanges()
    {
        var session = new ScoreEditSession(
            EditableMusicScore.FromMusicScore(
                CreateReferenceScore()));

        session.SetTempoAtMeasure(
            2,
            72d);
        AssertEqual(
            2,
            session.Score.TempoEvents.Count,
            "tempo replacement should not duplicate the event");
        AssertClose(
            72d,
            session.Score.TempoEvents.Single(item =>
                item.Tick == 3840L).BeatsPerMinute,
            "replaced measure tempo");

        AssertTrue(
            session.RemoveTempoAtMeasure(2),
            "measure tempo change should be removable");
        AssertEqual(
            1,
            session.Score.TempoEvents.Count,
            "tempo removal count");
        AssertTrue(
            !session.RemoveTempoAtMeasure(1),
            "initial tempo must not be removable");
    }

    private static void MeasureMeterAndTempoSurviveMusicXmlRoundTrip()
    {
        var session = new ScoreEditSession(
            EditableMusicScore.FromMusicScore(
                CreateReferenceScore()));
        session.SetMeasureTimeSignature(
            2,
            6,
            8);
        session.SetTempoAtMeasure(
            2,
            84d);

        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "PianoPracticeTool.Validation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(
            tempDirectory);
        var path = Path.Combine(
            tempDirectory,
            "meter-tempo.musicxml");

        try
        {
            EditorMusicXmlSaveService.SaveValidated(
                path,
                session.Score.ToMusicScore(),
                SongDifficulty.Intermediate);
            var loaded = EditorScoreLoader.Load(
                path).Score;

            AssertEqual(2, loaded.Measures.Count, "meter round-trip measure count");
            AssertEqual(6, loaded.Measures[1].Beats, "meter round-trip beats");
            AssertEqual(8, loaded.Measures[1].BeatType, "meter round-trip beat type");
            AssertClose(3d, loaded.Measures[1].DurationBeat, "meter round-trip duration");
            AssertClose(
                84d,
                loaded.TempoEvents.Single(item =>
                    Math.Abs(item.Beat - 4d)
                        <= ScoreTiming.EventBeatTolerance)
                    .BeatsPerMinute,
                "tempo round-trip BPM");
        }
        finally
        {
            DeleteDirectoryBestEffort(
                tempDirectory);
        }
    }

    private static void EditorMetadataEditsParticipateInDirtyState()
    {
        var keySession = new ScoreEditSession(
            EditableMusicScore.FromMusicScore(CreateReferenceScore()));
        keySession.MarkSaved();
        keySession.SetKeySignature(0L, 0, "minor", "harmonic-minor");
        AssertTrue(
            keySession.IsDirty,
            "changing key signature should mark the editor session dirty");
        var editedKey =
            keySession.Score.KeySignatureEvents.Single(item => item.Tick == 0L);
        AssertEqual("minor", editedKey.Mode, "edited key mode");
        AssertEqual(
            "harmonic-minor",
            editedKey.ScaleType,
            "edited key scale type");

        var harmonySession = new ScoreEditSession(
            EditableMusicScore.FromMusicScore(CreateReferenceScore()));
        harmonySession.MarkSaved();
        harmonySession.SetHarmony(0L, "G7");
        AssertTrue(
            harmonySession.IsDirty,
            "changing harmony should mark the editor session dirty");
        AssertEqual(
            "G7",
            harmonySession.Score.HarmonyEvents.First(item => item.Tick == 0L).Symbol,
            "edited harmony symbol");

        var restSession = new ScoreEditSession(
            EditableMusicScore.FromMusicScore(CreateReferenceScore()));
        restSession.MarkSaved();
        restSession.InsertRest(640L, 320L, Hand.Right);
        AssertTrue(
            restSession.IsDirty,
            "inserting an explicit rest should mark the editor session dirty");
    }

    private static void EditorWorkspaceRoundTrip()
    {
        var directory =
            Path.Combine(
                Path.GetTempPath(),
                "PianoPracticeTool.Validation",
                Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(
            directory);
        var musicXmlPath =
            Path.Combine(
                directory,
                "workspace.musicxml");
        File.WriteAllText(
            musicXmlPath,
            "<score-partwise version=\"3.1\"></score-partwise>");

        try
        {
            var score =
                new MusicScore
                {
                    Title = "Workspace",
                    Composer = "Composer",
                    TempoBpm = 120d,
                    Notes = new[]
                    {
                        new ScoreNote
                        {
                            Id = 1,
                            MidiNote = 61,
                            StartBeat = 0.5d,
                            DurationBeat = 1.5d,
                            Staff = 1,
                            Voice = "1",
                            Hand = Hand.Right,
                            Finger = 2
                        }
                    },
                    Measures = new[]
                    {
                        new ScoreMeasure(
                            1,
                            0d,
                            4d,
                            4,
                            4)
                    }
                };
            var reference =
                new ReferenceAudioProject
                {
                    Enabled = true,
                    AudioPath =
                        Path.Combine(
                            directory,
                            "reference.wav"),
                    ReferenceVolumePercent = 72,
                    SyncPoints = new List<ReferenceAudioSyncPoint>
                    {
                        new(
                            0d,
                            0.5d),
                        new(
                            4d,
                            3.5d)
                    }
                };
            var viewState =
                new EditorWorkspaceViewState
                {
                    CursorBeat = 1.25d,
                    GridTicks = 120L,
                    InsertHand = Hand.Left,
                    SelectedNoteIds = new List<int>
                    {
                        1
                    },
                    VisibleBeats = 12d,
                    VisiblePitchLowestMidi = 48,
                    VisiblePitchHighestMidi = 84
                };

            EditorWorkspaceService.Save(
                musicXmlPath,
                score,
                SongDifficulty.Intermediate,
                reference,
                viewState);

            AssertTrue(
                EditorWorkspaceService.Exists(
                    musicXmlPath),
                "workspace sidecar should exist");
            var loaded =
                EditorWorkspaceService.Load(
                    musicXmlPath);
            AssertEqual(
                "Workspace",
                loaded.Score.Title,
                "workspace score title");
            AssertEqual(
                61,
                loaded.Score.Notes.Single().MidiNote,
                "workspace note pitch");
            AssertEqual(
                SongDifficulty.Intermediate,
                loaded.Difficulty,
                "workspace difficulty");
            AssertEqual(
                Hand.Left,
                loaded.ViewState.InsertHand,
                "workspace insert hand");
            AssertClose(
                1.25d,
                loaded.ViewState.CursorBeat,
                "workspace cursor");
            AssertTrue(
                Path.IsPathRooted(
                    loaded.ReferenceAudioProject.AudioPath),
                "workspace reference path should resolve to an absolute path");

            EditorWorkspaceService.Delete(
                musicXmlPath);
            AssertTrue(
                !EditorWorkspaceService.Exists(
                    musicXmlPath),
                "workspace sidecar should be deleted");
        }
        finally
        {
            Directory.Delete(
                directory,
                recursive: true);
        }
    }

    private static void EditorSessionResumeUsesSavedBaseline()
    {
        var saved =
            EditableMusicScore.FromMusicScore(
                CreateReferenceScore());
        var resumed =
            saved.Clone();
        resumed.Notes[0].MidiNote +=
            1;

        var session =
            new ScoreEditSession(
                resumed,
                saved);

        AssertTrue(
            session.IsDirty,
            "resumed workspace should remain dirty against the MusicXML baseline");
        session.MarkSaved();
        AssertTrue(
            !session.IsDirty,
            "final save should move the MusicXML baseline to the resumed state");
    }

    private static void MusicXmlEmitsTripletNotation()
    {
        var score = new MusicScore
        {
            Title = "Triplet",
            TempoBpm = 120d,
            Notes = new[]
            {
                new ScoreNote
                {
                    Id = 0,
                    MidiNote = 60,
                    StartBeat = 0d,
                    DurationBeat = 1d / 3d,
                    Staff = 1,
                    Voice = "1",
                    Hand = Hand.Right,
                    Finger = 1
                }
            },
            Rests = Array.Empty<ScoreRest>(),
            TempoEvents = new[] { new ScoreTempoEvent(0d, 120d) },
            Measures = new[] { new ScoreMeasure(1, 0d, 4d, 4, 4) }
        };

        var document = MusicXmlWriter.BuildDocument(score, SongDifficulty.Beginner);
        var note = document.Descendants().First(element => element.Name.LocalName == "note");
        AssertEqual("eighth", DirectValue(note, "type"), "triplet normal note type");
        var timeModification = note.Elements().FirstOrDefault(element => element.Name.LocalName == "time-modification")
            ?? throw new InvalidOperationException("time-modification was not emitted for triplet duration.");
        AssertEqual("3", DirectValue(timeModification, "actual-notes"), "actual notes");
        AssertEqual("2", DirectValue(timeModification, "normal-notes"), "normal notes");
        AssertEqual("eighth", DirectValue(timeModification, "normal-type"), "normal type");
    }

    private static void TripletNoteAndRestRoundTrip()
    {
        var score = new MusicScore
        {
            Title = "Triplet Rest",
            TempoBpm = 120d,
            Notes = new[]
            {
                new ScoreNote
                {
                    Id = 0,
                    MidiNote = 60,
                    StartBeat = 0d,
                    DurationBeat = 2d / 3d,
                    Staff = 1,
                    Voice = "1",
                    Hand = Hand.Right
                }
            },
            Rests = new[]
            {
                new ScoreRest(2d / 3d, 1d / 3d, 1, "1")
            },
            TempoEvents = new[]
            {
                new ScoreTempoEvent(0d, 120d)
            },
            KeySignatureEvents = new[]
            {
                new ScoreKeySignatureEvent(0d, 0, "major")
            },
            Measures = new[]
            {
                new ScoreMeasure(1, 0d, 4d, 4, 4)
            }
        };

        var document = MusicXmlWriter.BuildDocument(
            score,
            SongDifficulty.Beginner);
        var notes = document
            .Descendants()
            .Where(element => element.Name.LocalName == "note")
            .ToArray();
        var pitched = notes.Single(element =>
            element.Elements().Any(child => child.Name.LocalName == "pitch"));
        var rest = notes.Single(element =>
            element.Elements().Any(child => child.Name.LocalName == "rest"));
        AssertEqual("quarter", DirectValue(pitched, "type"), "2/3-beat triplet note type");
        AssertEqual("eighth", DirectValue(rest, "type"), "1/3-beat triplet rest type");
        AssertEqual("1", DirectValue(rest, "voice"), "triplet rest voice");
        AssertTrue(
            pitched.Elements().Any(element => element.Name.LocalName == "time-modification"),
            "2/3-beat note should have time-modification");
        AssertTrue(
            rest.Elements().Any(element => element.Name.LocalName == "time-modification"),
            "1/3-beat rest should have time-modification");

        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "PianoPracticeTool.Validation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "triplet_rest.musicxml");
        try
        {
            EditorMusicXmlSaveService.SaveValidated(
                path,
                score,
                SongDifficulty.Beginner);
            var reloaded = EditorScoreLoader.Load(path).Score;
            AssertClose(
                2d / 3d,
                reloaded.Notes.Single().DurationBeat,
                "round-trip 2/3-beat note");
            AssertClose(
                1d / 3d,
                reloaded.Rests.Single().DurationBeat,
                "round-trip 1/3-beat rest");
            AssertEqual(
                "1",
                reloaded.Rests.Single().Voice,
                "round-trip triplet rest voice");
        }
        finally
        {
            DeleteDirectoryBestEffort(tempDirectory);
        }
    }

    private static void MidMeasureKeyChangeIsNotDuplicated()
    {
        var score = new MusicScore
        {
            Title = "Mid Key",
            TempoBpm = 120d,
            Notes = new[]
            {
                CreateValidationNote(0, 60, 0d, 1d),
                CreateValidationNote(1, 65, 4d, 1d)
            },
            TempoEvents = new[]
            {
                new ScoreTempoEvent(0d, 120d)
            },
            KeySignatureEvents = new[]
            {
                new ScoreKeySignatureEvent(0d, 0, "major"),
                new ScoreKeySignatureEvent(2d, -1, "major")
            },
            Measures = new[]
            {
                new ScoreMeasure(1, 0d, 4d, 4, 4),
                new ScoreMeasure(2, 4d, 4d, 4, 4)
            }
        };

        var document = MusicXmlWriter.BuildDocument(
            score,
            SongDifficulty.Beginner);
        AssertEqual(
            2,
            document.Descendants().Count(element =>
                element.Name.LocalName == "key"),
            "mid-measure key should not be emitted again at the next measure");
    }

    private static void MeterChangeDoesNotDuplicateKeySignature()
    {
        var score = new MusicScore
        {
            Title = "Meter Key",
            TempoBpm = 120d,
            Notes = new[]
            {
                CreateValidationNote(0, 60, 0d, 1d),
                CreateValidationNote(1, 62, 4d, 1d)
            },
            TempoEvents = new[]
            {
                new ScoreTempoEvent(0d, 120d)
            },
            KeySignatureEvents = new[]
            {
                new ScoreKeySignatureEvent(
                    0d,
                    2,
                    "major")
            },
            Measures = new[]
            {
                new ScoreMeasure(1, 0d, 4d, 4, 4),
                new ScoreMeasure(2, 4d, 3d, 3, 4)
            }
        };

        var editable =
            EditableMusicScore.FromMusicScore(score);
        var session =
            new ScoreEditSession(editable);
        session.MoveNotes(
            new[] { 1 },
            deltaTicks: 0L,
            deltaMidi: 1);

        var edited =
            session.Score.ToMusicScore();
        var document =
            MusicXmlWriter.BuildDocument(
                edited,
                SongDifficulty.Beginner);
        AssertEqual(
            1,
            document.Descendants().Count(element =>
                element.Name.LocalName == "key"),
            "meter-only attributes must not repeat the active key");

        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "PianoPracticeTool.Validation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(
            tempDirectory,
            "meter-key.musicxml");

        try
        {
            EditorMusicXmlSaveService.SaveValidated(
                path,
                edited,
                SongDifficulty.Beginner);
            var loaded =
                EditorScoreLoader.Load(path).Score;

            AssertEqual(
                1,
                loaded.KeySignatureEvents.Count,
                "meter-change round-trip key event count");
            AssertEqual(
                2,
                loaded.KeySignatureEvents[0].Fifths,
                "meter-change round-trip fifths");
            AssertEqual(
                "major",
                loaded.KeySignatureEvents[0].Mode,
                "meter-change round-trip mode");
            AssertEqual(
                61,
                loaded.Notes.Single(note =>
                    Math.Abs(note.StartBeat - 4d)
                        <= ScoreTiming.EventBeatTolerance)
                    .MidiNote,
                "edited note should survive the same round trip");
        }
        finally
        {
            DeleteDirectoryBestEffort(
                tempDirectory);
        }
    }

    private static void MusicXmlRoundTripPreservesScore()
    {
        var score = CreateReferenceScore();
        var tempDirectory = Path.Combine(Path.GetTempPath(), "PianoPracticeTool.Validation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "roundtrip.musicxml");

        try
        {
            EditorMusicXmlSaveService.SaveValidated(path, score, SongDifficulty.Intermediate);
            var loaded = EditorScoreLoader.Load(path).Score;

            AssertEqual(score.Notes.Count, loaded.Notes.Count, "round-trip note count");
            AssertEqual(score.Rests.Count, loaded.Rests.Count, "round-trip rest count");
            AssertEqual(score.Measures.Count, loaded.Measures.Count, "round-trip measure count");
            AssertEqual(score.TempoEvents.Count, loaded.TempoEvents.Count, "round-trip tempo count");
            AssertEqual(score.KeySignatureEvents.Count, loaded.KeySignatureEvents.Count, "round-trip key signature count");
            AssertEqual(score.HarmonyEvents.Count, loaded.HarmonyEvents.Count, "round-trip harmony count");
            AssertEqual(score.Composer, loaded.Composer, "round-trip composer");
            AssertEqual(SongDifficulty.Intermediate, SongDifficultyDetector.Detect(path), "round-trip difficulty");
            AssertRestVoicesEqual(score.Rests, loaded.Rests, "round-trip rest voice");

            AssertEqual(1, loaded.KeySignatureEvents[0].Fifths, "initial G major fifths");
            AssertEqual("major", loaded.KeySignatureEvents[0].Mode, "initial key mode");
            AssertEqual(-1, loaded.KeySignatureEvents[1].Fifths, "mid-measure F major fifths");
            AssertClose(2d, loaded.KeySignatureEvents[1].Beat, "mid-measure key signature beat");
            AssertEqual(0, loaded.KeySignatureEvents[2].Fifths, "second-measure C major fifths");
            AssertClose(4d, loaded.KeySignatureEvents[2].Beat, "second key signature beat");

            var tied = loaded.Notes.Single(note => note.MidiNote == 67);
            AssertClose(3.5d, tied.StartBeat, "tied note start");
            AssertClose(1d, tied.DurationBeat, "tied note duration");
            AssertEqual(4, tied.Finger, "fingering preservation");

            var xml = XDocument.Load(path);
            AssertTrue(
                xml.Descendants().Any(element => element.Name.LocalName == "tie" && element.Attribute("type")?.Value == "start"),
                "tie start should be present in serialized XML");
            AssertTrue(
                xml.Descendants().Any(element => element.Name.LocalName == "tie" && element.Attribute("type")?.Value == "stop"),
                "tie stop should be present in serialized XML");
            AssertEqual(3, xml.Descendants().Count(element => element.Name.LocalName == "key"), "serialized key element count");
            AssertEqual(2, xml.Descendants().Count(element => element.Name.LocalName == "harmony"), "serialized harmony element count");
            AssertTrue(
                xml.Descendants().Any(element => element.Name.LocalName == "chord"),
                "simultaneous same-voice notes should be serialized with chord notation");
            AssertTrue(
                xml.Descendants()
                    .Where(element => element.Name.LocalName == "note")
                    .Where(element => element.Elements().Any(child => child.Name.LocalName == "rest"))
                    .All(element => !string.IsNullOrWhiteSpace(DirectValue(element, "voice"))),
                "serialized rests should contain voice metadata");
        }
        finally
        {
            DeleteDirectoryBestEffort(tempDirectory);
        }
    }

    private static void EditorLoaderPreservesHarmonyDuringRestVoiceEnrichment()
    {
        var score = new MusicScore
        {
            Title = "Harmony With Rest",
            TempoBpm = 120d,
            Notes = Array.Empty<ScoreNote>(),
            Rests = new[]
            {
                new ScoreRest(0d, 4d, 1, "1")
            },
            HarmonyEvents = new[]
            {
                new ScoreHarmonyEvent(0d, "A"),
                new ScoreHarmonyEvent(2d, "Bm")
            },
            Measures = new[]
            {
                new ScoreMeasure(1, 0d, 4d, 4, 4)
            }
        };

        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "PianoPracticeTool.Validation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(
            tempDirectory,
            "harmony-rest-enrichment.musicxml");

        try
        {
            EditorMusicXmlSaveService.SaveValidated(
                path,
                score,
                SongDifficulty.Beginner);
            var loaded = EditorScoreLoader.Load(path).Score;

            AssertEqual(
                2,
                loaded.HarmonyEvents.Count,
                "editor loader harmony count after rest voice enrichment");
            AssertClose(
                0d,
                loaded.HarmonyEvents[0].Beat,
                "editor loader first harmony beat");
            AssertEqual(
                "A",
                loaded.HarmonyEvents[0].Symbol,
                "editor loader first harmony symbol");
            AssertClose(
                2d,
                loaded.HarmonyEvents[1].Beat,
                "editor loader second harmony beat");
            AssertEqual(
                "Bm",
                loaded.HarmonyEvents[1].Symbol,
                "editor loader second harmony symbol");
        }
        finally
        {
            DeleteDirectoryBestEffort(tempDirectory);
        }
    }

    private static void ValidationMusicXmlRoundTrip()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "validation_score.musicxml");
        if (!File.Exists(fixturePath))
        {
            throw new FileNotFoundException("Validation fixture was not copied to the output directory.", fixturePath);
        }

        var original = EditorScoreLoader.Load(fixturePath).Score;
        AssertTrue(original.Notes.Count > 0, "bundled fixture must contain notes");
        AssertTrue(original.Measures.Count > 0, "bundled fixture must contain measures");
        AssertTrue(original.KeySignatureEvents.Count > 0, "bundled fixture must contain a key signature");
        AssertEqual(1, original.KeySignatureEvents[0].Fifths, "bundled fixture initial key signature");

        var tempDirectory = Path.Combine(Path.GetTempPath(), "PianoPracticeTool.Validation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var outputPath = Path.Combine(tempDirectory, "validation_score_roundtrip.musicxml");

        try
        {
            EditorMusicXmlSaveService.SaveValidated(outputPath, original, SongDifficulty.Introductory);
            var reloaded = EditorScoreLoader.Load(outputPath).Score;
            AssertEqual(original.Notes.Count, reloaded.Notes.Count, "fixture note count");
            AssertEqual(original.Rests.Count, reloaded.Rests.Count, "fixture rest count");
            AssertEqual(original.Measures.Count, reloaded.Measures.Count, "fixture measure count");
            AssertEqual(original.KeySignatureEvents.Count, reloaded.KeySignatureEvents.Count, "fixture key signature count");
            AssertEqual(1, reloaded.KeySignatureEvents[0].Fifths, "fixture reloaded key signature");
            AssertRestVoicesEqual(original.Rests, reloaded.Rests, "fixture rest voice");
        }
        finally
        {
            DeleteDirectoryBestEffort(tempDirectory);
        }
    }

    private static void MusicTheoryMetadataProducesLabels()
    {
        var score = CreateReferenceScore();
        AssertEqual("G Major", MusicTheoryAnalyzer.GetScaleDisplayName(score), "source key display");

        var labels = MusicTheoryAnalyzer.CreateMeasureHarmonyLabels(score);
        AssertEqual("G", labels[1], "explicit first-measure harmony");
        AssertEqual("C", labels[2], "explicit second-measure harmony");

        var inferredScore = new MusicScore
        {
            Title = "Inferred C",
            TempoBpm = 120d,
            Notes = new[]
            {
                CreateValidationNote(0, 60, 0d, 2d),
                CreateValidationNote(1, 64, 0d, 2d),
                CreateValidationNote(2, 67, 0d, 2d),
                CreateValidationNote(3, 62, 2d, 0.5d),
                CreateValidationNote(4, 65, 2.5d, 0.5d),
                CreateValidationNote(5, 69, 3d, 0.5d),
                CreateValidationNote(6, 71, 3.5d, 0.5d)
            },
            Measures = new[]
            {
                new ScoreMeasure(1, 0d, 4d, 4, 4)
            }
        };

        var inferredLabels = MusicTheoryAnalyzer.CreateMeasureHarmonyLabels(inferredScore);
        AssertEqual("C?", inferredLabels[1], "inferred C-major chord label");
        var analyses = MusicTheoryAnalyzer.CreateMeasureHarmonyAnalyses(inferredScore);
        AssertEqual("C", analyses[1].Symbol, "editor inferred chord symbol");
        AssertTrue(analyses[1].IsInferred, "editor inferred chord should carry inferred metadata");

        var key = MusicTheoryAnalyzer.AnalyzeKeyAt(inferredScore, 0d)
            ?? throw new InvalidOperationException("inferred key was not produced");
        AssertTrue(key.IsInferred, "inferred scale should carry inferred metadata");
        AssertEqual(7, key.PitchClasses.Count, "major scale pitch class count");
        AssertTrue(key.PitchClasses.Contains(0), "C major should contain C");
        AssertTrue(key.PitchClasses.Contains(4), "C major should contain E");
        AssertTrue(key.PitchClasses.Contains(7), "C major should contain G");
        AssertTrue(
            MusicTheoryAnalyzer.GetScaleDisplayName(inferredScore).EndsWith("(推定)", StringComparison.Ordinal),
            "inferred scale should be marked as inferred");

        AssertTrue(
            MusicTheoryAnalyzer.TryGetChordPitchClasses("G7", out var g7PitchClasses),
            "G7 should be parsed for chord-tone highlighting");
        AssertTrue(
            new[] { 7, 11, 2, 5 }.All(g7PitchClasses.Contains),
            "G7 pitch classes");
    }

    private static void ScaleVariantsAndTimedChordGuides()
    {
        var score = new MusicScore
        {
            Title = "Guides",
            TempoBpm = 120d,
            Notes = new[]
            {
                CreateValidationNote(0, 60, 0d, 1d),
                CreateValidationNote(1, 64, 0d, 1d),
                CreateValidationNote(2, 67, 0d, 1d),
                CreateValidationNote(3, 62, 2d, 1d),
                CreateValidationNote(4, 67, 2d, 1d),
                CreateValidationNote(5, 71, 2d, 1d)
            },
            KeySignatureEvents = new[]
            {
                new ScoreKeySignatureEvent(0d, 0, "dorian")
            },
            HarmonyEvents = new[]
            {
                new ScoreHarmonyEvent(0d, "Cmaj9"),
                new ScoreHarmonyEvent(2d, "G13")
            },
            Measures = new[]
            {
                new ScoreMeasure(1, 0d, 4d, 4, 4)
            }
        };

        var key = MusicTheoryAnalyzer.AnalyzeKeyAt(score, 0d)
            ?? throw new InvalidOperationException("Dorian key analysis was not produced.");
        AssertEqual("dorian", key.ScaleId, "Dorian scale id");
        AssertEqual(2, key.RootPitchClass, "D Dorian tonic");
        AssertTrue(
            new[] { 0, 2, 4, 5, 7, 9, 11 }.All(key.PitchClasses.Contains),
            "D Dorian should share the C-major pitch-class set");

        var dorianDocument = MusicXmlWriter.BuildDocument(
            score,
            SongDifficulty.Beginner);
        AssertEqual(
            "dorian",
            dorianDocument.Descendants()
                .First(element => element.Name.LocalName == "mode")
                .Value,
            "Dorian should use the standard MusicXML mode");
        AssertTrue(
            !dorianDocument.Descendants().Any(element =>
                element.Name.LocalName == "miscellaneous-field"
                && string.Equals(
                    element.Attribute("name")?.Value,
                    MusicTheoryAnalyzer.ScaleEventsMetadataFieldName,
                    StringComparison.OrdinalIgnoreCase)),
            "standard diatonic modes should not require custom scale metadata");

        var candidates = MusicTheoryAnalyzer.CreateScaleCandidates(score, 8);
        AssertTrue(candidates.Count > 0, "scale candidates should be produced");
        AssertTrue(
            candidates.Any(candidate =>
                MusicTheoryAnalyzer.GetScaleDefinition(candidate.ScaleId)?.IsDiatonic == true),
            "diatonic candidates should be available");

        AssertTrue(
            MusicTheoryAnalyzer.GetScaleDefinition("harmonic-minor") is not null,
            "harmonic minor should be selectable");
        AssertTrue(
            MusicTheoryAnalyzer.GetScaleDefinition("minor-pentatonic") is not null,
            "minor pentatonic should be selectable");
        AssertTrue(
            MusicTheoryAnalyzer.GetScaleDefinition("blues") is not null,
            "blues scale should be selectable");

        var chordRegions = MusicTheoryAnalyzer.CreateChordGuideRegions(score);
        AssertEqual(2, chordRegions.Count, "timed chord guide region count");
        AssertClose(0d, chordRegions[0].StartBeat, "first chord guide start");
        AssertClose(2d, chordRegions[0].EndBeat, "first chord guide end");
        AssertClose(2d, chordRegions[1].StartBeat, "second chord guide start");
        AssertClose(4d, chordRegions[1].EndBeat, "second chord guide end");
        AssertTrue(
            new[] { 0, 2, 4, 7, 11 }.All(chordRegions[0].PitchClasses.Contains),
            "Cmaj9 chord-tone guide");
        AssertTrue(
            new[] { 7, 9, 11, 0, 2, 5 }.All(chordRegions[1].PitchClasses.Contains),
            "G13 chord-tone guide");

        var scaleRegions = MusicTheoryAnalyzer.CreateScaleGuideRegions(score);
        AssertEqual(1, scaleRegions.Count, "scale guide region count");
        AssertClose(0d, scaleRegions[0].StartBeat, "scale guide start");
        AssertClose(4d, scaleRegions[0].EndBeat, "scale guide end");

        var suggestions = MusicTheoryAnalyzer.CreateChordSuggestions(
            score,
            score.Measures[0],
            16);
        AssertTrue(suggestions.Count > 0, "chord suggestions should be produced");
    }

    private static void ExplicitHarmonyTimelinePreservesIntraMeasureChanges()
    {
        var score = new MusicScore
        {
            Title = "Harmony Timeline",
            TempoBpm = 120d,
            Notes = Array.Empty<ScoreNote>(),
            HarmonyEvents = new[]
            {
                new ScoreHarmonyEvent(0d, "A"),
                new ScoreHarmonyEvent(2d, "Bm")
            },
            Measures = new[]
            {
                new ScoreMeasure(1, 0d, 4d, 4, 4),
                new ScoreMeasure(2, 4d, 4d, 4, 4)
            }
        };

        var timeline = MusicTheoryAnalyzer.CreateHarmonyTimeline(score);
        AssertEqual(2, timeline.Count, "explicit harmony timeline count");
        AssertEqual("A", timeline[0].Symbol, "first explicit harmony symbol");
        AssertClose(0d, timeline[0].StartBeat, "first explicit harmony start");
        AssertClose(2d, timeline[0].EndBeat, "first explicit harmony end");
        AssertEqual("Bm", timeline[1].Symbol, "second explicit harmony symbol");
        AssertClose(2d, timeline[1].StartBeat, "second explicit harmony start");
        AssertClose(8d, timeline[1].EndBeat, "second explicit harmony carries across measure");
        AssertTrue(!timeline[0].IsInferred, "first harmony should remain explicit");
        AssertTrue(!timeline[1].IsInferred, "second harmony should remain explicit");

        var labels = MusicTheoryAnalyzer.CreateMeasureHarmonyLabels(score);
        AssertEqual("A → Bm", labels[1], "first measure intra-measure harmony label");
        AssertEqual("Bm", labels[2], "second measure carried explicit harmony label");

        var firstMeasureSecondHalf = MusicTheoryAnalyzer.GetHarmonyAt(score, 3d)
            ?? throw new InvalidOperationException("active Bm harmony was not found in measure 1");
        AssertEqual("Bm", firstMeasureSecondHalf.Symbol, "active harmony after intra-measure change");
        AssertTrue(!firstMeasureSecondHalf.IsInferred, "active intra-measure harmony should remain explicit");

        var secondMeasure = MusicTheoryAnalyzer.GetHarmonyAt(score, 5d)
            ?? throw new InvalidOperationException("carried Bm harmony was not found in measure 2");
        AssertEqual("Bm", secondMeasure.Symbol, "active harmony carried into next measure");
        AssertTrue(!secondMeasure.IsInferred, "carried harmony should not be re-inferred");

        var guides = MusicTheoryAnalyzer.CreateChordGuideRegions(score);
        AssertEqual(2, guides.Count, "explicit harmony guide count");
        AssertClose(0d, guides[0].StartBeat, "first explicit guide start");
        AssertClose(2d, guides[0].EndBeat, "first explicit guide end");
        AssertClose(2d, guides[1].StartBeat, "second explicit guide start");
        AssertClose(8d, guides[1].EndBeat, "second explicit guide carries across measure");
    }

    private static void CustomScaleMetadataRoundTrip()
    {
        var score = new MusicScore
        {
            Title = "A Harmonic Minor",
            TempoBpm = 120d,
            Notes = new[]
            {
                CreateValidationNote(0, 57, 0d, 1d),
                CreateValidationNote(1, 60, 1d, 1d),
                CreateValidationNote(2, 64, 2d, 1d),
                CreateValidationNote(3, 68, 3d, 1d)
            },
            TempoEvents = new[]
            {
                new ScoreTempoEvent(0d, 120d)
            },
            KeySignatureEvents = new[]
            {
                new ScoreKeySignatureEvent(
                    0d,
                    0,
                    "minor",
                    "harmonic-minor")
            },
            Measures = new[]
            {
                new ScoreMeasure(1, 0d, 4d, 4, 4)
            }
        };

        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "PianoPracticeTool.Validation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "harmonic_minor.musicxml");

        try
        {
            EditorMusicXmlSaveService.SaveValidated(
                path,
                score,
                SongDifficulty.Beginner);
            var reloaded = EditorScoreLoader.Load(path).Score;
            var key = reloaded.KeySignatureEvents.Single();
            AssertEqual("minor", key.Mode, "custom scale MusicXML base mode");
            AssertEqual(
                "harmonic-minor",
                key.ScaleType,
                "custom scale metadata");
            AssertEqual(
                "A Harmonic Minor",
                MusicTheoryAnalyzer.GetScaleDisplayName(reloaded),
                "custom scale display name");

            var document = XDocument.Load(path);
            AssertEqual(
                "minor",
                document.Descendants()
                    .First(element => element.Name.LocalName == "mode")
                    .Value,
                "serialized harmonic-minor base mode");
            var scaleMetadata = document
                .Descendants()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "miscellaneous-field"
                    && string.Equals(
                        element.Attribute("name")?.Value,
                        MusicTheoryAnalyzer.ScaleEventsMetadataFieldName,
                        StringComparison.OrdinalIgnoreCase))
                ?.Value;
            AssertTrue(
                scaleMetadata?.Contains(
                    "harmonic-minor",
                    StringComparison.Ordinal) == true,
                "custom scale metadata field should be serialized");
        }
        finally
        {
            DeleteDirectoryBestEffort(tempDirectory);
        }
    }

    private static void ExtendedChordKindsRoundTrip()
    {
        var score = new MusicScore
        {
            Title = "Extended Chords",
            TempoBpm = 120d,
            Notes = new[]
            {
                CreateValidationNote(0, 60, 0d, 1d),
                CreateValidationNote(1, 67, 2d, 1d)
            },
            TempoEvents = new[]
            {
                new ScoreTempoEvent(0d, 120d)
            },
            HarmonyEvents = new[]
            {
                new ScoreHarmonyEvent(0d, "Cmaj9"),
                new ScoreHarmonyEvent(2d, "G13")
            },
            Measures = new[]
            {
                new ScoreMeasure(1, 0d, 4d, 4, 4)
            }
        };

        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "PianoPracticeTool.Validation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "extended_chords.musicxml");

        try
        {
            EditorMusicXmlSaveService.SaveValidated(
                path,
                score,
                SongDifficulty.Beginner);
            var reloaded = EditorScoreLoader.Load(path).Score;
            AssertEqual(2, reloaded.HarmonyEvents.Count, "extended harmony count");
            AssertEqual("Cmaj9", reloaded.HarmonyEvents[0].Symbol, "major ninth round trip");
            AssertEqual("G13", reloaded.HarmonyEvents[1].Symbol, "dominant thirteenth round trip");

            var document = XDocument.Load(path);
            var kinds = document
                .Descendants()
                .Where(element => element.Name.LocalName == "kind")
                .Select(element => element.Value)
                .ToArray();
            AssertTrue(kinds.Contains("major-ninth"), "major-ninth MusicXML kind");
            AssertTrue(kinds.Contains("dominant-13th"), "dominant-13th MusicXML kind");
        }
        finally
        {
            DeleteDirectoryBestEffort(tempDirectory);
        }
    }

    private static void PortableAppPathsSurviveFolderMove()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "PianoPracticeTool.Validation",
            Guid.NewGuid().ToString("N"));
        var originalDirectory = Path.Combine(root, "original");
        var movedDirectory = Path.Combine(root, "moved");
        var originalScoreDirectory = Path.Combine(originalDirectory, "score_data");
        var movedScoreDirectory = Path.Combine(movedDirectory, "score_data");
        var originalSongPath = Path.Combine(originalScoreDirectory, "portable.musicxml");
        var movedSongPath = Path.Combine(movedScoreDirectory, "portable.musicxml");

        Directory.CreateDirectory(originalScoreDirectory);
        Directory.CreateDirectory(movedScoreDirectory);
        File.WriteAllText(originalSongPath, "<score-partwise version=\"3.1\" />");
        File.Copy(originalSongPath, movedSongPath);

        try
        {
            var originalSettingsService = new AppSettingsService(originalDirectory);
            var settings = originalSettingsService.Load();
            AssertEqual(
                EditorTimelineDragMode.TouchScroll,
                settings.EditorTimelineDragMode,
                "default editor timeline drag mode");
            settings.EditorTimelineDragMode = EditorTimelineDragMode.DirectFollow;
            settings.EditorPanPitchEnabled = false;
            settings.SongDifficultyOverrides[originalSongPath] = SongDifficulty.Beginner;
            originalSettingsService.Save(settings);

            var storedSettings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(originalSettingsService.SettingsPath))
                ?? throw new InvalidOperationException("stored settings were not readable");
            AssertEqual(
                "score_data",
                storedSettings.SongFolders.Single(),
                "stored default song folder should be relative");
            AssertEqual(
                Path.Combine("SoundFonts", AppSettingsService.DefaultSoundFontFileName),
                storedSettings.BuiltInSoundFontPath,
                "stored default SoundFont should be relative");
            AssertEqual(
                EditorTimelineDragMode.DirectFollow,
                storedSettings.EditorTimelineDragMode,
                "stored editor timeline drag mode");
            AssertTrue(
                !storedSettings.EditorPanPitchEnabled,
                "stored editor pitch pan preference");
            AssertTrue(
                storedSettings.SongDifficultyOverrides.ContainsKey(
                    Path.Combine("score_data", "portable.musicxml")),
                "stored difficulty override should be relative");

            var movedSettingsPath = Path.Combine(
                movedDirectory,
                Path.GetFileName(originalSettingsService.SettingsPath));
            File.Copy(originalSettingsService.SettingsPath, movedSettingsPath, overwrite: true);

            var movedSettings = new AppSettingsService(movedDirectory).Load();
            AssertEqual(
                Path.GetFullPath(movedScoreDirectory),
                movedSettings.SongFolders.Single(),
                "moved default song folder");
            AssertEqual(
                Path.Combine(
                    Path.GetFullPath(movedDirectory),
                    "SoundFonts",
                    AppSettingsService.DefaultSoundFontFileName),
                movedSettings.BuiltInSoundFontPath,
                "moved default SoundFont");
            AssertEqual(
                EditorTimelineDragMode.DirectFollow,
                movedSettings.EditorTimelineDragMode,
                "moved editor timeline drag mode");
            AssertTrue(
                !movedSettings.EditorPanPitchEnabled,
                "moved editor pitch pan preference");
            AssertTrue(
                movedSettings.SongDifficultyOverrides.ContainsKey(
                    Path.GetFullPath(movedSongPath)),
                "moved difficulty override");

            var historyService = new PracticeHistoryService(originalDirectory);
            var result = new PracticeResult(
                DateTimeOffset.UtcNow,
                "Portable",
                PracticeMode.WaitForCorrectNotes,
                PracticeHandMode.Both,
                0d,
                1d,
                1d,
                1d,
                1,
                PracticeScoring.CreateWaitScore(1, 0),
                null,
                0,
                1);
            historyService.Append(originalSongPath, result);

            var originalHistoryPath = Path.Combine(
                originalDirectory,
                "practice-history.json");
            var storedHistory = JsonSerializer.Deserialize<List<PracticeHistoryEntry>>(
                File.ReadAllText(originalHistoryPath))
                ?? throw new InvalidOperationException("stored history was not readable");
            AssertEqual(
                Path.Combine("score_data", "portable.musicxml"),
                storedHistory.Single().SongPath,
                "stored practice history path should be relative");

            var movedHistoryPath = Path.Combine(
                movedDirectory,
                "practice-history.json");
            File.Copy(originalHistoryPath, movedHistoryPath, overwrite: true);

            var movedHistory = new PracticeHistoryService(movedDirectory);
            AssertEqual(
                1,
                movedHistory.GetForSong(movedSongPath).Count,
                "practice history should follow the moved application folder");
        }
        finally
        {
            DeleteDirectoryBestEffort(root);
        }
    }

    private static void SongCatalogCacheKeysByPathAndHash()
    {
        var directory =
            Path.Combine(
                Path.GetTempPath(),
                "PianoPracticeTool.Validation",
                Guid.NewGuid().ToString("N"));
        var songDirectory =
            Path.Combine(
                directory,
                "songs");
        Directory.CreateDirectory(
            songDirectory);

        var firstPath =
            Path.Combine(
                songDirectory,
                "first.musicxml");
        var secondPath =
            Path.Combine(
                songDirectory,
                "second.musicxml");
        File.WriteAllText(
            firstPath,
            "same-content");
        File.WriteAllText(
            secondPath,
            "same-content");

        try
        {
            var score =
                CreateReferenceScore();
            var keyboard =
                KeyboardRangeAnalyzer.Analyze(
                    score);
            var firstEntry =
                new SongCatalogEntry(
                    firstPath,
                    score,
                    keyboard,
                    SongDifficulty.Beginner);
            var secondEntry =
                new SongCatalogEntry(
                    secondPath,
                    score,
                    keyboard,
                    SongDifficulty.Intermediate);
            var hash =
                SongCatalogCacheService
                    .ComputeSha256(
                        firstPath);
            AssertEqual(
                hash,
                SongCatalogCacheService
                    .ComputeSha256(
                        secondPath),
                "duplicate file hashes");

            var cache =
                new SongCatalogCacheService(
                    directory);
            cache.Store(
                firstPath,
                hash,
                fingeringMetadataHash: null,
                difficultyOverride: null,
                firstEntry);
            AssertTrue(
                cache.TryGet(
                    firstPath,
                    hash,
                    fingeringMetadataHash: null,
                    difficultyOverride: null,
                    out var cachedFirst),
                "same path and hash should hit the catalog cache");
            AssertEqual(
                SongDifficulty.Beginner,
                cachedFirst.Difficulty,
                "cached first difficulty");
            AssertTrue(
                !cache.TryGet(
                    secondPath,
                    hash,
                    fingeringMetadataHash: null,
                    difficultyOverride: null,
                    out _),
                "same content at another path must not reuse path-sensitive metadata");

            cache.Store(
                secondPath,
                hash,
                fingeringMetadataHash: null,
                difficultyOverride: null,
                secondEntry);
            cache.SaveIfChanged();

            var reloaded =
                new SongCatalogCacheService(
                    directory);
            AssertTrue(
                reloaded.TryGet(
                    secondPath,
                    hash,
                    fingeringMetadataHash: null,
                    difficultyOverride: null,
                    out var cachedSecond),
                "saved catalog cache should survive reload");
            AssertEqual(
                SongDifficulty.Intermediate,
                cachedSecond.Difficulty,
                "cached duplicate path should keep its own metadata");
            AssertTrue(
                !reloaded.TryGet(
                    secondPath,
                    hash,
                    fingeringMetadataHash: "changed-sidecar",
                    difficultyOverride: null,
                    out _),
                "fingering sidecar changes should invalidate a cached entry");

            File.WriteAllText(
                secondPath,
                "changed-content");
            var changedHash =
                SongCatalogCacheService
                    .ComputeSha256(
                        secondPath);
            AssertTrue(
                !string.Equals(
                    hash,
                    changedHash,
                    StringComparison.Ordinal),
                "changed content should produce a new hash");
            AssertTrue(
                !reloaded.TryGet(
                    secondPath,
                    changedHash,
                    fingeringMetadataHash: null,
                    difficultyOverride: null,
                    out _),
                "changed MusicXML content should invalidate a cached entry");

            File.Delete(
                firstPath);
            AssertEqual(
                1,
                reloaded.PruneMissingFiles(),
                "missing MusicXML cache entries should be pruned");
            reloaded.SaveIfChanged();

            var pruned =
                new SongCatalogCacheService(
                    directory);
            AssertTrue(
                !pruned.TryGet(
                    firstPath,
                    hash,
                    fingeringMetadataHash: null,
                    difficultyOverride: null,
                    out _),
                "deleted MusicXML should stay removed from the cache");
        }
        finally
        {
            Directory.Delete(
                directory,
                recursive: true);
        }
    }

    private static void LegacyNoteFlowSettingMigratesToVisibleRange()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "PianoPracticeTool.Validation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var settingsPath = Path.Combine(root, "piano-practice-settings.json");
            File.WriteAllText(
                settingsPath,
                """
                {
                  "NoteFlowSpeedPercent": 200,
                  "SongFolders": []
                }
                """);

            var settings = new AppSettingsService(root).Load();
            AssertEqual(
                50,
                settings.PianoRollVisibleRangePercent,
                "legacy fast note flow should migrate to a narrow visible range");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void OctaveShiftMovesEffectiveKeyboardRange()
    {
        var settings = new AppSettings
        {
            KeyboardLowestMidi = 48,
            KeyboardHighestMidi = 72,
            HasOctaveShift = true
        };

        var original = KeyboardCompatibilityService.CreatePlayableMidiRange(settings, 0);
        var shiftedUp = KeyboardCompatibilityService.CreatePlayableMidiRange(settings, 12);
        var shiftedDown = KeyboardCompatibilityService.CreatePlayableMidiRange(settings, -12);

        AssertEqual(48, original.LowestMidi, "unshifted keyboard lowest note");
        AssertEqual(72, original.HighestMidi, "unshifted keyboard highest note");
        AssertEqual(60, shiftedUp.LowestMidi, "+1 octave keyboard lowest note");
        AssertEqual(84, shiftedUp.HighestMidi, "+1 octave keyboard highest note");
        AssertEqual(36, shiftedDown.LowestMidi, "-1 octave keyboard lowest note");
        AssertEqual(60, shiftedDown.HighestMidi, "-1 octave keyboard highest note");

        settings.HasOctaveShift = false;
        var disabledShift = KeyboardCompatibilityService.CreatePlayableMidiRange(settings, 12);
        AssertEqual(48, disabledShift.LowestMidi, "disabled octave shift lowest note");
        AssertEqual(72, disabledShift.HighestMidi, "disabled octave shift highest note");
    }

    private static void SongLibraryCompatibilityPrefersUncompressedHandPractice()
    {
        var settings = new AppSettings
        {
            KeyboardLowestMidi = 48,
            KeyboardHighestMidi = 72,
            HasOctaveShift = true
        };
        var score = new MusicScore
        {
            Title = "Separate Hands",
            TempoBpm = 120d,
            Notes = new[]
            {
                new ScoreNote
                {
                    Id = 0,
                    MidiNote = 72,
                    StartBeat = 0d,
                    DurationBeat = 1d,
                    Hand = Hand.Right,
                    Staff = 1,
                    Voice = "1"
                },
                new ScoreNote
                {
                    Id = 1,
                    MidiNote = 84,
                    StartBeat = 1d,
                    DurationBeat = 1d,
                    Hand = Hand.Right,
                    Staff = 1,
                    Voice = "1"
                },
                new ScoreNote
                {
                    Id = 2,
                    MidiNote = 36,
                    StartBeat = 2d,
                    DurationBeat = 1d,
                    Hand = Hand.Left,
                    Staff = 2,
                    Voice = "1"
                },
                new ScoreNote
                {
                    Id = 3,
                    MidiNote = 48,
                    StartBeat = 3d,
                    DurationBeat = 1d,
                    Hand = Hand.Left,
                    Staff = 2,
                    Voice = "1"
                }
            },
            Measures = new[]
            {
                new ScoreMeasure(1, 0d, 4d, 4, 4)
            }
        };

        var compression =
            SongOctaveCompressionService.Analyze(
                score,
                settings.KeyboardLowestMidi,
                settings.KeyboardHighestMidi);
        AssertTrue(
            compression.CanCompress,
            "full-song compression should be available for the priority test");

        var availability =
            KeyboardCompatibilityService.AnalyzeLibraryAvailability(
                score,
                settings,
                compression);
        AssertEqual(
            KeyboardPracticeAvailabilityLevel.EitherSingleHand,
            availability.Level,
            "separate octave shifts should allow either hand independently");
        AssertTrue(
            !availability.RequiresSongCompression,
            "uncompressed single-hand practice should win over compressed both-hands practice");
        AssertEqual(
            "片手可",
            availability.ShortText,
            "library badge should make single-hand availability visible");
    }

    private static void SongLibraryCompatibilityFallsBackToCompression()
    {
        var settings = new AppSettings
        {
            KeyboardLowestMidi = 48,
            KeyboardHighestMidi = 72,
            HasOctaveShift = true
        };
        var score = new MusicScore
        {
            Title = "Compression Fallback",
            TempoBpm = 120d,
            Notes = new[]
            {
                new ScoreNote
                {
                    Id = 0,
                    MidiNote = 36,
                    StartBeat = 0d,
                    DurationBeat = 1d,
                    Hand = Hand.Right,
                    Staff = 1,
                    Voice = "1"
                },
                new ScoreNote
                {
                    Id = 1,
                    MidiNote = 72,
                    StartBeat = 1d,
                    DurationBeat = 1d,
                    Hand = Hand.Right,
                    Staff = 1,
                    Voice = "1"
                },
                new ScoreNote
                {
                    Id = 2,
                    MidiNote = 37,
                    StartBeat = 2d,
                    DurationBeat = 1d,
                    Hand = Hand.Left,
                    Staff = 2,
                    Voice = "1"
                },
                new ScoreNote
                {
                    Id = 3,
                    MidiNote = 73,
                    StartBeat = 3d,
                    DurationBeat = 1d,
                    Hand = Hand.Left,
                    Staff = 2,
                    Voice = "1"
                }
            },
            Measures = new[]
            {
                new ScoreMeasure(1, 0d, 4d, 4, 4)
            }
        };

        var compression =
            SongOctaveCompressionService.Analyze(
                score,
                settings.KeyboardLowestMidi,
                settings.KeyboardHighestMidi);
        AssertTrue(
            compression.CanCompress,
            "compression fallback should be available");

        var availability =
            KeyboardCompatibilityService.AnalyzeLibraryAvailability(
                score,
                settings,
                compression);
        AssertEqual(
            KeyboardPracticeAvailabilityLevel.BothHands,
            availability.Level,
            "compression should make both hands playable");
        AssertTrue(
            availability.RequiresSongCompression,
            "fallback should report that song compression is required");
        AssertEqual(
            "両手可(圧縮)",
            availability.ShortText,
            "library badge should show compression requirement");
    }

    private static void PracticeHandAssignmentCorrectsCrossingStaffNotes()
    {
        var score = new MusicScore
        {
            Title = "Hand Correction",
            TempoBpm = 120d,
            Notes = new[]
            {
                new ScoreNote
                {
                    Id = 0,
                    MidiNote = 60,
                    StartBeat = 0d,
                    DurationBeat = 2d,
                    Staff = 1,
                    Hand = Hand.Right
                },
                new ScoreNote
                {
                    Id = 1,
                    MidiNote = 69,
                    StartBeat = 0d,
                    DurationBeat = 2d,
                    Staff = 1,
                    Hand = Hand.Right
                },
                new ScoreNote
                {
                    Id = 2,
                    MidiNote = 65,
                    StartBeat = 1d,
                    DurationBeat = 0.5d,
                    Staff = 2,
                    Hand = Hand.Left
                },
                new ScoreNote
                {
                    Id = 3,
                    MidiNote = 48,
                    StartBeat = 2d,
                    DurationBeat = 1d,
                    Staff = 2,
                    Hand = Hand.Left
                }
            }
        };

        var changed =
            PracticeScorePreparation
                .ApplyAutomaticHandCorrections(
                    score);

        AssertEqual(
            1,
            changed,
            "crossing staff correction count");
        AssertEqual(
            Hand.Right,
            score.Notes.Single(note => note.Id == 2).Hand,
            "note inside a sustained right-hand range should move to the right hand");
        AssertEqual(
            Hand.Left,
            score.Notes.Single(note => note.Id == 3).Hand,
            "ordinary lower-staff note should stay on the left hand");
    }

    private static void PracticeHandAssignmentAvoidsRapidIsolatedOctaveJump()
    {
        var score = new MusicScore
        {
            Title = "Rapid Hand Reassignment",
            TempoBpm = 120d,
            Notes = new[]
            {
                new ScoreNote
                {
                    Id = 0,
                    MidiNote = 51,
                    StartBeat = 0d,
                    DurationBeat = 1d,
                    Staff = 2,
                    Hand = Hand.Left
                },
                new ScoreNote
                {
                    Id = 1,
                    MidiNote = 53,
                    StartBeat = 0.5d,
                    DurationBeat = 0.5d,
                    Staff = 1,
                    Hand = Hand.Right
                },
                new ScoreNote
                {
                    Id = 2,
                    MidiNote = 51,
                    StartBeat = 1d,
                    DurationBeat = 1d,
                    Staff = 2,
                    Hand = Hand.Left
                },
                new ScoreNote
                {
                    Id = 3,
                    MidiNote = 58,
                    StartBeat = 1d,
                    DurationBeat = 0.25d,
                    Staff = 1,
                    Hand = Hand.Right
                },
                new ScoreNote
                {
                    Id = 4,
                    MidiNote = 69,
                    StartBeat = 1.25d,
                    DurationBeat = 0.5d,
                    Staff = 1,
                    Hand = Hand.Right
                }
            }
        };

        var changed =
            PracticeScorePreparation
                .ApplyAutomaticHandCorrections(
                    score);

        AssertEqual(
            2,
            changed,
            "rapid low pickup correction count");
        AssertEqual(
            Hand.Left,
            score.Notes.Single(note => note.Id == 1).Hand,
            "low pickup should move to the left hand");
        AssertEqual(
            Hand.Left,
            score.Notes.Single(note => note.Id == 3).Hand,
            "note before the rapid octave jump should move to the left hand");
        AssertEqual(
            Hand.Right,
            score.Notes.Single(note => note.Id == 4).Hand,
            "upper melody should remain on the right hand");
    }

    private static void FingeringAvoidsBlackKeyThumbWhenAlternativeExists()
    {
        var score = new MusicScore
        {
            Title = "Black Key Start",
            TempoBpm = 120d,
            Notes = new[]
            {
                new ScoreNote
                {
                    Id = 0,
                    MidiNote = 61,
                    StartBeat = 0d,
                    DurationBeat = 0.25d,
                    Staff = 1,
                    Hand = Hand.Right
                },
                new ScoreNote
                {
                    Id = 1,
                    MidiNote = 62,
                    StartBeat = 0.25d,
                    DurationBeat = 0.25d,
                    Staff = 1,
                    Hand = Hand.Right
                },
                new ScoreNote
                {
                    Id = 2,
                    MidiNote = 64,
                    StartBeat = 0.5d,
                    DurationBeat = 0.5d,
                    Staff = 1,
                    Hand = Hand.Right
                }
            }
        };

        FingeringGenerator.Generate(
            score);

        AssertTrue(
            score.Notes[0].Finger != 1,
            "an easy phrase should not begin with the thumb on a black key");
        AssertTrue(
            score.Notes[0].Finger is 2 or 3,
            "black-key phrase start should favor a long finger");
    }

    private static void FingeringKeepsThumbForBlackKeyOctave()
    {
        var score = new MusicScore
        {
            Title = "Black Key Octave",
            TempoBpm = 120d,
            Notes = new[]
            {
                new ScoreNote
                {
                    Id = 0,
                    MidiNote = 61,
                    StartBeat = 0d,
                    DurationBeat = 1d,
                    Staff = 1,
                    Hand = Hand.Right
                },
                new ScoreNote
                {
                    Id = 1,
                    MidiNote = 73,
                    StartBeat = 0d,
                    DurationBeat = 1d,
                    Staff = 1,
                    Hand = Hand.Right
                }
            }
        };

        FingeringGenerator.Generate(
            score);

        AssertEqual(
            1,
            score.Notes[0].Finger,
            "black-key octave lower note should keep the thumb");
        AssertEqual(
            5,
            score.Notes[1].Finger,
            "black-key octave upper note should use the fifth finger");
    }

    private static void FingeringRespectsHeldFingerPositions()
    {
        var score = new MusicScore
        {
            Title = "Held Finger Position",
            TempoBpm = 120d,
            Notes = new[]
            {
                new ScoreNote
                {
                    Id = 0,
                    MidiNote = 67,
                    StartBeat = 0d,
                    DurationBeat = 2d,
                    Staff = 1,
                    Hand = Hand.Right
                },
                new ScoreNote
                {
                    Id = 1,
                    MidiNote = 60,
                    StartBeat = 0.5d,
                    DurationBeat = 0.5d,
                    Staff = 1,
                    Hand = Hand.Right
                },
                new ScoreNote
                {
                    Id = 2,
                    MidiNote = 64,
                    StartBeat = 0.5d,
                    DurationBeat = 0.5d,
                    Staff = 1,
                    Hand = Hand.Right
                }
            }
        };

        FingeringGenerator.Generate(
            score);

        var heldFinger =
            score.Notes.Single(note =>
                note.Id == 0).Finger;
        var lowerFingers =
            score.Notes
                .Where(note =>
                    note.Id is 1 or 2)
                .OrderBy(note =>
                    note.MidiNote)
                .Select(note =>
                    note.Finger)
                .ToArray();

        AssertTrue(
            heldFinger >= 3,
            "a held right-hand note must leave two lower fingers available");
        AssertTrue(
            lowerFingers.All(finger =>
                finger < heldFinger),
            "notes below a held right-hand note must use lower-numbered fingers");
        AssertTrue(
            lowerFingers.Distinct().Count()
                == lowerFingers.Length,
            "simultaneous lower notes must use distinct fingers");
    }

    private static void FingeringPreservesExplicitScoreAnnotations()
    {
        var score = new MusicScore
        {
            Title = "Explicit Fingering",
            TempoBpm = 120d,
            Notes = new[]
            {
                new ScoreNote
                {
                    Id = 0,
                    MidiNote = 61,
                    StartBeat = 0d,
                    DurationBeat = 0.25d,
                    Staff = 1,
                    Hand = Hand.Right,
                    Finger = 4
                },
                new ScoreNote
                {
                    Id = 1,
                    MidiNote = 62,
                    StartBeat = 0.25d,
                    DurationBeat = 0.25d,
                    Staff = 1,
                    Hand = Hand.Right
                },
                new ScoreNote
                {
                    Id = 2,
                    MidiNote = 64,
                    StartBeat = 0.5d,
                    DurationBeat = 0.5d,
                    Staff = 1,
                    Hand = Hand.Right
                }
            }
        };

        FingeringGenerator.Generate(
            score);

        AssertEqual(
            4,
            score.Notes[0].Finger,
            "explicit MusicXML fingering should not be overwritten");
        AssertTrue(
            score.Notes.Skip(1).All(note =>
                note.Finger is >= 1 and <= 5),
            "missing neighboring fingerings should still be generated");
    }

    private static void PracticeScoreDetectsAndCorrectsRepeatedKeyOverlap()
    {
        var score = new MusicScore
        {
            Title = "Repeated Key",
            TempoBpm = 120d,
            Notes = new[]
            {
                new ScoreNote
                {
                    Id = 0,
                    MidiNote = 60,
                    StartBeat = 0d,
                    DurationBeat = 2d,
                    Staff = 1,
                    Hand = Hand.Right
                },
                new ScoreNote
                {
                    Id = 1,
                    MidiNote = 60,
                    StartBeat = 0.5d,
                    DurationBeat = 0.5d,
                    Staff = 2,
                    Hand = Hand.Left
                }
            }
        };

        var analysis =
            PracticeScorePreparation
                .AnalyzeConsistency(
                    score);
        AssertEqual(
            1,
            analysis.Issues.Count,
            "repeated key overlap issue count");
        AssertTrue(
            analysis.CanAutoCorrect,
            "repeated key overlap should be auto-correctable");

        var corrected =
            PracticeScorePreparation
                .ApplyPhysicalCorrections(
                    score);
        AssertClose(
            0.5d,
            corrected.Notes.Single(note => note.Id == 0).DurationBeat,
            "first C4 should end when the retrigger begins");
        AssertClose(
            2d,
            score.Notes.Single(note => note.Id == 0).DurationBeat,
            "physical correction must not mutate the source score");
        AssertEqual(
            0,
            PracticeScorePreparation
                .AnalyzeConsistency(
                    corrected)
                .AutoCorrectableIssueCount,
            "corrected score should remove repeated-key conflicts");
    }

    private static void PracticeScoreWarnsWhenOneHandExceedsFiveKeys()
    {
        var notes =
            Enumerable
                .Range(
                    0,
                    6)
                .Select(index =>
                    new ScoreNote
                    {
                        Id = index,
                        MidiNote = 60 + index,
                        StartBeat = 0d,
                        DurationBeat = 1d,
                        Staff = 1,
                        Hand = Hand.Right
                    })
                .ToArray();
        var score = new MusicScore
        {
            Title = "Six Keys",
            TempoBpm = 120d,
            Notes = notes
        };

        var analysis =
            PracticeScorePreparation
                .AnalyzeConsistency(
                    score);
        AssertTrue(
            analysis.Issues.Any(issue =>
                issue.Kind
                    == ScoreConsistencyIssueKind.TooManySimultaneousKeys),
            "six simultaneous right-hand keys should be reported");
        AssertTrue(
            analysis.ManualReviewIssueCount > 0,
            "six-key hand span should require manual review");
    }

    private static void ListenMemorizationNavigationReturnsToReplayStart()
    {
        using var soundService =
            new SilentMidiSoundService();
        var workflow =
            new PracticeWorkflow(
                soundService);

        workflow.Prepare(
            CreateReferenceScore());
        workflow.SetMode(
            PracticeMode.Listen);
        workflow.Seek(
            4d);
        workflow.TogglePlayback();
        workflow.TogglePlayback();
        workflow.Seek(
            6d);

        var returned =
            workflow.ReturnToListenReplayStart();
        AssertEqual(
            PracticeRunState.Paused,
            returned.State,
            "return-to-start should remain paused");
        AssertClose(
            4d,
            returned.CurrentBeat,
            "return-to-start beat");

        var oneBeatLater =
            workflow.SeekListenForMemorization(
                1,
                byMeasure: false);
        AssertClose(
            5d,
            oneBeatLater.CurrentBeat,
            "wheel step should move one beat forward");

        var measureStart =
            workflow.SeekListenForMemorization(
                -1,
                byMeasure: true);
        AssertClose(
            4d,
            measureStart.CurrentBeat,
            "measure wheel step should move to current measure start");

        var previousMeasure =
            workflow.SeekListenForMemorization(
                -1,
                byMeasure: true);
        AssertClose(
            0d,
            previousMeasure.CurrentBeat,
            "second measure wheel step should move to previous measure");
    }

    private static void SongOctaveCompressionIsIndependentFromDeviceShift()
    {
        var score = new MusicScore
        {
            Title = "Compression",
            Composer = "Composer",
            TempoBpm = 120d,
            Notes = new[]
            {
                CreateValidationNote(0, 36, 0d, 1d),
                CreateValidationNote(1, 76, 1d, 1d)
            },
            Measures = new[]
            {
                new ScoreMeasure(1, 0d, 4d, 4, 4)
            }
        };

        var analysis = SongOctaveCompressionService.Analyze(
            score,
            48,
            72);
        AssertTrue(analysis.IsNeeded, "compression should be needed");
        AssertTrue(analysis.CanCompress, "compression should be available");

        var compressed = SongOctaveCompressionService.Compress(
            score,
            48,
            72);
        AssertTrue(
            compressed.Notes.All(note =>
                note.MidiNote is >= 48 and <= 72),
            "compressed notes should fit the physical keyboard range");
        for (var index = 0; index < score.Notes.Count; index++)
        {
            AssertEqual(
                score.Notes[index].MidiNote % 12,
                compressed.Notes[index].MidiNote % 12,
                $"compressed pitch class #{index}");
            AssertClose(
                score.Notes[index].StartBeat,
                compressed.Notes[index].StartBeat,
                $"compressed start beat #{index}");
        }

        var collisionScore = new MusicScore
        {
            Title = "Unsafe Compression",
            TempoBpm = 120d,
            Notes = new[]
            {
                CreateValidationNote(0, 36, 0d, 1d),
                CreateValidationNote(1, 60, 0d, 1d)
            },
            Measures = new[]
            {
                new ScoreMeasure(1, 0d, 4d, 4, 4)
            }
        };
        var collision = SongOctaveCompressionService.Analyze(
            collisionScore,
            48,
            72);
        AssertTrue(
            !collision.CanCompress,
            "compression that collapses simultaneous notes should be rejected");
    }

    private static void SongMetadataUpdatePreservesScoreContent()
    {
        var score = CreateReferenceScore();
        var updated = SongMetadataService.CreateUpdatedScore(
            score,
            new SongMetadataUpdate(
                "Renamed Score",
                "Updated Composer",
                SongDifficulty.Advanced,
                UpdateScale: true,
                ScaleFifths: -3,
                ScaleId: "harmonic-minor"));

        AssertEqual("Renamed Score", updated.Title, "metadata title");
        AssertEqual("Updated Composer", updated.Composer, "metadata composer");
        AssertEqual(score.Notes.Count, updated.Notes.Count, "metadata note count");
        AssertEqual(score.HarmonyEvents.Count, updated.HarmonyEvents.Count, "metadata harmony count");
        AssertEqual(-3, updated.KeySignatureEvents[0].Fifths, "metadata initial fifths");
        AssertEqual("harmonic-minor", updated.KeySignatureEvents[0].ScaleType, "metadata scale type");
        AssertTrue(
            updated.KeySignatureEvents.Any(item =>
                Math.Abs(item.Beat - 2d)
                    <= ScoreTiming.EventBeatTolerance),
            "metadata update should preserve later key changes");
    }

    private static void ReferenceAudioStartAdjustsBeforeFirstSyncPoint()
    {
        var score = CreateReferenceScore();
        var baseline =
            ReferenceAudioSynchronizer.EnsureOuterAnchors(
                new[]
                {
                    new ReferenceAudioSyncPoint(0d, 0d),
                    new ReferenceAudioSyncPoint(4d, 4d)
                },
                score.LengthBeats,
                8d);
        var delayed =
            ReferenceAudioSynchronizer.AdjustAudioSegment(
                score,
                baseline,
                8d,
                2d,
                1.25d);

        AssertEqual(
            4,
            delayed.Count,
            "delayed sync point count");
        AssertClose(
            0d,
            delayed[0].AudioSeconds,
            "source zero marker should remain");
        AssertClose(
            1.25d,
            delayed[1].AudioSeconds,
            "score start should move later in the source");
        AssertClose(
            4d,
            delayed[2].AudioSeconds,
            "first existing sync point must stay fixed");
        AssertClose(
            8d,
            delayed[3].AudioSeconds,
            "score end must stay fixed");

        var delayedSynchronizer =
            new ReferenceAudioSynchronizer(
                score,
                delayed);
        AssertClose(
            1.25d,
            delayedSynchronizer.ScoreBeatToAudioSeconds(0d),
            "delayed score start source position");
        AssertClose(
            2.625d,
            delayedSynchronizer.ScoreBeatToAudioSeconds(2d),
            "segment before first sync point should stretch");
        AssertEqual(
            1,
            delayedSynchronizer.AudioOnlyGaps.Count,
            "delaying source should create one lead-in gap");

        var corrected =
            ReferenceAudioSynchronizer.AdjustAudioSegment(
                score,
                delayed,
                8d,
                2d,
                -0.75d);
        AssertClose(
            0.5d,
            corrected[1].AudioSeconds,
            "start adjustment should be reversible toward the front");
        AssertClose(
            4d,
            corrected[2].AudioSeconds,
            "first sync point should remain fixed while correcting");

        var reset =
            ReferenceAudioSynchronizer.AdjustAudioSegment(
                score,
                corrected,
                8d,
                2d,
                -2d);
        AssertEqual(
            3,
            reset.Count,
            "moving back to source zero should collapse the lead-in marker");
        AssertClose(
            0d,
            reset[0].AudioSeconds,
            "source start must clamp at zero");
        AssertClose(
            4d,
            reset[1].AudioSeconds,
            "first sync point must remain fixed after reset");
        var resetSynchronizer =
            new ReferenceAudioSynchronizer(
                score,
                reset);
        AssertEqual(
            0,
            resetSynchronizer.AudioOnlyGaps.Count,
            "reset source adjustment should remove the lead-in gap");

        var clampedAtFirstSync =
            ReferenceAudioSynchronizer.AdjustAudioSegment(
                score,
                reset,
                8d,
                2d,
                10d);
        AssertTrue(
            clampedAtFirstSync[1].AudioSeconds
                < clampedAtFirstSync[2].AudioSeconds,
            "start position must not cross the first sync point");
        AssertClose(
            4d,
            clampedAtFirstSync[2].AudioSeconds,
            "first sync point must stay fixed at the upper clamp");
    }

    private static void ReferenceAudioSegmentAdjustmentPreservesSyncBoundaries()
    {
        var score = CreateReferenceScore();
        var baseline =
            ReferenceAudioSynchronizer.EnsureOuterAnchors(
                new[]
                {
                    new ReferenceAudioSyncPoint(0d, 0d),
                    new ReferenceAudioSyncPoint(2d, 2d),
                    new ReferenceAudioSyncPoint(6d, 6d)
                },
                score.LengthBeats,
                8d);

        var shiftedDown =
            ReferenceAudioSynchronizer.AdjustAudioSegment(
                score,
                baseline,
                8d,
                4d,
                1.5d);

        AssertTrue(
            shiftedDown.Count(point =>
                Math.Abs(
                    point.ScoreBeat
                    - 2d)
                <= ScoreTiming.EventBeatTolerance)
            == 2,
            "middle segment start should split into two anchors");
        var startBoundary =
            shiftedDown
                .Where(point =>
                    Math.Abs(
                        point.ScoreBeat
                        - 2d)
                    <= ScoreTiming.EventBeatTolerance)
                .OrderBy(point =>
                    point.AudioSeconds)
                .ToArray();
        AssertClose(
            2d,
            startBoundary[0].AudioSeconds,
            "previous segment end should stay fixed");
        AssertClose(
            3.5d,
            startBoundary[1].AudioSeconds,
            "current segment start should move later");
        AssertClose(
            6d,
            shiftedDown
                .Single(point =>
                    Math.Abs(
                        point.ScoreBeat
                        - 6d)
                    <= ScoreTiming.EventBeatTolerance)
                .AudioSeconds,
            "middle segment end should stay fixed");

        var shiftedSynchronizer =
            new ReferenceAudioSynchronizer(
                score,
                shiftedDown);
        AssertTrue(
            shiftedSynchronizer.AudioOnlyGaps.Any(gap =>
                Math.Abs(
                    gap.ScoreBeat
                    - 2d)
                <= ScoreTiming.EventBeatTolerance
                && Math.Abs(
                    gap.StartAudioSeconds
                    - 2d)
                    <= ScoreTiming.EventBeatTolerance
                && Math.Abs(
                    gap.EndAudioSeconds
                    - 3.5d)
                    <= ScoreTiming.EventBeatTolerance),
            "moving a middle segment start should create an audio-only gap");

        var restored =
            ReferenceAudioSynchronizer.AdjustAudioSegment(
                score,
                shiftedDown,
                8d,
                4d,
                -1.5d);
        AssertEqual(
            4,
            restored.Count,
            "reversing the drag should collapse the temporary gap");
        AssertClose(
            2d,
            restored[1].AudioSeconds,
            "reversing should restore the middle segment start");

        var shiftedUp =
            ReferenceAudioSynchronizer.AdjustAudioSegment(
                score,
                baseline,
                8d,
                4d,
                -1.25d);
        var endBoundary =
            shiftedUp
                .Where(point =>
                    Math.Abs(
                        point.ScoreBeat
                        - 6d)
                    <= ScoreTiming.EventBeatTolerance)
                .OrderBy(point =>
                    point.AudioSeconds)
                .ToArray();
        AssertEqual(
            2,
            endBoundary.Length,
            "middle segment end should split into two anchors");
        AssertClose(
            4.75d,
            endBoundary[0].AudioSeconds,
            "current segment end should move earlier");
        AssertClose(
            6d,
            endBoundary[1].AudioSeconds,
            "next segment start should stay fixed");
    }

    private static void ReferenceAudioEndAdjustmentCreatesOutroGap()
    {
        var score = CreateReferenceScore();
        var baseline =
            ReferenceAudioSynchronizer.EnsureOuterAnchors(
                new[]
                {
                    new ReferenceAudioSyncPoint(0d, 0d),
                    new ReferenceAudioSyncPoint(4d, 4d)
                },
                score.LengthBeats,
                10d);

        AssertClose(
            10d,
            baseline[^1].AudioSeconds,
            "source end should become the default score-end anchor");

        var shiftedDown =
            ReferenceAudioSynchronizer.AdjustAudioSegment(
                score,
                baseline,
                10d,
                score.LengthBeats - 1d,
                1d);
        var finalSegmentStart =
            shiftedDown
                .Where(point =>
                    Math.Abs(
                        point.ScoreBeat
                        - 4d)
                    <= ScoreTiming.EventBeatTolerance)
                .OrderBy(point =>
                    point.AudioSeconds)
                .ToArray();
        AssertEqual(
            2,
            finalSegmentStart.Length,
            "downward final-segment adjustment should split its start boundary");
        AssertClose(
            4d,
            finalSegmentStart[0].AudioSeconds,
            "previous segment end should stay fixed at the final segment boundary");
        AssertClose(
            5d,
            finalSegmentStart[1].AudioSeconds,
            "final segment start should move later when dragged down");
        AssertClose(
            10d,
            shiftedDown[^1].AudioSeconds,
            "downward final-segment adjustment must keep the score end fixed");

        var adjusted =
            ReferenceAudioSynchronizer.AdjustAudioSegment(
                score,
                baseline,
                10d,
                score.LengthBeats - 1d,
                -1.5d);
        var endBoundary =
            adjusted
                .Where(point =>
                    Math.Abs(
                        point.ScoreBeat
                        - score.LengthBeats)
                    <= ScoreTiming.EventBeatTolerance)
                .OrderBy(point =>
                    point.AudioSeconds)
                .ToArray();

        AssertEqual(
            2,
            endBoundary.Length,
            "adjusted score end should keep the source-end anchor");
        AssertClose(
            8.5d,
            endBoundary[0].AudioSeconds,
            "score end should move earlier in the source");
        AssertClose(
            10d,
            endBoundary[1].AudioSeconds,
            "source file end should remain fixed");

        var synchronizer =
            new ReferenceAudioSynchronizer(
                score,
                adjusted);
        AssertClose(
            8.5d,
            synchronizer.ScoreBeatToAudioSecondsForRangeEnd(
                score.LengthBeats),
            "score playback should stop at the adjusted end position");
        AssertTrue(
            synchronizer.AudioOnlyGaps.Any(gap =>
                Math.Abs(
                    gap.ScoreBeat
                    - score.LengthBeats)
                <= ScoreTiming.EventBeatTolerance
                && Math.Abs(
                    gap.DurationSeconds
                    - 1.5d)
                    <= ScoreTiming.EventBeatTolerance),
            "source tail after the score end should be an audio-only gap");

        var restored =
            ReferenceAudioSynchronizer.AdjustAudioSegment(
                score,
                adjusted,
                10d,
                score.LengthBeats - 1d,
                1.5d);
        AssertEqual(
            3,
            restored.Count,
            "restoring the score end should collapse the outro gap");
        AssertClose(
            10d,
            restored[^1].AudioSeconds,
            "restored score end should reach the source end");
    }

    private static void SynchronizedScorePreviewRetriggersAdjacentRepeatedNotes()
    {
        var score =
            new MusicScore
            {
                Title =
                    "Repeated note preview",
                TempoBpm = 120d,
                Notes =
                    new[]
                    {
                        new ScoreNote
                        {
                            Id = 1,
                            MidiNote = 60,
                            StartBeat = 0d,
                            DurationBeat = 1d,
                            Staff = 1,
                            Voice = "1",
                            Hand = Hand.Right
                        },
                        new ScoreNote
                        {
                            Id = 2,
                            MidiNote = 60,
                            StartBeat = 1d,
                            DurationBeat = 1d,
                            Staff = 1,
                            Voice = "1",
                            Hand = Hand.Right
                        }
                    },
                Measures =
                    new[]
                    {
                        new ScoreMeasure(
                            1,
                            0d,
                            4d,
                            4,
                            4)
                    }
            };
        var tracker =
            new EditorScorePreviewEventTracker(
                score);

        var initial =
            tracker.AdvanceTo(
                0d);
        AssertEqual(
            1,
            initial.Count,
            "initial repeated-note preview event count");
        AssertEqual(
            EditorScorePreviewMidiEventType.NoteOn,
            initial[0].EventType,
            "initial repeated-note preview event");
        AssertEqual(
            60,
            initial[0].MidiNote,
            "initial repeated-note MIDI note");

        var repeated =
            tracker.AdvanceTo(
                1.1d);
        AssertEqual(
            2,
            repeated.Count,
            "adjacent repeated note should retrigger");
        AssertEqual(
            EditorScorePreviewMidiEventType.NoteOff,
            repeated[0].EventType,
            "adjacent repeated note should release the previous note first");
        AssertEqual(
            EditorScorePreviewMidiEventType.NoteOn,
            repeated[1].EventType,
            "adjacent repeated note should start the next note");
        AssertEqual(
            60,
            repeated[0].MidiNote,
            "repeated note-off MIDI note");
        AssertEqual(
            60,
            repeated[1].MidiNote,
            "repeated note-on MIDI note");
    }

    private static void ReferenceAudioSyncSupportsSourceOnlyGaps()
    {
        var score = new MusicScore
        {
            Title = "Reference Sync",
            TempoBpm = 120d,
            Notes = Array.Empty<ScoreNote>(),
            TempoEvents = new[]
            {
                new ScoreTempoEvent(0d, 120d)
            },
            Measures = new[]
            {
                new ScoreMeasure(1, 0d, 4d, 4, 4),
                new ScoreMeasure(2, 4d, 4d, 4, 4)
            }
        };
        var synchronizer = new ReferenceAudioSynchronizer(
            score,
            new[]
            {
                new ReferenceAudioSyncPoint(0d, 5d),
                new ReferenceAudioSyncPoint(4d, 9d),
                new ReferenceAudioSyncPoint(4d, 21d),
                new ReferenceAudioSyncPoint(8d, 25d)
            });

        AssertEqual(
            1,
            synchronizer.AudioOnlyGaps.Count,
            "reference audio gap count");
        AssertClose(
            12d,
            synchronizer.AudioOnlyGaps[0].DurationSeconds,
            "reference audio gap duration");
        AssertClose(
            21d,
            synchronizer.ScoreBeatToAudioSeconds(4d),
            "exact score boundary should use the post-gap audio point");
        AssertClose(
            9d,
            synchronizer.ScoreBeatToAudioSecondsForRangeEnd(4d),
            "range ending at the gap boundary should use the pre-gap audio point");
        AssertClose(
            4d,
            synchronizer.AudioSecondsToScoreBeat(15d),
            "audio-only region should hold the score position");
        AssertTrue(
            synchronizer.IsAudioOnlyGap(9d),
            "audio-only region should begin at the first duplicate-beat source point");
        AssertTrue(
            synchronizer.IsAudioOnlyGap(15d),
            "audio-only region should be detectable");
        AssertTrue(
            !synchronizer.IsAudioOnlyGap(21d),
            "score playback should resume at the second duplicate-beat source point");
        AssertTrue(
            !synchronizer.IsAudioOnlyGap(23d),
            "normal synchronized region must not be marked as audio-only");
        AssertClose(
            21d,
            synchronizer.SkipAudioOnlyGaps(9d),
            "score-synchronized playback should jump to the end of a cut region");
        AssertClose(
            21d,
            synchronizer.SkipAudioOnlyGaps(15d),
            "playback inside a cut region should resume immediately after it");
        AssertClose(
            23d,
            synchronizer.SkipAudioOnlyGaps(23d),
            "normal synchronized audio should not be moved");
    }

    private static void MeasureEditsKeepReferenceAudioSyncAligned()
    {
        var leadingSession = new ScoreEditSession(
            EditableMusicScore.FromMusicScore(
                CreateReferenceScore()));
        leadingSession.Score.ReferenceAudioSyncPoints.AddRange(
            new[]
            {
                new ReferenceAudioSyncPoint(0d, 0d),
                new ReferenceAudioSyncPoint(0d, 1.5d),
                new ReferenceAudioSyncPoint(4d, 4d),
                new ReferenceAudioSyncPoint(8d, 8d)
            });
        leadingSession.InsertMeasureBefore(1);
        AssertTrue(
            leadingSession.Score.ReferenceAudioSyncPoints.Any(point =>
                Math.Abs(point.ScoreBeat)
                    <= ScoreTiming.EventBeatTolerance
                && Math.Abs(point.AudioSeconds)
                    <= ScoreTiming.EventBeatTolerance),
            "leading insertion should preserve the fixed source-start marker");
        AssertTrue(
            leadingSession.Score.ReferenceAudioSyncPoints.Any(point =>
                Math.Abs(point.ScoreBeat - 4d)
                    <= ScoreTiming.EventBeatTolerance
                && Math.Abs(point.AudioSeconds)
                    <= ScoreTiming.EventBeatTolerance),
            "leading insertion should move the former score-start anchor with the score");
        AssertTrue(
            leadingSession.Score.ReferenceAudioSyncPoints.Any(point =>
                Math.Abs(point.ScoreBeat - 4d)
                    <= ScoreTiming.EventBeatTolerance
                && Math.Abs(point.AudioSeconds - 1.5d)
                    <= ScoreTiming.EventBeatTolerance),
            "leading insertion should move the adjusted score-start anchor with the score");
        AssertTrue(
            leadingSession.Score.ReferenceAudioSyncPoints.Any(point =>
                Math.Abs(point.ScoreBeat - 12d)
                    <= ScoreTiming.EventBeatTolerance
                && Math.Abs(point.AudioSeconds - 8d)
                    <= ScoreTiming.EventBeatTolerance),
            "leading insertion should move the fixed source-end marker to the new score end");

        var session = new ScoreEditSession(
            EditableMusicScore.FromMusicScore(
                CreateReferenceScore()));
        session.Score.ReferenceAudioSyncPoints.AddRange(
            new[]
            {
                new ReferenceAudioSyncPoint(0d, 0d),
                new ReferenceAudioSyncPoint(4d, 3d),
                new ReferenceAudioSyncPoint(8d, 7d)
            });

        session.InsertMeasureAfter(1);
        AssertClose(
            0d,
            session.Score.ReferenceAudioSyncPoints[0].ScoreBeat,
            "sync point before insertion");
        AssertClose(
            8d,
            session.Score.ReferenceAudioSyncPoints[1].ScoreBeat,
            "sync point at insertion boundary should shift");
        AssertClose(
            12d,
            session.Score.ReferenceAudioSyncPoints[2].ScoreBeat,
            "later sync point should shift");
        AssertClose(
            3d,
            session.Score.ReferenceAudioSyncPoints[1].AudioSeconds,
            "audio time must not move when score measures are inserted");

        AssertTrue(
            session.Undo(),
            "measure insertion with reference sync should undo");
        AssertClose(
            4d,
            session.Score.ReferenceAudioSyncPoints[1].ScoreBeat,
            "sync point should return on undo");

        var meterSession = new ScoreEditSession(
            EditableMusicScore.FromMusicScore(
                CreateReferenceScore()));
        meterSession.Score.ReferenceAudioSyncPoints.AddRange(
            new[]
            {
                new ReferenceAudioSyncPoint(0d, 0d),
                new ReferenceAudioSyncPoint(4d, 3d),
                new ReferenceAudioSyncPoint(8d, 7d)
            });
        meterSession.SetMeasureTimeSignature(
            1,
            5,
            4);
        AssertClose(
            5d,
            meterSession.Score.ReferenceAudioSyncPoints[1].ScoreBeat,
            "meter expansion should shift following sync points");
        AssertClose(
            9d,
            meterSession.Score.ReferenceAudioSyncPoints[2].ScoreBeat,
            "meter expansion should shift later sync points");
        AssertClose(
            3d,
            meterSession.Score.ReferenceAudioSyncPoints[1].AudioSeconds,
            "meter edits must not move source audio time");

        session.DeleteMeasure(1);
        AssertEqual(
            2,
            session.Score.ReferenceAudioSyncPoints.Count,
            "sync points inside a deleted measure should be removed");
        AssertClose(
            0d,
            session.Score.ReferenceAudioSyncPoints[0].ScoreBeat,
            "following sync point should move to deletion seam");
        AssertClose(
            4d,
            session.Score.ReferenceAudioSyncPoints[1].ScoreBeat,
            "later sync point should move earlier after deletion");
    }

    private static void ReferenceAudioProjectSidecarRoundTrip()
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "PianoPracticeTool.Validation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var musicXmlPath = Path.Combine(
            tempDirectory,
            "reference.musicxml");
        var audioPath = Path.Combine(
            tempDirectory,
            "reference.wav");
        File.WriteAllText(
            musicXmlPath,
            "<score-partwise version=\"3.1\" />");
        File.WriteAllBytes(
            audioPath,
            Array.Empty<byte>());

        try
        {
            var project = new ReferenceAudioProject
            {
                Enabled = true,
                AudioPath = audioPath,
                PlaybackMode = EditorReferencePlaybackMode.Both,
                ReferenceVolumePercent = 73,
                ScoreVolumePercent = 61,
                SyncPoints = new List<ReferenceAudioSyncPoint>
                {
                    new(0d, 4.25d),
                    new(8d, 12.5d),
                    new(8d, 20d)
                }
            };
            EditorReferenceAudioProjectService.Save(
                musicXmlPath,
                project);
            var loaded =
                EditorReferenceAudioProjectService.Load(
                    musicXmlPath);

            AssertTrue(
                loaded.Enabled,
                "reference audio enabled flag");
            AssertEqual(
                EditorReferencePlaybackMode.Both,
                loaded.PlaybackMode,
                "reference playback mode");
            AssertEqual(
                73,
                loaded.ReferenceVolumePercent,
                "reference volume");
            AssertEqual(
                61,
                loaded.ScoreVolumePercent,
                "score comparison volume");
            AssertEqual(
                Path.GetFullPath(audioPath),
                Path.GetFullPath(loaded.AudioPath),
                "reference audio path");
            AssertEqual(
                3,
                loaded.SyncPoints.Count,
                "reference sync point count");
            AssertClose(
                20d,
                loaded.SyncPoints[2].AudioSeconds,
                "source-only gap endpoint");
        }
        finally
        {
            DeleteDirectoryBestEffort(
                tempDirectory);
        }
    }

    private static void SoundFontPresetSupportFollowsSf2BankRules()
    {
        AssertTrue(
            new SoundFontPresetInfo(0, 0, "Piano").IsSupported,
            "melodic bank 0 should be supported");
        AssertTrue(
            new SoundFontPresetInfo(127, 127, "Melodic").IsSupported,
            "melodic bank 127 should be supported");
        AssertTrue(
            new SoundFontPresetInfo(128, 0, "Percussion").IsSupported,
            "percussion bank 128 should be supported");
        AssertTrue(
            !new SoundFontPresetInfo(129, 0, "Invalid").IsSupported,
            "bank 129 should not be selectable");
        AssertTrue(
            !new SoundFontPresetInfo(0, 128, "Invalid").IsSupported,
            "program 128 should not be selectable");
    }

    private static void SoundFontReverbCreatesTail()
    {
        const int sampleRate = 44100;
        var samples = new float[sampleRate * 2];
        samples[0] = 1f;
        samples[1] = 1f;

        var processor = new StereoReverbProcessor(sampleRate, 100);
        processor.Process(samples);

        var tailEnergy = samples
            .Skip(sampleRate / 10 * 2)
            .Sum(sample => Math.Abs(sample));

        AssertTrue(tailEnergy > 0.01d, "reverb should create a measurable tail");

        var drySamples = new float[4096];
        drySamples[0] = 0.5f;
        drySamples[1] = 0.5f;
        var dryProcessor = new StereoReverbProcessor(sampleRate, 0);
        dryProcessor.Process(drySamples);

        AssertClose(0.5d, drySamples[0], "dry left sample");
        AssertClose(0.5d, drySamples[1], "dry right sample");
        AssertTrue(
            drySamples.Skip(2).All(sample => Math.Abs(sample) < 0.000001f),
            "zero reverb should not add a tail");
    }

    private static void MidiOutputSelectionIsDeterministic()
    {
        var devices = new[]
        {
            MidiOutputDevice.BuiltInSoundFont,
            new MidiOutputDevice(0, "Microsoft GS Wavetable Synth"),
            new MidiOutputDevice(1, "Digital Piano MIDI OUT")
        };

        var preferred = MidiOutputSelector.Select(
            devices,
            "Digital Piano MIDI OUT");
        AssertEqual("Digital Piano MIDI OUT", preferred?.Name, "preferred MIDI output");

        var builtInDefault = MidiOutputSelector.Select(devices, null);
        AssertEqual(
            MidiOutputDevice.BuiltInSoundFont.Name,
            builtInDefault?.Name,
            "built-in MIDI output");

        var missingPreferred = MidiOutputSelector.Select(
            devices,
            "Missing MIDI OUT");
        AssertEqual(
            MidiOutputDevice.BuiltInSoundFont.Name,
            missingPreferred?.Name,
            "missing preferred MIDI output falls back to built-in");
    }

    private static ScoreNote CreateValidationNote(
        int id,
        int midiNote,
        double startBeat,
        double durationBeat)
        => new()
        {
            Id = id,
            MidiNote = midiNote,
            StartBeat = startBeat,
            DurationBeat = durationBeat,
            Staff = 1,
            Voice = "1",
            Hand = Hand.Right
        };

    private static MusicScore CreateReferenceScore()
        => new()
        {
            Title = "Validation Score",
            Composer = "Validation Composer",
            TempoBpm = 120d,
            Notes = new[]
            {
                new ScoreNote
                {
                    Id = 0,
                    MidiNote = 60,
                    StartBeat = 0d,
                    DurationBeat = 1d,
                    Staff = 1,
                    Voice = "1",
                    Hand = Hand.Right,
                    Finger = 1
                },
                new ScoreNote
                {
                    Id = 3,
                    MidiNote = 64,
                    StartBeat = 0d,
                    DurationBeat = 1d,
                    Staff = 1,
                    Voice = "1",
                    Hand = Hand.Right,
                    Finger = 3
                },
                new ScoreNote
                {
                    Id = 1,
                    MidiNote = 67,
                    StartBeat = 3.5d,
                    DurationBeat = 1d,
                    Staff = 1,
                    Voice = "1",
                    Hand = Hand.Right,
                    Finger = 4
                },
                new ScoreNote
                {
                    Id = 2,
                    MidiNote = 48,
                    StartBeat = 5d,
                    DurationBeat = 1d / 3d,
                    Staff = 2,
                    Voice = "2",
                    Hand = Hand.Left,
                    Finger = 2
                }
            },
            Rests = new[]
            {
                new ScoreRest(1d, 1d, 1, "2"),
                new ScoreRest(6d, 0.5d, 2, "3")
            },
            TempoEvents = new[]
            {
                new ScoreTempoEvent(0d, 120d),
                new ScoreTempoEvent(4d, 90d)
            },
            KeySignatureEvents = new[]
            {
                new ScoreKeySignatureEvent(0d, 1, "major"),
                new ScoreKeySignatureEvent(2d, -1, "major"),
                new ScoreKeySignatureEvent(4d, 0, "major")
            },
            HarmonyEvents = new[]
            {
                new ScoreHarmonyEvent(0d, "G"),
                new ScoreHarmonyEvent(4d, "C")
            },
            Measures = new[]
            {
                new ScoreMeasure(1, 0d, 4d, 4, 4),
                new ScoreMeasure(2, 4d, 4d, 4, 4)
            }
        };

    private static void AssertRestVoicesEqual(
        IReadOnlyList<ScoreRest> expected,
        IReadOnlyList<ScoreRest> actual,
        string message)
    {
        var expectedVoices = expected
            .OrderBy(rest => rest.StartBeat)
            .ThenBy(rest => rest.Staff)
            .ThenBy(rest => rest.DurationBeat)
            .Select(rest => rest.Voice)
            .ToArray();
        var actualVoices = actual
            .OrderBy(rest => rest.StartBeat)
            .ThenBy(rest => rest.Staff)
            .ThenBy(rest => rest.DurationBeat)
            .Select(rest => rest.Voice)
            .ToArray();

        AssertEqual(expectedVoices.Length, actualVoices.Length, $"{message} count");
        for (var index = 0; index < expectedVoices.Length; index++)
        {
            AssertEqual(expectedVoices[index], actualVoices[index], $"{message} #{index}");
        }
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string? DirectValue(XElement element, string localName)
        => element.Elements().FirstOrDefault(child => child.Name.LocalName == localName)?.Value;

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertClose(double expected, double actual, string message, double tolerance = 0.00001d)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException($"{message}: expected {expected:R}, actual {actual:R}");
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
        }
    }

    private sealed class SilentMidiSoundService : IMidiSoundService
    {
        public bool IsAvailable => true;

        public string? DeviceName => "Validation";

        public IReadOnlyList<MidiOutputDevice> GetDevices()
            => Array.Empty<MidiOutputDevice>();

        public void Initialize()
        {
        }

        public void Connect(MidiOutputDevice device)
        {
            ArgumentNullException.ThrowIfNull(device);
        }

        public void Disconnect()
        {
        }

        public void ConfigureBuiltInSoundFont(
            SoundFontConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
        }

        public void NoteOn(
            int midiNote,
            int velocity,
            int channel = 1)
        {
        }

        public void NoteOff(
            int midiNote,
            int velocity = 0,
            int channel = 1)
        {
        }

        public void ControlChange(
            int controller,
            int value,
            int channel = 1)
        {
        }

        public void ProgramChange(
            int program,
            int channel = 1)
        {
        }

        public void PlayClick(
            bool accent,
            int volumePercent)
        {
        }

        public void StopClicks()
        {
        }

        public void AllNotesOff()
        {
        }

        public void Dispose()
        {
        }
    }

}
