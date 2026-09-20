using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PianoPracticeTool.Core.Editing;

namespace PianoPracticeTool.Services.Editor;

public static class EditorReferenceAudioProjectService
{
    private const string SidecarSuffix = ".pianopractice.audio.json";

    private static readonly JsonSerializerOptions JsonOptions =
        CreateJsonOptions();

    public static string GetSidecarPath(string musicXmlPath)
    {
        if (string.IsNullOrWhiteSpace(musicXmlPath))
        {
            throw new ArgumentException(
                "MusicXML path is required.",
                nameof(musicXmlPath));
        }

        return Path.GetFullPath(musicXmlPath)
            + SidecarSuffix;
    }

    public static ReferenceAudioProject Load(string musicXmlPath)
    {
        var sidecarPath = GetSidecarPath(musicXmlPath);
        if (!File.Exists(sidecarPath))
        {
            return new ReferenceAudioProject();
        }

        try
        {
            var json = File.ReadAllText(sidecarPath);
            var project = JsonSerializer.Deserialize<ReferenceAudioProject>(
                json,
                JsonOptions)
                ?? new ReferenceAudioProject();

            project.ReferenceVolumePercent = Math.Clamp(
                project.ReferenceVolumePercent,
                0,
                100);
            project.ScoreVolumePercent = Math.Clamp(
                project.ScoreVolumePercent,
                0,
                100);
            project.ReferencePanPercent = Math.Clamp(
                project.ReferencePanPercent,
                -100,
                100);
            project.ScorePanPercent = Math.Clamp(
                project.ScorePanPercent,
                -100,
                100);
            project.SyncPoints = ReferenceAudioSynchronizer.Normalize(
                    project.SyncPoints)
                .ToList();

            if (!string.IsNullOrWhiteSpace(project.AudioPath))
            {
                project.AudioPath = ResolveAudioPath(
                    musicXmlPath,
                    project.AudioPath);
            }

            return project;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidOperationException)
        {
            throw new InvalidDataException(
                "耳コピ用の元音源設定を読み込めませんでした。",
                ex);
        }
    }

    public static void Save(
        string musicXmlPath,
        ReferenceAudioProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var sidecarPath = GetSidecarPath(musicXmlPath);
        if (string.IsNullOrWhiteSpace(project.AudioPath)
            && project.SyncPoints.Count == 0)
        {
            if (File.Exists(sidecarPath))
            {
                File.Delete(sidecarPath);
            }

            return;
        }

        var directory = Path.GetDirectoryName(sidecarPath)
            ?? throw new InvalidOperationException(
                "元音源設定の保存先を確認できません。");
        Directory.CreateDirectory(directory);

        var serializable = project.Clone();
        serializable.ReferenceVolumePercent = Math.Clamp(
            serializable.ReferenceVolumePercent,
            0,
            100);
        serializable.ScoreVolumePercent = Math.Clamp(
            serializable.ScoreVolumePercent,
            0,
            100);
        serializable.ReferencePanPercent = Math.Clamp(
            serializable.ReferencePanPercent,
            -100,
            100);
        serializable.ScorePanPercent = Math.Clamp(
            serializable.ScorePanPercent,
            -100,
            100);
        serializable.SyncPoints = ReferenceAudioSynchronizer.Normalize(
                serializable.SyncPoints)
            .ToList();

        if (!string.IsNullOrWhiteSpace(serializable.AudioPath))
        {
            serializable.AudioPath = MakeStoredAudioPath(
                musicXmlPath,
                serializable.AudioPath);
        }

        var json = JsonSerializer.Serialize(
            serializable,
            JsonOptions);
        var temporaryPath =
            sidecarPath
            + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                json);
            File.Move(
                temporaryPath,
                sidecarPath,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static void CopyForDuplicate(
        string sourceMusicXmlPath,
        string destinationMusicXmlPath)
    {
        var sourceSidecar =
            GetSidecarPath(sourceMusicXmlPath);
        if (!File.Exists(sourceSidecar))
        {
            return;
        }

        var project = Load(sourceMusicXmlPath);
        Save(
            destinationMusicXmlPath,
            project);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options =
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
        options.Converters.Add(
            new JsonStringEnumConverter());
        return options;
    }

    private static string ResolveAudioPath(
        string musicXmlPath,
        string storedPath)
    {
        if (Path.IsPathRooted(storedPath))
        {
            return Path.GetFullPath(storedPath);
        }

        var directory = Path.GetDirectoryName(
            Path.GetFullPath(musicXmlPath))
            ?? string.Empty;
        return Path.GetFullPath(
            Path.Combine(
                directory,
                storedPath));
    }

    private static string MakeStoredAudioPath(
        string musicXmlPath,
        string audioPath)
    {
        var xmlDirectory = Path.GetDirectoryName(
            Path.GetFullPath(musicXmlPath))
            ?? string.Empty;
        var fullAudioPath = Path.GetFullPath(audioPath);

        try
        {
            return Path.GetRelativePath(
                xmlDirectory,
                fullAudioPath);
        }
        catch (ArgumentException)
        {
            return fullAudioPath;
        }
    }
}
