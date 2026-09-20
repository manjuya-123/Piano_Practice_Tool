using System.IO;
using PianoPracticeTool.Core;
using PianoPracticeTool.Services.Songs;

namespace PianoPracticeTool.Services.Editor;

public sealed record EditorScoreDocument(
    string FilePath,
    MusicScore Score,
    SongDifficulty Difficulty)
{
    public string FileName => Path.GetFileName(FilePath) ?? FilePath;
}

public static class EditorScoreLoader
{
    public static Task<EditorScoreDocument> LoadAsync(
        string path,
        IReadOnlyDictionary<string, SongDifficulty>? difficultyOverrides = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be null, empty, or whitespace.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var overrides = difficultyOverrides?.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Load(fullPath, overrides);
            },
            cancellationToken);
    }

    public static EditorScoreDocument Load(
        string path,
        IReadOnlyDictionary<string, SongDifficulty>? difficultyOverrides = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be null, empty, or whitespace.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var parsedScore = MusicXmlParser.Load(fullPath);
        var score = MusicXmlRestVoiceMetadata.EnrichScore(fullPath, parsedScore);
        var difficulty = SongDifficultyDetector.Detect(fullPath, difficultyOverrides);
        return new EditorScoreDocument(fullPath, score, difficulty);
    }
}
