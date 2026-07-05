namespace AutoSwshRng.Core.Rng;

public readonly record struct RngState(ulong Seed0, ulong Seed1);

public enum XoroshiroOperation
{
    Next,
    Previous,
    NextInteger,
    FindInitial,
}

public sealed record XoroshiroRequest
{
    public XoroshiroRequest(RngState state, XoroshiroOperation operation, ulong amount)
    {
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        if (amount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");
        }

        State = state;
        Operation = operation;
        Amount = amount;
    }

    public RngState State { get; }
    public XoroshiroOperation Operation { get; }
    public ulong Amount { get; }
}

public sealed record XoroshiroResult(
    RngState State,
    ulong Distance,
    ulong? Value,
    bool Found);

public sealed record FixedSeedRequest
{
    public FixedSeedRequest(
        uint seed,
        bool shiny,
        uint trainerShinyValue,
        int guaranteedIndividualValues)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(guaranteedIndividualValues);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(guaranteedIndividualValues, 6);

        Seed = seed;
        Shiny = shiny;
        TrainerShinyValue = trainerShinyValue;
        GuaranteedIndividualValues = guaranteedIndividualValues;
    }

    public uint Seed { get; }
    public bool Shiny { get; }
    public uint TrainerShinyValue { get; }
    public int GuaranteedIndividualValues { get; }
}

public sealed class RngIndividualValues : IEquatable<RngIndividualValues>
{
    private readonly byte[] values;

    public RngIndividualValues(IEnumerable<byte> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        this.values = values.ToArray();
        if (this.values.Length != 6)
        {
            throw new ArgumentException("Exactly six individual values are required.", nameof(values));
        }

        if (this.values.Any(value => value > 31))
        {
            throw new ArgumentOutOfRangeException(nameof(values), "Individual values must be between 0 and 31.");
        }
    }

    public byte HP => values[0];
    public byte Attack => values[1];
    public byte Defense => values[2];
    public byte SpecialAttack => values[3];
    public byte SpecialDefense => values[4];
    public byte Speed => values[5];

    public byte[] ToArray() => values.ToArray();

    public bool Equals(RngIndividualValues? other) =>
        other is not null && values.SequenceEqual(other.values);

    public override bool Equals(object? obj) => obj is RngIndividualValues other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in values)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}

public sealed record FixedSeedResult(
    uint EncryptionConstant,
    uint PersonalityId,
    RngIndividualValues IndividualValues,
    byte Height);

public sealed record RetailSeedRequest
{
    public RetailSeedRequest(string observations)
    {
        ValidateBinary(observations, 128, 128, nameof(observations));
        Observations = observations;
    }

    public string Observations { get; }

    internal static void ValidateBinary(
        string observations,
        int minimumLength,
        int maximumLength,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(observations, parameterName);
        if (observations.Length < minimumLength || observations.Length > maximumLength)
        {
            throw new ArgumentException(
                $"Observation count must be between {minimumLength} and {maximumLength}.",
                parameterName);
        }

        if (observations.Any(value => value is not ('0' or '1')))
        {
            throw new ArgumentException("Observations must contain only zero and one.", parameterName);
        }
    }
}

public sealed record RetailRangeSeedRequest
{
    public RetailRangeSeedRequest(string observations, int minimumAdvance, int maximumAdvance)
    {
        RetailSeedRequest.ValidateBinary(observations, 64, 128, nameof(observations));
        ArgumentOutOfRangeException.ThrowIfNegative(minimumAdvance);
        if (maximumAdvance < minimumAdvance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumAdvance),
                "Maximum advance cannot be less than minimum advance.");
        }

        Observations = observations;
        MinimumAdvance = minimumAdvance;
        MaximumAdvance = maximumAdvance;
    }

    public string Observations { get; }
    public int MinimumAdvance { get; }
    public int MaximumAdvance { get; }
}

public sealed record AnimationSequenceRequest
{
    public AnimationSequenceRequest(RngState state, ulong initialAdvance, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        State = state;
        InitialAdvance = initialAdvance;
        Count = count;
    }

    public RngState State { get; }
    public ulong InitialAdvance { get; }
    public int Count { get; }
}

public sealed class AnimationSequenceResult
{
    private readonly IReadOnlyList<byte> observations;

    public AnimationSequenceResult(IEnumerable<byte> observations, RngState initialState)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var observationCopy = observations.ToArray();
        if (observationCopy.Any(value => value > 1))
        {
            throw new ArgumentException("Animation observations must be zero or one.", nameof(observations));
        }

        this.observations = Array.AsReadOnly(observationCopy);
        InitialState = initialState;
    }

    public IReadOnlyList<byte> Observations => observations;
    public RngState InitialState { get; }
}

public sealed record ReidentifySeedRequest
{
    private readonly IReadOnlyList<byte> observations;

    public ReidentifySeedRequest(
        IEnumerable<byte> observations,
        RngState initialState,
        string pattern)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var observationCopy = observations.ToArray();
        if (observationCopy.Any(value => value > 1))
        {
            throw new ArgumentException("Animation observations must be zero or one.", nameof(observations));
        }

        this.observations = Array.AsReadOnly(observationCopy);
        RetailSeedRequest.ValidateBinary(pattern, 1, int.MaxValue, nameof(pattern));
        InitialState = initialState;
        Pattern = pattern;
    }

    public IReadOnlyList<byte> Observations => observations;
    public RngState InitialState { get; }
    public string Pattern { get; }
}

public sealed record ReidentifySeedResult(
    int Hits,
    int Advances,
    RngState State);

public interface IXoroshiroService
{
    Task<XoroshiroResult> CalculateAsync(
        XoroshiroRequest request,
        CancellationToken cancellationToken = default);

    Task<FixedSeedResult> GenerateFixedAsync(
        FixedSeedRequest request,
        CancellationToken cancellationToken = default);
}

public interface IRetailSeedService
{
    Task<RngState> FindExactAsync(
        RetailSeedRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RngState>> FindRangeAsync(
        RetailRangeSeedRequest request,
        IProgress<Common.OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<AnimationSequenceResult> GenerateAnimationSequenceAsync(
        AnimationSequenceRequest request,
        CancellationToken cancellationToken = default);

    Task<ReidentifySeedResult> ReidentifyAsync(
        ReidentifySeedRequest request,
        CancellationToken cancellationToken = default);
}
