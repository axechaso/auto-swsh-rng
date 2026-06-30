using System.Collections.ObjectModel;

namespace AutoSwshRng.Core.SpreadFinder;

public sealed class SpreadSearchRequest
{
    public SpreadSearchScope Scope { get; }

    public IReadOnlyList<IndividualValueRange> IndividualValueRanges { get; }

    public int GuaranteedIndividualValues { get; }

    public bool RareEncryptionConstant { get; }

    public SpreadScale Scale { get; }

    public SpreadSearchRequest(
        SpreadSearchScope scope,
        IEnumerable<IndividualValueRange> individualValueRanges,
        int guaranteedIndividualValues = 0,
        bool rareEncryptionConstant = false,
        SpreadScale scale = SpreadScale.Any)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(individualValueRanges);

        var copiedRanges = individualValueRanges.ToArray();
        if (copiedRanges.Length != 6)
        {
            throw new ArgumentException("Exactly six individual value ranges are required.", nameof(individualValueRanges));
        }

        if (guaranteedIndividualValues is < 0 or > 6)
        {
            throw new ArgumentOutOfRangeException(
                nameof(guaranteedIndividualValues),
                "Guaranteed individual value count must be between 0 and 6.");
        }

        Scope = scope;
        IndividualValueRanges = new ReadOnlyCollection<IndividualValueRange>(copiedRanges);
        GuaranteedIndividualValues = guaranteedIndividualValues;
        RareEncryptionConstant = rareEncryptionConstant;
        Scale = scale;
    }
}
