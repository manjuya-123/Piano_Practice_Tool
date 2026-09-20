using PianoPracticeTool.Core;

namespace PianoPracticeTool.Services.Songs;

public sealed record SongMetadataUpdate(
    string Title,
    string Composer,
    SongDifficulty Difficulty,
    bool UpdateScale,
    int ScaleFifths,
    string ScaleId);

public static class SongMetadataService
{
    public static void Save(
        SongCatalogEntry song,
        SongMetadataUpdate update)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentNullException.ThrowIfNull(update);

        var title = update.Title.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException(
                "曲名を入力してください。",
                nameof(update));
        }

        var score = CreateUpdatedScore(
            song.Score,
            update with
            {
                Title = title,
                Composer = update.Composer.Trim()
            });

        MusicXmlWriter.SaveValidated(
            song.FilePath,
            score,
            update.Difficulty);
    }

    public static MusicScore CreateUpdatedScore(
        MusicScore score,
        SongMetadataUpdate update)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(update);

        var keyEvents = score.KeySignatureEvents;
        if (update.UpdateScale)
        {
            var definition =
                MusicTheoryAnalyzer.GetScaleDefinition(
                    update.ScaleId)
                ?? throw new ArgumentException(
                    "Scale種類を解釈できません。",
                    nameof(update));

            if (update.ScaleFifths is < -7 or > 7)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(update),
                    "調号は -7 から 7 の範囲で指定してください。");
            }

            keyEvents = score.KeySignatureEvents
                .Where(item =>
                    Math.Abs(item.Beat)
                        > ScoreTiming.EventBeatTolerance)
                .Append(new ScoreKeySignatureEvent(
                    0d,
                    update.ScaleFifths,
                    definition.MusicXmlMode,
                    definition.IsDiatonic
                        ? null
                        : definition.Id))
                .OrderBy(item => item.Beat)
                .ToArray();
        }

        return new MusicScore
        {
            Title = update.Title.Trim(),
            Composer = update.Composer.Trim(),
            TempoBpm = score.TempoBpm,
            Notes = score.Notes,
            Rests = score.Rests,
            TempoEvents = score.TempoEvents,
            KeySignatureEvents = keyEvents,
            HarmonyEvents = score.HarmonyEvents,
            Measures = score.Measures
        };
    }
}
