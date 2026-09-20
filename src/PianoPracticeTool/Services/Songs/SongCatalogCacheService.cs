using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PianoPracticeTool.Core;
using PianoPracticeTool.Services;

namespace PianoPracticeTool.Services.Songs;

public sealed class SongCatalogCacheService
{
    public const string CacheFileName =
        "song-catalog-cache.json";

    private const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions =
        CreateJsonOptions();

    private readonly object _syncRoot = new();
    private readonly string _applicationDirectory;
    private readonly string _cachePath;
    private readonly Dictionary<string, SongCatalogCacheEntry> _entries;
    private bool _dirty;

    public SongCatalogCacheService(
        string? applicationDirectory = null)
    {
        _applicationDirectory =
            Path.GetFullPath(
                string.IsNullOrWhiteSpace(
                    applicationDirectory)
                    ? AppContext.BaseDirectory
                    : applicationDirectory);
        _cachePath =
            Path.Combine(
                _applicationDirectory,
                CacheFileName);
        _entries =
            LoadEntries(
                out var resetRequired);
        _dirty =
            resetRequired;
        PruneMissingFiles();
    }

    public string CachePath =>
        _cachePath;

    public static string ComputeSha256(
        string path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            throw new ArgumentException(
                "Path must not be null, empty, or whitespace.",
                nameof(path));
        }

        using var stream =
            new FileStream(
                Path.GetFullPath(
                    path),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
        return Convert.ToHexString(
            SHA256.HashData(
                stream));
    }

    public bool TryGet(
        string songPath,
        string musicXmlHash,
        string? fingeringMetadataHash,
        SongDifficulty? difficultyOverride,
        out SongCatalogEntry song)
    {
        ValidatePath(
            songPath,
            nameof(songPath));
        if (string.IsNullOrWhiteSpace(
                musicXmlHash))
        {
            throw new ArgumentException(
                "Hash must not be null, empty, or whitespace.",
                nameof(musicXmlHash));
        }

        var fullPath =
            Path.GetFullPath(
                songPath);
        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(
                    fullPath,
                    out var cached)
                || !string.Equals(
                    cached.MusicXmlHash,
                    musicXmlHash,
                    StringComparison.Ordinal)
                || !string.Equals(
                    cached.FingeringMetadataHash,
                    fingeringMetadataHash,
                    StringComparison.Ordinal)
                || cached.DifficultyOverride
                    != difficultyOverride
                || cached.Score is null
                || cached.Keyboard is null
                || cached.Consistency is null)
            {
                song =
                    null!;
                return false;
            }

