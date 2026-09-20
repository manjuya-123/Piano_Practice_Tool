using System.IO;
using MeltySynth;

namespace PianoPracticeTool.Services.Midi;

public static class SoundFontCatalogService
{
    public static IReadOnlyList<SoundFontPresetInfo> LoadPresets(string soundFontPath)
    {
        if (string.IsNullOrWhiteSpace(soundFontPath))
        {
            throw new ArgumentException("SoundFont path is required.", nameof(soundFontPath));
        }

        var fullPath = Path.GetFullPath(soundFontPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("SoundFont was not found.", fullPath);
        }

        var soundFont = new SoundFont(fullPath);
        return soundFont.Presets
            .Select(preset => new SoundFontPresetInfo(
                preset.BankNumber,
                preset.PatchNumber,
                preset.Name))
            .OrderBy(preset => preset.BankNumber)
            .ThenBy(preset => preset.PatchNumber)
            .ThenBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
