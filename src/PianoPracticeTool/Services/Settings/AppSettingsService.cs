using System.IO;
using System.Text.Json;
using PianoPracticeTool.Services;
using PianoPracticeTool.Services.Midi;
using PianoPracticeTool.Services.Songs;

namespace PianoPracticeTool.Services.Settings;

public sealed class AppSettingsService
{
    private const string SettingsFileName = "piano-practice-settings.json";
    private const string DefaultSongDirectoryName = "score_data";
    private const string LegacyDefaultSongDirectoryName = "data";
    private const string DefaultSoundFontDirectoryName = "SoundFonts";
    private const string LegacyBuiltInPianoOutputName = "Built-in Piano Synthesizer";

    public const string DefaultSoundFontFileName = "UprightPianoKW-small-20190703.sf2";

    private readonly string _applicationDirectory;
    private readonly string _settingsPath;
    private readonly string _defaultSongDirectory;
    private readonly string _legacyDefaultSongDirectory;
    private readonly string _defaultSoundFontPath;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true
    };

    public AppSettingsService(string? applicationDirectory = null)
    {
        _applicationDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(applicationDirectory)
                ? AppContext.BaseDirectory
                : applicationDirectory);
        _settingsPath = Path.Combine(_applicationDirectory, SettingsFileName);
        _defaultSongDirectory = Path.Combine(_applicationDirectory, DefaultSongDirectoryName);
        _legacyDefaultSongDirectory = Path.Combine(
            _applicationDirectory,
            LegacyDefaultSongDirectoryName);
        _defaultSoundFontPath = Path.Combine(
            _applicationDirectory,
            DefaultSoundFontDirectoryName,
            DefaultSoundFontFileName);
    }

    public string SettingsPath => _settingsPath;

    public string DefaultSongDirectory => _defaultSongDirectory;

    public string DefaultSoundFontPath => _defaultSoundFontPath;

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            var defaults = CreateDefaultSettings();
            EnsureDefaultSongDirectory(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, _serializerOptions)
                ?? CreateDefaultSettings();
            ApplyLegacySettings(json, settings);
            Normalize(settings);
            EnsureDefaultSongDirectory(settings);
            return settings;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or JsonException
                or ArgumentException
                or NotSupportedException)
        {
            var defaults = CreateDefaultSettings();
            EnsureDefaultSongDirectory(defaults);
            return defaults;
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        EnsureDefaultSongDirectory(settings);

        Directory.CreateDirectory(_applicationDirectory);

        var storedSettings = CreateStoredSettings(settings);
        var json = JsonSerializer.Serialize(storedSettings, _serializerOptions);
        File.WriteAllText(_settingsPath, json);
    }

    private AppSettings CreateDefaultSettings()
    {
        return new AppSettings
        {
            SongFolders = new List<string>
            {
                _defaultSongDirectory
            },
            BuiltInSoundFontPath = _defaultSoundFontPath
        };
    }

    private static void ApplyLegacySettings(string json, AppSettings settings)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty(nameof(AppSettings.BuiltInSoundFontVolumePercent), out _)
            && root.TryGetProperty("BuiltInPianoVolumePercent", out var legacyVolume)
            && legacyVolume.TryGetInt32(out var volumePercent))
        {
            settings.BuiltInSoundFontVolumePercent = volumePercent;
        }

        if (!root.TryGetProperty(nameof(AppSettings.BuiltInSoundFontReverbPercent), out _)
            && root.TryGetProperty("BuiltInPianoReverbPercent", out var legacyReverb)
            && legacyReverb.TryGetInt32(out var reverbPercent))
        {
            settings.BuiltInSoundFontReverbPercent = reverbPercent;
        }

        if (!root.TryGetProperty(nameof(AppSettings.PianoRollVisibleRangePercent), out _)
            && root.TryGetProperty("NoteFlowSpeedPercent", out var legacyFlowSpeed)
            && legacyFlowSpeed.TryGetInt32(out var legacyFlowSpeedPercent))
        {
            var normalizedLegacyPercent = Math.Clamp(legacyFlowSpeedPercent, 50, 200);
            settings.PianoRollVisibleRangePercent = Math.Clamp(
                (int)Math.Round(10000d / normalizedLegacyPercent),
                50,
                200);
        }
    }

    private void Normalize(AppSettings settings)
    {
        settings.SongFolders ??= new List<string>();
        if (string.IsNullOrWhiteSpace(settings.PreferredMidiOutputDeviceName)
            || string.Equals(
                settings.PreferredMidiOutputDeviceName,
                LegacyBuiltInPianoOutputName,
                StringComparison.OrdinalIgnoreCase))
        {
            settings.PreferredMidiOutputDeviceName = MidiOutputDevice.BuiltInSoundFont.Name;
        }

        settings.BuiltInSoundFontPath = PortablePath.Resolve(
            _applicationDirectory,
            string.IsNullOrWhiteSpace(settings.BuiltInSoundFontPath)
                ? Path.Combine(DefaultSoundFontDirectoryName, DefaultSoundFontFileName)
                : settings.BuiltInSoundFontPath);
        settings.BuiltInSoundFontBankNumber = Math.Clamp(
            settings.BuiltInSoundFontBankNumber,
            0,
            128);
        settings.BuiltInSoundFontPatchNumber = Math.Clamp(
            settings.BuiltInSoundFontPatchNumber,
            0,
            127);
        settings.BuiltInSoundFontVolumePercent = Math.Clamp(
            settings.BuiltInSoundFontVolumePercent,
            0,
            150);
        settings.BuiltInSoundFontReverbPercent = Math.Clamp(
            settings.BuiltInSoundFontReverbPercent,
            0,
            100);

        settings.SongDifficultyOverrides ??= new Dictionary<string, SongDifficulty>(
            StringComparer.OrdinalIgnoreCase);

        settings.SongFolders = settings.SongFolders
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => PortablePath.Resolve(_applicationDirectory, path))
            .Select(path => string.Equals(
                    path,
                    _legacyDefaultSongDirectory,
                    StringComparison.OrdinalIgnoreCase)
                ? _defaultSongDirectory
                : path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var normalizedOverrides = new Dictionary<string, SongDifficulty>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in settings.SongDifficultyOverrides)
        {
            if (string.IsNullOrWhiteSpace(item.Key))
            {
                continue;
            }

            normalizedOverrides[
                PortablePath.Resolve(_applicationDirectory, item.Key)] = item.Value;
        }

        settings.SongDifficultyOverrides = normalizedOverrides;

        if (settings.SongFolders.Count == 0)
        {
            settings.SongFolders.Add(_defaultSongDirectory);
        }

        settings.AutomaticPlaybackVelocity = Math.Clamp(settings.AutomaticPlaybackVelocity, 1, 127);
        settings.PianoRollVisibleRangePercent = Math.Clamp(
            settings.PianoRollVisibleRangePercent,
            50,
            200);
        settings.MetronomeVolumePercent = Math.Clamp(settings.MetronomeVolumePercent, 0, 100);
        settings.JudgementTimingPercent = Math.Clamp(settings.JudgementTimingPercent, 50, 150);
        if (!Enum.IsDefined(typeof(EditorTimelineDragMode), settings.EditorTimelineDragMode))
        {
            settings.EditorTimelineDragMode = EditorTimelineDragMode.TouchScroll;
        }

        settings.KeyboardLowestMidi = Math.Clamp(settings.KeyboardLowestMidi, 0, 127);
        settings.KeyboardHighestMidi = Math.Clamp(settings.KeyboardHighestMidi, 0, 127);
        if (settings.KeyboardHighestMidi < settings.KeyboardLowestMidi)
        {
            (settings.KeyboardLowestMidi, settings.KeyboardHighestMidi) =
                (settings.KeyboardHighestMidi, settings.KeyboardLowestMidi);
        }
    }

    private AppSettings CreateStoredSettings(AppSettings settings)
    {
        var stored = settings.Clone();
        stored.BuiltInSoundFontPath = PortablePath.ToStored(
            _applicationDirectory,
            settings.BuiltInSoundFontPath);
        stored.SongFolders = settings.SongFolders
            .Select(path => PortablePath.ToStored(_applicationDirectory, path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        stored.SongDifficultyOverrides = settings.SongDifficultyOverrides
            .ToDictionary(
                item => PortablePath.ToStored(_applicationDirectory, item.Key),
                item => item.Value,
                StringComparer.OrdinalIgnoreCase);

        return stored;
    }

    private void EnsureDefaultSongDirectory(AppSettings settings)
    {
        if (settings.SongFolders.Any(path => string.Equals(
                Path.GetFullPath(path),
                _defaultSongDirectory,
                StringComparison.OrdinalIgnoreCase)))
        {
            Directory.CreateDirectory(_defaultSongDirectory);
        }
    }
}