            song =
                new SongCatalogEntry(
                    fullPath,
                    cached.Score,
                    cached.Keyboard,
                    cached.Difficulty)
                {
                    Consistency =
                        cached.Consistency,
                    AutomaticHandCorrectionCount =
                        cached.AutomaticHandCorrectionCount
                };
            return true;
        }
    }

    public void Store(
        string songPath,
        string musicXmlHash,
        string? fingeringMetadataHash,
        SongDifficulty? difficultyOverride,
        SongCatalogEntry song)
    {
        ValidatePath(
            songPath,
            nameof(songPath));
        if (string.IsNullOrWhiteSpace(
                musicXmlHash))
        {
            throw new ArgumentException(
                "Hash must not be null, empty, or whitespace.",
                nameof(musicXmlHash));
        }

        ArgumentNullException.ThrowIfNull(
            song);

        var fullPath =
            Path.GetFullPath(
                songPath);
        var cached =
            new SongCatalogCacheEntry
            {
                SongPath =
                    PortablePath.ToStored(
                        _applicationDirectory,
                        fullPath),
                MusicXmlHash =
                    musicXmlHash,
                FingeringMetadataHash =
                    fingeringMetadataHash,
                DifficultyOverride =
                    difficultyOverride,
                Score =
                    song.Score,
                Keyboard =
                    song.Keyboard,
                Difficulty =
                    song.Difficulty,
                Consistency =
                    song.Consistency,
                AutomaticHandCorrectionCount =
                    song.AutomaticHandCorrectionCount
            };

        lock (_syncRoot)
        {
            _entries[fullPath] =
                cached;
            _dirty =
                true;
        }
    }

    public int PruneMissingFiles()
    {
        lock (_syncRoot)
        {
            var missingPaths =
                _entries
                    .Keys
                    .Where(path =>
                        !File.Exists(
                            path))
                    .ToArray();
            foreach (var path in missingPaths)
            {
                _entries.Remove(
                    path);
            }

            if (missingPaths.Length > 0)
            {
                _dirty =
                    true;
            }

            return missingPaths.Length;
        }
    }

    public bool TrySave()
    {
        try
        {
            SaveIfChanged();
            return true;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or JsonException
                or NotSupportedException)
        {
            return false;
        }
    }

    public void SaveIfChanged()
    {
        lock (_syncRoot)
        {
            if (!_dirty)
            {
                return;
            }

            Directory.CreateDirectory(
                _applicationDirectory);
            var document =
                new SongCatalogCacheDocument
                {
                    Version =
                        CurrentFormatVersion,
                    BuildId =
                        GetCurrentBuildId(),
                    Entries =
                        _entries
                            .OrderBy(item =>
                                item.Key,
                                StringComparer.OrdinalIgnoreCase)
                            .Select(item =>
                                item.Value)
                            .ToList()
                };
            var json =
                JsonSerializer.Serialize(
                    document,
                    JsonOptions);
            var temporaryPath =
                _cachePath
                + $".{Guid.NewGuid():N}.tmp";

            try
            {
                File.WriteAllText(
                    temporaryPath,
                    json);
                File.Move(
                    temporaryPath,
                    _cachePath,
                    overwrite: true);
                _dirty =
                    false;
            }
            finally
            {
                if (File.Exists(
                        temporaryPath))
                {
                    File.Delete(
                        temporaryPath);
                }
            }
        }
    }

    private Dictionary<string, SongCatalogCacheEntry> LoadEntries(
        out bool resetRequired)
    {
        resetRequired =
            false;
        if (!File.Exists(
                _cachePath))
        {
            return new Dictionary<string, SongCatalogCacheEntry>(
                StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json =
                File.ReadAllText(
                    _cachePath);
            var document =
                JsonSerializer.Deserialize<SongCatalogCacheDocument>(
                    json,
                    JsonOptions);
            if (document is null
                || document.Version
                    != CurrentFormatVersion
                || !string.Equals(
                    document.BuildId,
                    GetCurrentBuildId(),
                    StringComparison.Ordinal))
            {
                resetRequired =
                    true;
                return new Dictionary<string, SongCatalogCacheEntry>(
                    StringComparer.OrdinalIgnoreCase);
            }

            var entries =
                new Dictionary<string, SongCatalogCacheEntry>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (var cached in document.Entries)
            {
                if (string.IsNullOrWhiteSpace(
                        cached.SongPath)
                    || string.IsNullOrWhiteSpace(
                        cached.MusicXmlHash)
                    || cached.Score is null
                    || cached.Keyboard is null
                    || cached.Consistency is null)
                {
                    resetRequired =
                        true;
                    continue;
                }

                string fullPath;
                try
                {
                    fullPath =
                        PortablePath.Resolve(
                            _applicationDirectory,
                            cached.SongPath);
                    cached.SongPath =
                        PortablePath.ToStored(
                            _applicationDirectory,
                            fullPath);
                }
                catch (ArgumentException)
                {
                    resetRequired =
                        true;
                    continue;
                }

                entries[fullPath] =
                    cached;
            }

            return entries;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or JsonException
                or ArgumentException
                or NotSupportedException)
        {
            resetRequired =
                true;
            return new Dictionary<string, SongCatalogCacheEntry>(
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options =
            new JsonSerializerOptions
            {
                WriteIndented =
                    false,
                PropertyNameCaseInsensitive =
                    true
            };
        options.Converters.Add(
            new JsonStringEnumConverter());
        return options;
    }

    private static string GetCurrentBuildId()
        => typeof(SongCatalogCacheService)
            .Assembly
            .ManifestModule
            .ModuleVersionId
            .ToString(
                "N");

    private static void ValidatePath(
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

    private sealed class SongCatalogCacheDocument
    {
        public SongCatalogCacheDocument()
        {
        }

        public int Version { get; set; }

        public string BuildId { get; set; } =
            string.Empty;

        public List<SongCatalogCacheEntry> Entries { get; set; } =
            new();
    }

    private sealed class SongCatalogCacheEntry
    {
        public SongCatalogCacheEntry()
        {
        }

        public string SongPath { get; set; } =
            string.Empty;

        public string MusicXmlHash { get; set; } =
            string.Empty;

        public string? FingeringMetadataHash { get; set; }

        public SongDifficulty? DifficultyOverride { get; set; }

        public MusicScore? Score { get; set; }

        public KeyboardRecommendation? Keyboard { get; set; }

        public SongDifficulty Difficulty { get; set; } =
            SongDifficulty.Unknown;

        public ScoreConsistencyAnalysis? Consistency { get; set; }

        public int AutomaticHandCorrectionCount { get; set; }
    }
}
