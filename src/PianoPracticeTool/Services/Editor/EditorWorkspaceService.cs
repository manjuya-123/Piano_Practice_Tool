using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PianoPracticeTool.Core;
using PianoPracticeTool.Core.Editing;
using PianoPracticeTool.Services.Songs;

namespace PianoPracticeTool.Services.Editor;

public sealed class EditorWorkspaceViewState
{
    public double CursorBeat { get; set; }

    public long GridTicks { get; set; } = 240L;

    public Hand InsertHand { get; set; } =
        Hand.Right;

    public List<int> SelectedNoteIds { get; set; } =
        new();

    public double VisibleBeats { get; set; } = 16d;

    public int VisiblePitchLowestMidi { get; set; } = 21;

    public int VisiblePitchHighestMidi { get; set; } = 108;

    public bool ScaleGuideEnabled { get; set; }

    public double ReferenceAudioCurrentSeconds { get; set; }
}

public sealed class EditorWorkspaceDocument
{
    public int Version { get; set; } =
        EditorWorkspaceService.CurrentVersion;

    public DateTimeOffset SavedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset SourceMusicXmlLastWriteUtc { get; set; }

    public long SourceMusicXmlLength { get; set; }

    public required MusicScore Score { get; init; }

    public SongDifficulty Difficulty { get; init; }

    public ReferenceAudioProject ReferenceAudioProject { get; init; } =
        new();

    public EditorWorkspaceViewState ViewState { get; init; } =
        new();
}

public static class EditorWorkspaceService
{
    public const int CurrentVersion = 1;

    private const string SidecarSuffix =
        ".pianopractice.editor.json";

    private static readonly JsonSerializerOptions JsonOptions =
        CreateJsonOptions();

    public static string GetWorkspacePath(
        string musicXmlPath)
    {
        if (string.IsNullOrWhiteSpace(
                musicXmlPath))
        {
            throw new ArgumentException(
                "MusicXML path is required.",
                nameof(musicXmlPath));
        }

        return Path.GetFullPath(
                musicXmlPath)
            + SidecarSuffix;
    }

    public static bool Exists(
        string musicXmlPath)
        => File.Exists(
            GetWorkspacePath(
                musicXmlPath));

    public static EditorWorkspaceDocument Load(
        string musicXmlPath)
    {
        var workspacePath =
            GetWorkspacePath(
                musicXmlPath);
        if (!File.Exists(
                workspacePath))
        {
            throw new FileNotFoundException(
                "編集途中の作業データが見つかりません。",
                workspacePath);
        }

        try
        {
            var json =
                File.ReadAllText(
                    workspacePath);
            var document =
                JsonSerializer.Deserialize<EditorWorkspaceDocument>(
                    json,
                    JsonOptions)
                ?? throw new InvalidDataException(
                    "編集途中の作業データが空です。");

            if (document.Version
                != CurrentVersion)
            {
                throw new InvalidDataException(
                    $"対応していない作業データ形式です。Version={document.Version}");
            }

            ArgumentNullException.ThrowIfNull(
                document.Score);
            _ =
                EditableMusicScore.FromMusicScore(
                    document.Score);

            document.ReferenceAudioProject.SyncPoints =
                ReferenceAudioSynchronizer
                    .Normalize(
                        document.ReferenceAudioProject.SyncPoints)
                    .ToList();
            if (!string.IsNullOrWhiteSpace(
                    document.ReferenceAudioProject.AudioPath))
            {
                document.ReferenceAudioProject.AudioPath =
                    ResolveStoredPath(
                        musicXmlPath,
                        document.ReferenceAudioProject.AudioPath);
            }

            document.ViewState.SelectedNoteIds ??=
                new List<int>();
            document.ViewState.GridTicks =
                Math.Max(
                    1L,
                    document.ViewState.GridTicks);
            document.ViewState.VisibleBeats =
                Math.Clamp(
                    document.ViewState.VisibleBeats,
                    4d,
                    64d);
            document.ViewState.VisiblePitchLowestMidi =
                Math.Clamp(
                    document.ViewState.VisiblePitchLowestMidi,
                    0,
                    127);
            document.ViewState.VisiblePitchHighestMidi =
                Math.Clamp(
                    document.ViewState.VisiblePitchHighestMidi,
                    document.ViewState.VisiblePitchLowestMidi,
                    127);

            return document;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or JsonException
                or ArgumentException
                or InvalidOperationException)
        {
            throw new InvalidDataException(
                "編集途中の作業データを読み込めませんでした。",
                ex);
        }
    }

