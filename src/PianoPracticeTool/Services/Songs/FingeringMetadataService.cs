using System.IO;
using System.Text.Json;
using PianoPracticeTool.Core;

namespace PianoPracticeTool.Services.Songs;

public static class FingeringMetadataService
{
    private const string MetadataSuffix = ".pianopractice.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static void Apply(string scorePath, MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        ValidatePathArgument(scorePath);

        var metadataPath = GetMetadataPath(scorePath);
        if (!File.Exists(metadataPath))
        {
            return;
        }

        using var stream = File.OpenRead(metadataPath);
        var metadata = JsonSerializer.Deserialize<FingeringMetadata>(stream, SerializerOptions);
        if (metadata?.Notes is null)
        {
            return;
        }

        var notesById = score.Notes.ToDictionary(note => note.Id);
        foreach (var entry in metadata.Notes)
        {
            if (entry.Finger is < 1 or > 5
                || !notesById.TryGetValue(entry.NoteId, out var note)
                || note.MidiNote != entry.MidiNote
                || Math.Abs(note.StartBeat - entry.StartBeat) > ScoreTiming.BeatGroupingTolerance)
            {
                continue;
            }

            note.Finger = entry.Finger;
        }
    }

    public static void Save(string scorePath, MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        ValidatePathArgument(scorePath);

        var metadata = new FingeringMetadata(
            score.Notes
                .Where(note => note.Finger is >= 1 and <= 5)
                .Select(note => new FingeringEntry(
                    note.Id,
                    note.MidiNote,
                    note.StartBeat,
                    note.Finger))
                .ToArray());

        var metadataPath = GetMetadataPath(scorePath);
        using var stream = File.Create(metadataPath);
        JsonSerializer.Serialize(stream, metadata, SerializerOptions);
    }

    public static string GetMetadataPath(string scorePath)
    {
        ValidatePathArgument(scorePath);
        return scorePath + MetadataSuffix;
    }

    private static void ValidatePathArgument(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be null, empty, or whitespace.", nameof(path));
        }
    }

    private sealed record FingeringMetadata(IReadOnlyList<FingeringEntry> Notes);

    private sealed record FingeringEntry(
        int NoteId,
        int MidiNote,
        double StartBeat,
        int Finger);
}
