namespace PianoPracticeTool.Core;

public static class PracticeSpeed
{
    public const double MinimumMultiplier = 0.1d;
    public const double MaximumMultiplier = 2d;
    public const double StepMultiplier = 0.1d;

    public static double Clamp(double multiplier)
        => Math.Clamp(multiplier, MinimumMultiplier, MaximumMultiplier);

    public static double Step(double multiplier, int direction)
    {
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        var stepped = multiplier + direction * StepMultiplier;
        var rounded = Math.Round(stepped / StepMultiplier) * StepMultiplier;
        return Clamp(rounded);
    }
}
