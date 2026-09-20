using System.IO;
using System.Xml.Linq;

namespace PianoPracticeTool.Services.Songs;

public enum SongDifficulty
{
    Introductory,
    Beginner,
    Intermediate,
    Advanced,
    Unknown
}

public static class SongDifficultyDetector
{
    public static SongDifficulty Detect(
        string filePath,
        IReadOnlyDictionary<string, SongDifficulty>? overrides = null)
    {
        if (TryReadToolDifficulty(filePath, out var fromToolMetadata))
        {
            return fromToolMetadata;
        }

        if (overrides is not null
            && overrides.TryGetValue(Path.GetFullPath(filePath), out var overridden))
        {
            return overridden;
        }

        var movementTitle = TryReadMovementTitle(filePath);
        var fromMetadata = Parse(movementTitle);
        if (fromMetadata != SongDifficulty.Unknown)
        {
            return fromMetadata;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var fromFolder = Parse(Path.GetFileName(directory));
            if (fromFolder != SongDifficulty.Unknown)
            {
                return fromFolder;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return SongDifficulty.Unknown;
    }

    public static SongDifficulty Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SongDifficulty.Unknown;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains("入門", StringComparison.Ordinal)
            || normalized.Contains("introduct", StringComparison.Ordinal)
            || normalized.Contains("starter", StringComparison.Ordinal))
        {
            return SongDifficulty.Introductory;
        }

        if (normalized.Contains("初級", StringComparison.Ordinal)
            || normalized.Contains("beginner", StringComparison.Ordinal)
            || normalized.Contains("elementary", StringComparison.Ordinal))
        {
            return SongDifficulty.Beginner;
        }

        if (normalized.Contains("中級", StringComparison.Ordinal)
            || normalized.Contains("intermediate", StringComparison.Ordinal))
        {
            return SongDifficulty.Intermediate;
        }

        if (normalized.Contains("上級", StringComparison.Ordinal)
            || normalized.Contains("advanced", StringComparison.Ordinal))
        {
            return SongDifficulty.Advanced;
        }

        return SongDifficulty.Unknown;
    }

    public static string ToDisplayName(SongDifficulty difficulty)
    {
        return difficulty switch
        {
            SongDifficulty.Introductory => "入門",
            SongDifficulty.Beginner => "初級",
            SongDifficulty.Intermediate => "中級",
            SongDifficulty.Advanced => "上級",
            _ => "未分類"
        };
    }

    public static bool IsDifficultyLabel(string? value)
        => Parse(value) != SongDifficulty.Unknown;

    private static bool TryReadToolDifficulty(
        string filePath,
        out SongDifficulty difficulty)
    {
        difficulty = SongDifficulty.Unknown;
        try
        {
            using var stream = File.OpenRead(filePath);
            var document = XDocument.Load(stream, LoadOptions.None);
            var value = document
                .Descendants()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "miscellaneous-field"
                    && string.Equals(
                        element.Attribute("name")?.Value,
                        MusicXmlWriter.DifficultyFieldName,
                        StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?.Trim()
                .ToLowerInvariant();

            difficulty = value switch
            {
                "introductory" => SongDifficulty.Introductory,
                "beginner" => SongDifficulty.Beginner,
                "intermediate" => SongDifficulty.Intermediate,
                "advanced" => SongDifficulty.Advanced,
                "unknown" => SongDifficulty.Unknown,
                _ => SongDifficulty.Unknown
            };

            return value is "introductory" or "beginner" or "intermediate" or "advanced" or "unknown";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }
    }

    private static string? TryReadMovementTitle(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var document = XDocument.Load(stream, LoadOptions.None);
            return document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "movement-title")
                ?.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }
}
