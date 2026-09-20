using System.IO;
using System.Text.Json;
using PianoPracticeTool.Core;
using PianoPracticeTool.Services;

namespace PianoPracticeTool.Services.Practice;

public sealed record PracticeHistoryEntry(
    string SongPath,
    PracticeResult Result);

public sealed record PracticeModeBestScores(
    double? WaitForCorrectNotes,
    double? PlayAlong,
    double? OriginalTempo)
{
    public double? Get(PracticeMode mode)
        => mode switch
        {
            PracticeMode.WaitForCorrectNotes => WaitForCorrectNotes,
            PracticeMode.PlayAlong => PlayAlong,
            PracticeMode.OriginalTempo => OriginalTempo,
            _ => null
        };
}

public sealed class PracticeHistoryService
{
    private const string HistoryFileName = "practice-history.json";
    private const int MaximumStoredEntries = 5000;

    private readonly string _applicationDirectory;
    private readonly string _historyPath;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true
    };
    private readonly List<PracticeHistoryEntry> _entries;

    public PracticeHistoryService(string? applicationDirectory = null)
    {
        _applicationDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(applicationDirectory)
                ? AppContext.BaseDirectory
                : applicationDirectory);
        _historyPath = Path.Combine(_applicationDirectory, HistoryFileName);
        _entries = LoadEntries();
    }

    public IReadOnlyList<PracticeHistoryEntry> GetForSong(string songPath)
    {
        ValidateSongPath(songPath);
        var fullPath = PortablePath.Resolve(_applicationDirectory, songPath);
        return _entries
            .Where(entry => string.Equals(
                PortablePath.Resolve(_applicationDirectory, entry.SongPath),
                fullPath,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.Result.CompletedAtUtc)
            .ToArray();
    }

    public IReadOnlyDictionary<string, PracticeModeBestScores> GetBestHundredPointScoresBySong()
    {
        return _entries
            .Where(entry => entry.Result.Mode != PracticeMode.Listen)
            .Where(entry => entry.Result.AutoPlayedTargetNoteCount == 0)
            .GroupBy(
                entry => PortablePath.Resolve(_applicationDirectory, entry.SongPath),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new PracticeModeBestScores(
                    GetBestScore(group, PracticeMode.WaitForCorrectNotes),
                    GetBestScore(group, PracticeMode.PlayAlong),
                    GetBestScore(group, PracticeMode.OriginalTempo)),
                StringComparer.OrdinalIgnoreCase);
    }

    public void Append(string songPath, PracticeResult result)
    {
        ValidateSongPath(songPath);
        ArgumentNullException.ThrowIfNull(result);

        _entries.Add(new PracticeHistoryEntry(
            PortablePath.ToStored(_applicationDirectory, songPath),
            result));
        if (_entries.Count > MaximumStoredEntries)
        {
            _entries.RemoveRange(0, _entries.Count - MaximumStoredEntries);
        }

        Save();
    }

    private static double? GetBestScore(
        IEnumerable<PracticeHistoryEntry> entries,
        PracticeMode mode)
    {
        var scores = entries
            .Where(entry => entry.Result.Mode == mode)
            .Select(entry => PracticeScoring.ToHundredPointScore(entry.Result.Score))
            .ToArray();
        return scores.Length == 0 ? null : scores.Max();
    }

    private List<PracticeHistoryEntry> LoadEntries()
    {
        if (!File.Exists(_historyPath))
        {
            return new List<PracticeHistoryEntry>();
        }

        try
        {
            var json = File.ReadAllText(_historyPath);
            var entries = JsonSerializer.Deserialize<List<PracticeHistoryEntry>>(
                    json,
                    _serializerOptions)
                ?? new List<PracticeHistoryEntry>();

            return entries
                .Select(entry => entry with
                {
                    SongPath = PortablePath.ToStored(
                        _applicationDirectory,
                        PortablePath.Resolve(_applicationDirectory, entry.SongPath))
                })
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new List<PracticeHistoryEntry>();
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_historyPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(_entries, _serializerOptions);
        File.WriteAllText(_historyPath, json);
    }

    private static void ValidateSongPath(string songPath)
    {
        if (string.IsNullOrWhiteSpace(songPath))
        {
            throw new ArgumentException("Song path must not be null, empty, or whitespace.", nameof(songPath));
        }
    }
}