    public static void Save(
        string musicXmlPath,
        MusicScore score,
        SongDifficulty difficulty,
        ReferenceAudioProject referenceAudioProject,
        EditorWorkspaceViewState viewState)
    {
        ArgumentNullException.ThrowIfNull(
            score);
        ArgumentNullException.ThrowIfNull(
            referenceAudioProject);
        ArgumentNullException.ThrowIfNull(
            viewState);

        var fullMusicXmlPath =
            Path.GetFullPath(
                musicXmlPath);
        var workspacePath =
            GetWorkspacePath(
                fullMusicXmlPath);
        var directory =
            Path.GetDirectoryName(
                workspacePath)
            ?? throw new InvalidOperationException(
                "作業データの保存先フォルダを特定できません。");
        Directory.CreateDirectory(
            directory);

        var sourceInfo =
            new FileInfo(
                fullMusicXmlPath);
        var storedReference =
            referenceAudioProject.Clone();
        if (!string.IsNullOrWhiteSpace(
                storedReference.AudioPath))
        {
            storedReference.AudioPath =
                MakeStoredPath(
                    fullMusicXmlPath,
                    storedReference.AudioPath);
        }

        var document =
            new EditorWorkspaceDocument
            {
                Version = CurrentVersion,
                SavedAtUtc = DateTimeOffset.UtcNow,
                SourceMusicXmlLastWriteUtc =
                    sourceInfo.Exists
                        ? sourceInfo.LastWriteTimeUtc
                        : default,
                SourceMusicXmlLength =
                    sourceInfo.Exists
                        ? sourceInfo.Length
                        : 0L,
                Score = score,
                Difficulty = difficulty,
                ReferenceAudioProject =
                    storedReference,
                ViewState = viewState
            };
        var json =
            JsonSerializer.Serialize(
                document,
                JsonOptions);
        var temporaryPath =
            workspacePath
            + $".{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllText(
                temporaryPath,
                json);
            File.Move(
                temporaryPath,
                workspacePath,
                overwrite: true);
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

    public static void Delete(
        string musicXmlPath)
    {
        var workspacePath =
            GetWorkspacePath(
                musicXmlPath);
        if (File.Exists(
                workspacePath))
        {
            File.Delete(
                workspacePath);
        }
    }

    public static bool SourceMusicXmlChanged(
        string musicXmlPath,
        EditorWorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(
            document);

        var info =
            new FileInfo(
                Path.GetFullPath(
                    musicXmlPath));
        if (!info.Exists)
        {
            return true;
        }

        return info.Length
                != document.SourceMusicXmlLength
            || info.LastWriteTimeUtc
                != document.SourceMusicXmlLastWriteUtc.UtcDateTime;
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

    private static string ResolveStoredPath(
        string musicXmlPath,
        string storedPath)
    {
        if (Path.IsPathRooted(
                storedPath))
        {
            return Path.GetFullPath(
                storedPath);
        }

        var directory =
            Path.GetDirectoryName(
                Path.GetFullPath(
                    musicXmlPath))
            ?? string.Empty;
        return Path.GetFullPath(
            Path.Combine(
                directory,
                storedPath));
    }

    private static string MakeStoredPath(
        string musicXmlPath,
        string path)
    {
        var directory =
            Path.GetDirectoryName(
                Path.GetFullPath(
                    musicXmlPath))
            ?? string.Empty;
        var fullPath =
            Path.GetFullPath(
                path);

        try
        {
            return Path.GetRelativePath(
                directory,
                fullPath);
        }
        catch (ArgumentException)
        {
            return fullPath;
        }
    }
}
