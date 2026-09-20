using System.Globalization;
using System.IO;
using PianoPracticeTool.Core;

namespace PianoPracticeTool.Services.Songs;

public sealed record SongCatalogEntry(
    string FilePath,
    MusicScore Score,
    KeyboardRecommendation Keyboard,
    SongDifficulty Difficulty)
{
    public ScoreConsistencyAnalysis Consistency { get; init; } =
        ScoreConsistencyAnalysis.Empty;

    public int AutomaticHandCorrectionCount { get; init; }

    public string Title => SongDifficultyDetector.IsDifficultyLabel(Score.Title)
        ? Path.GetFileNameWithoutExtension(FilePath)
        : Score.Title;

    public string FileName => Path.GetFileName(FilePath) ?? FilePath;

    public string DifficultyText => SongDifficultyDetector.ToDisplayName(Difficulty);

    public string TempoText => string.Format(CultureInfo.CurrentCulture, "{0:0.#} BPM", Score.TempoBpm);

    public string NoteCountText => string.Format(CultureInfo.CurrentCulture, "{0:N0} ノート", Score.Notes.Count);

    public string RangeText => $"{MidiPitch.ToName(Keyboard.LowestMidi)} – {MidiPitch.ToName(Keyboard.HighestMidi)}";

    public string ScaleText
    {
        get
        {
            var scale = MusicTheoryAnalyzer.GetScaleDisplayName(Score);
            return string.IsNullOrWhiteSpace(scale) ? string.Empty : $"キー {scale}";
        }
    }

    public string RecommendedKeyboardText => string.Format(
        CultureInfo.CurrentCulture,
        "推奨 {0}鍵",
        Keyboard.ComfortableStandardKeyCount);

    public string MinimumKeyboardText
    {
        get
        {
            if (!Keyboard.CanReduceWithOctaveShift)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "最小 {0}鍵",
                    Keyboard.MinimumStandardKeyCount);
            }

            return string.Format(
                CultureInfo.CurrentCulture,
                "最小 {0}鍵 ({1:+#;-#;0}半音シフト)",
                Keyboard.MinimumStandardKeyCount,
                Keyboard.SuggestedTransposeSemitones);
        }
    }

    public string DurationText
    {
        get
        {
            var timeline = new ScoreTimeline(Score);
            if (timeline.TotalSeconds <= 0d)
            {
                return "—";
            }

            var duration = TimeSpan.FromSeconds(timeline.TotalSeconds);
            return duration.TotalHours >= 1d
                ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }
    }

    public bool HasWideHandSpanWarning => Keyboard.HasWideHandSpanWarning;
}
