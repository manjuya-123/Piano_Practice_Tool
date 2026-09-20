namespace PianoPracticeTool.Services.Midi;

public sealed record SoundFontConfiguration(
    string FilePath,
    int BankNumber,
    int PatchNumber,
    int VolumePercent,
    int ReverbPercent);
