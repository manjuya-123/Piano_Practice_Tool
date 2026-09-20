namespace PianoPracticeTool.Services.Midi;

public sealed record SoundFontPresetInfo(
    int BankNumber,
    int PatchNumber,
    string Name)
{
    public bool IsSupported
        => BankNumber is >= 0 and <= 128
            && PatchNumber is >= 0 and <= 127;

    public string DisplayName
        => $"{BankNumber:000}:{PatchNumber:000} {Name}";
}
