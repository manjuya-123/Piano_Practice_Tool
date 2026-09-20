namespace PianoPracticeTool.Core;

public readonly record struct PracticeMidiRange
{
    public PracticeMidiRange(int lowestMidi, int highestMidi)
    {
        if (lowestMidi is < 0 or > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(lowestMidi));
        }

        if (highestMidi is < 0 or > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(highestMidi));
        }

        if (highestMidi < lowestMidi)
        {
            throw new ArgumentException("Highest MIDI note must not be lower than the lowest MIDI note.");
        }

        LowestMidi = lowestMidi;
        HighestMidi = highestMidi;
    }

    public static PracticeMidiRange Full { get; } = new(0, 127);

    public int LowestMidi { get; }

    public int HighestMidi { get; }

    public bool Contains(int midiNote)
        => midiNote >= LowestMidi && midiNote <= HighestMidi;
}
