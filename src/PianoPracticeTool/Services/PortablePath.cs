using System.IO;

namespace PianoPracticeTool.Services;

internal static class PortablePath
{
    private const string DefaultSongDirectoryName = "score_data";

    public static string Resolve(string applicationDirectory, string path)
    {
        ValidatePath(applicationDirectory, nameof(applicationDirectory));
        ValidatePath(path, nameof(path));

        var baseDirectory = Path.GetFullPath(applicationDirectory);
        if (!Path.IsPathRooted(path))
        {
            return Path.GetFullPath(Path.Combine(baseDirectory, path));
        }

        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            return fullPath;
        }

        var movedPath = TryResolveMovedScoreDataPath(baseDirectory, fullPath);
        return movedPath ?? fullPath;
    }

    public static string ToStored(string applicationDirectory, string path)
    {
        ValidatePath(applicationDirectory, nameof(applicationDirectory));
        ValidatePath(path, nameof(path));

        var baseDirectory = Path.GetFullPath(applicationDirectory);
        var fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(baseDirectory, path));
        var relativePath = Path.GetRelativePath(baseDirectory, fullPath);

        if (!Path.IsPathRooted(relativePath)
            && !string.Equals(relativePath, "..", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return relativePath;
        }

        return fullPath;
    }

    private static void ValidatePath(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Path must not be null, empty, or whitespace.",
                parameterName);
        }
    }

    private static string? TryResolveMovedScoreDataPath(
        string applicationDirectory,
        string oldAbsolutePath)
    {
        var parts = oldAbsolutePath
            .Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
        var scoreDataIndex = Array.FindLastIndex(
            parts,
            part => string.Equals(
                part,
                DefaultSongDirectoryName,
                StringComparison.OrdinalIgnoreCase));
        if (scoreDataIndex < 0)
        {
            return null;
        }

        var relativeParts = parts.Skip(scoreDataIndex).ToArray();
        var candidate = relativeParts.Aggregate(
            applicationDirectory,
            (current, part) => Path.Combine(current, part));

        if (relativeParts.Length == 1
            || File.Exists(candidate)
            || Directory.Exists(candidate))
        {
            return Path.GetFullPath(candidate);
        }

        return null;
    }
}
