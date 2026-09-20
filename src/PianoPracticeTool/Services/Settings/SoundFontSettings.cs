using PianoPracticeTool.Services.Midi;

namespace PianoPracticeTool.Services.Settings;

public static class SoundFontSettings
{
    public static SoundFontConfiguration CreateConfiguration(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new SoundFontConfiguration(
            settings.BuiltInSoundFontPath,
            settings.BuiltInSoundFontBankNumber,
            settings.BuiltInSoundFontPatchNumber,
            settings.BuiltInSoundFontVolumePercent,
            settings.BuiltInSoundFontReverbPercent);
    }
}
