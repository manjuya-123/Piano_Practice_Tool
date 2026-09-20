namespace PianoPracticeTool.Services.Midi;

public sealed record MidiOutputDevice(int DeviceNumber, string Name)
{
    public const int BuiltInDeviceNumber = -1;

    public static MidiOutputDevice BuiltInSoundFont { get; } =
        new(BuiltInDeviceNumber, "Built-in SoundFont Synthesizer");

    public bool IsBuiltIn => DeviceNumber == BuiltInDeviceNumber;
}

public static class MidiOutputSelector
{
    public static MidiOutputDevice? Select(
        IReadOnlyList<MidiOutputDevice> devices,
        string? preferredName)
    {
        ArgumentNullException.ThrowIfNull(devices);

        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            var preferred = devices.FirstOrDefault(device => string.Equals(
                device.Name,
                preferredName,
                StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return devices.FirstOrDefault(device => device.IsBuiltIn);
    }
}
