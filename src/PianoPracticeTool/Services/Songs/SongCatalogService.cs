using System.IO;
using System.Text.Json;
using System.Xml;
using PianoPracticeTool.Core;

namespace PianoPracticeTool.Services.Songs;

public static class SongCatalogService
{
    private static readonly string[] SupportedExtensions =
    {
        ".xml",
        ".musicxml"
    };

    private static readonly SongCatalogCacheService CatalogCache =
        new();

    public static SongCatalogResult LoadDirectory(
        string directoryPath,
        IReadOnlyDictionary<string, SongDifficulty>? difficultyOverrides = null)
    {
        ValidatePathArgument(
            directoryPath,
            nameof(directoryPath));
        return LoadDirectories(
            new[]
            {
                directoryPath
            },
            difficultyOverrides);
    }

    public static Task<SongCatalogResult> LoadDirectoriesAsync(
        IEnumerable<string> directoryPaths,
        IReadOnlyDictionary<string, SongDifficulty>? difficultyOverrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            directoryPaths);
        var paths =
            directoryPaths.ToArray();
        var overrides =
            CopyOverrides(
                difficultyOverrides);
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return LoadDirectories(
                    paths,
                    overrides,
                    cancellationToken);
            },
            cancellationToken);
    }

    public static SongCatalogResult LoadDirectories(
        IEnumerable<string> directoryPaths,
        IReadOnlyDictionary<string, SongDifficulty>? difficultyOverrides = null)
        => LoadDirectories(
            directoryPaths,
            difficultyOverrides,
            CancellationToken.None);

    public static Task<SongCatalogEntry> LoadSongAsync(
        string path,
        IReadOnlyDictionary<string, SongDifficulty>? difficultyOverrides = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePathArgument(
            path,
            nameof(path));
        var fullPath =
            Path.GetFullPath(
                path);
        var overrides =
            CopyOverrides(
                difficultyOverrides);
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                CatalogCache.PruneMissingFiles();
                var song =
                    LoadSongCore(
                        fullPath,
                        overrides);
                CatalogCache.TrySave();
                return song;
            },
            cancellationToken);
    }

    public static SongCatalogEntry LoadSong(
        string path,
        IReadOnlyDictionary<string, SongDifficulty>? difficultyOverrides = null)
    {
        ValidatePathArgument(
            path,
            nameof(path));

        CatalogCache.PruneMissingFiles();
        var song =
            LoadSongCore(
                Path.GetFullPath(
                    path),
                difficultyOverrides);
        CatalogCache.TrySave();
        return song;
    }

    private static SongCatalogEntry LoadSongCore(
        string path,
        IReadOnlyDictionary<string, SongDifficulty>? difficultyOverrides)
    {
        var fullPath =
            Path.GetFullPath(
                path);
        var musicXmlHash =
            SongCatalogCacheService
                .ComputeSha256(
                    fullPath);
        var fingeringPath =
            FingeringMetadataService
                .GetMetadataPath(
                    fullPath);
        var fingeringMetadataHash =
            File.Exists(
                fingeringPath)
                ? SongCatalogCacheService
                    .ComputeSha256(
                        fingeringPath)
                : null;
        var difficultyOverride =
            GetDifficultyOverride(
                fullPath,
                difficultyOverrides);

        if (CatalogCache.TryGet(
                fullPath,
                musicXmlHash,
                fingeringMetadataHash,
                difficultyOverride,
                out var cachedSong))
        {
            return cachedSong;
        }

        var score =
            MusicXmlParser.Load(
                fullPath);
        var automaticHandCorrectionCount =
            PracticeScorePreparation
                .ApplyAutomaticHandCorrections(
                    score);
        FingeringGenerator.Generate(
            score);
        FingeringMetadataService.Apply(
            fullPath,
            score);
        var consistency =
            PracticeScorePreparation
                .AnalyzeConsistency(
                    score);
        var keyboard =
            KeyboardRangeAnalyzer.Analyze(
                score);
        var difficulty =
            SongDifficultyDetector.Detect(
                fullPath,
                difficultyOverrides);
        var song =
            new SongCatalogEntry(
                fullPath,
                score,
                keyboard,
                difficulty)
            {
                Consistency =
                    consistency,
                AutomaticHandCorrectionCount =
                    automaticHandCorrectionCount
            };

        CatalogCache.Store(
            fullPath,
            musicXmlHash,
            fingeringMetadataHash,
            difficultyOverride,
            song);
        return song;
    }

    private static SongDifficulty? GetDifficultyOverride(
        string fullPath,
        IReadOnlyDictionary<string, SongDifficulty>? difficultyOverrides)
    {
        if (difficultyOverrides is not null
            && difficultyOverrides.TryGetValue(
                fullPath,
                out var overridden))
        {
            return overridden;
        }

        return null;
    }

    private static IReadOnlyDictionary<string, SongDifficulty>? CopyOverrides(
        IReadOnlyDictionary<string, SongDifficulty>? difficultyOverrides)
        => difficultyOverrides?.ToDictionary(
            item =>
                item.Key,
            item =>
                item.Value,
            StringComparer.OrdinalIgnoreCase);

    private static SongCatalogResult LoadDirectories(
        IEnumerable<string> directoryPaths,
        IReadOnlyDictionary<string, SongDifficulty>? difficultyOverrides,
        CancellationToken cancellationToken)
    {
        var songs =
            new List<SongCatalogEntry>();
        var errors =
            new List<string>();
        var discoveredPaths =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        CatalogCache.PruneMissingFiles();
        try
        {
            foreach (var directoryPath in directoryPaths
                         .Where(path =>
                             !string.IsNullOrWhiteSpace(
                                 path))
                         .Select(
                             Path.GetFullPath)
                         .Distinct(
                             StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(
                        directoryPath))
                {
                    errors.Add(
                        $"フォルダが見つかりません: {directoryPath}");
                    continue;
                }

                string[] paths;
                try
                {
                    paths =
                        Directory
                            .EnumerateFiles(
                                directoryPath,
                                "*",
                                SearchOption.AllDirectories)
                            .Where(
                                IsSupportedMusicXml)
                            .OrderBy(
                                path =>
                                    path,
                                StringComparer.CurrentCultureIgnoreCase)
                            .ToArray();
                }
                catch (Exception ex) when (
                    ex is IOException
                        or UnauthorizedAccessException)
                {
                    errors.Add(
                        $"{directoryPath}: {ex.Message}");
                    continue;
                }

                foreach (var path in paths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fullPath =
                        Path.GetFullPath(
                            path);
                    if (!discoveredPaths.Add(
                            fullPath))
                    {
                        continue;
                    }

                    try
                    {
                        songs.Add(
                            LoadSongCore(
                                fullPath,
                                difficultyOverrides));
                    }
                    catch (Exception ex) when (
                        ex is IOException
                            or UnauthorizedAccessException
                            or XmlException
                            or JsonException)
                    {
                        errors.Add(
                            $"{Path.GetFileName(path)}: {ex.Message}");
                    }
                }
            }
        }
        finally
        {
            CatalogCache.TrySave();
        }

        return new SongCatalogResult(
            songs
                .OrderBy(song =>
                    song.Difficulty)
                .ThenBy(
                    song =>
                        song.Title,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            errors);
    }

    private static bool IsSupportedMusicXml(
        string path)
    {
        var extension =
            Path.GetExtension(
                path);
        return SupportedExtensions.Contains(
            extension,
            StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidatePathArgument(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new ArgumentException(
                "Path must not be null, empty, or whitespace.",
                parameterName);
        }
    }
}

public sealed record SongCatalogResult(
    IReadOnlyList<SongCatalogEntry> Songs,
    IReadOnlyList<string> Errors);
