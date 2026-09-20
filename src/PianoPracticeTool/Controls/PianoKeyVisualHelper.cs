namespace PianoPracticeTool.Controls;

internal static class PianoKeyVisualHelper
{
    public static bool IsBlackKey(int midiNote)
        => midiNote % 12 is 1 or 3 or 6 or 8 or 10;
}
