namespace AutoSwshRng.Core.SpreadFinder;

public readonly record struct IndividualValueRange
{
    public static IndividualValueRange Any => new(0, 31);

    public byte Minimum { get; }

    public byte Maximum { get; }

    public IndividualValueRange(byte minimum, byte maximum)
    {
        if (minimum > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), "Individual values cannot exceed 31.");
        }

        if (maximum > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), "Individual values cannot exceed 31.");
        }

        if (minimum > maximum)
        {
            throw new ArgumentException("Minimum individual value cannot exceed maximum individual value.");
        }

        Minimum = minimum;
        Maximum = maximum;
    }
}
