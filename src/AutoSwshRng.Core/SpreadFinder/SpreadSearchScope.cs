using System.Collections.ObjectModel;

namespace AutoSwshRng.Core.SpreadFinder;

public abstract record SpreadSearchScope
{
    private protected SpreadSearchScope()
    {
    }

    public sealed record Seeds : SpreadSearchScope
    {
        public IReadOnlyList<uint> Values { get; }

        public Seeds(IEnumerable<uint> values)
        {
            ArgumentNullException.ThrowIfNull(values);

            var copiedValues = values.ToArray();
            if (copiedValues.Length == 0)
            {
                throw new ArgumentException("At least one seed is required.", nameof(values));
            }

            Values = new ReadOnlyCollection<uint>(copiedValues);
        }
    }

    public sealed record Range : SpreadSearchScope
    {
        public uint Start { get; }

        public uint End { get; }

        public int PartitionCount { get; }

        public Range(uint start, uint end, int partitionCount = 1)
        {
            if (start > end)
            {
                throw new ArgumentException("Start seed cannot exceed end seed.");
            }

            ValidatePartitionCount(partitionCount);

            Start = start;
            End = end;
            PartitionCount = partitionCount;
        }
    }

    public sealed record EntireSpace : SpreadSearchScope
    {
        public int PartitionCount { get; }

        public EntireSpace(int partitionCount)
        {
            ValidatePartitionCount(partitionCount);
            PartitionCount = partitionCount;
        }
    }

    private static void ValidatePartitionCount(int partitionCount)
    {
        if (partitionCount is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(partitionCount), "Partition count must be between 1 and 64.");
        }
    }
}
