using AutoSwshRng.Core.Common;
using AutoSwshRng.Core.Profiles;
using AutoSwshRng.Core.Rng;

namespace AutoSwshRng.Core.Encounters;

public enum IndividualValueMatch
{
    Range,
    Either,
}

public sealed record IndividualValueConstraint
{
    public static IndividualValueConstraint Any { get; } =
        new(IndividualValueMatch.Range, 0, 31);

    public IndividualValueConstraint(
        IndividualValueMatch match,
        int minimum,
        int maximum)
    {
        if (minimum is < 0 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum));
        }

        if (maximum is < 0 or > 31 || maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum));
        }

        Match = match;
        Minimum = (byte)minimum;
        Maximum = (byte)maximum;
    }

    public IndividualValueMatch Match { get; }
    public byte Minimum { get; }
    public byte Maximum { get; }
}

public enum ShinyFilter
{
    Any,
    Either,
    Star,
    Square,
    None,
}

public enum AuraFilter
{
    Any,
    Brilliant,
    None,
}

public enum HeightFilter
{
    Any,
    XXXS,
    XXS,
    XS,
    Small,
    Medium,
    Large,
    XL,
    XXL,
    XXXL,
    MinOrMax,
}

public enum MarkFilterMode
{
    Ignore,
    None,
    Any,
    Personality,
    PersonalityOrRare,
    Specific,
}

public sealed record MarkFilter
{
    public static MarkFilter Ignore { get; } = new(MarkFilterMode.Ignore);

    public MarkFilter(MarkFilterMode mode, string? specificMark = null)
    {
        if (mode == MarkFilterMode.Specific && string.IsNullOrWhiteSpace(specificMark))
        {
            throw new ArgumentException("A specific mark name is required.", nameof(specificMark));
        }

        Mode = mode;
        SpecificMark = specificMark;
    }

    public MarkFilterMode Mode { get; }
    public string? SpecificMark { get; }
}

public enum PokemonGender
{
    Male,
    Female,
    Genderless,
}

public sealed record EncounterFilter
{
    private readonly IndividualValueConstraint[] individualValues;

    public static EncounterFilter Any { get; } = new();

    public EncounterFilter(
        string? targetSpecies = null,
        ShinyFilter shiny = ShinyFilter.Any,
        AuraFilter aura = AuraFilter.Any,
        MarkFilter? mark = null,
        HeightFilter height = HeightFilter.Any,
        IEnumerable<IndividualValueConstraint>? individualValues = null,
        bool rareEncryptionConstant = false,
        string? targetNature = null,
        string? targetAbility = null,
        PokemonGender? targetGender = null)
    {
        this.individualValues = individualValues?.ToArray()
            ?? Enumerable.Repeat(IndividualValueConstraint.Any, 6).ToArray();
        if (this.individualValues.Length != 6)
        {
            throw new ArgumentException("Exactly six IV constraints are required.", nameof(individualValues));
        }

        TargetSpecies = string.IsNullOrWhiteSpace(targetSpecies) ? null : targetSpecies;
        Shiny = shiny;
        Aura = aura;
        Mark = mark ?? MarkFilter.Ignore;
        Height = height;
        RareEncryptionConstant = rareEncryptionConstant;
        TargetNature = string.IsNullOrWhiteSpace(targetNature) ? null : targetNature;
        TargetAbility = string.IsNullOrWhiteSpace(targetAbility) ? null : targetAbility;
        TargetGender = targetGender;
    }

    public string? TargetSpecies { get; }
    public ShinyFilter Shiny { get; }
    public AuraFilter Aura { get; }
    public MarkFilter Mark { get; }
    public HeightFilter Height { get; }
    public IReadOnlyList<IndividualValueConstraint> IndividualValues => individualValues;
    public bool RareEncryptionConstant { get; }
    public string? TargetNature { get; }
    public string? TargetAbility { get; }
    public PokemonGender? TargetGender { get; }
}

public sealed record OverworldEnvironmentSettings(
    bool ConsiderMenuClose,
    uint MenuCloseNonPlayerCharacters,
    bool HoldDirection,
    bool ConsiderFlying,
    uint AreaLoadAdvances,
    uint AreaLoadNonPlayerCharacters,
    bool ConsiderRain,
    uint RainTicksDuringAreaLoad,
    uint RainTicksBeforeEncounter)
{
    public static OverworldEnvironmentSettings None { get; } =
        new(false, 0, false, false, 0, 0, false, 0, 0);
}

public sealed record OverworldSearchRequest
{
    private readonly short[] dexRecommendationSlots;

    public OverworldSearchRequest(
        RngState initialState,
        ulong startAdvance,
        ulong endAdvance,
        EncounterCatalogRequest context,
        RngProfile profile,
        EncounterFilter filter,
        OverworldEnvironmentSettings environment,
        int auraKnockouts = 500,
        int hiddenMaximumStep = 0,
        IEnumerable<short>? dexRecommendationSlots = null)
    {
        if (endAdvance < startAdvance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endAdvance),
                "End advance cannot be less than start advance.");
        }

        if (startAdvance == 0 && endAdvance == ulong.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endAdvance),
                "The inclusive advance range is too large.");
        }

        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentOutOfRangeException.ThrowIfNegative(auraKnockouts);
        ArgumentOutOfRangeException.ThrowIfNegative(hiddenMaximumStep);
        if (context.Kind == EncounterKind.Static && filter.TargetSpecies is null)
        {
            throw new ArgumentException("Static encounters require a target species.", nameof(filter));
        }

        this.dexRecommendationSlots = dexRecommendationSlots?.ToArray() ?? [0, 0, 0, 0];
        if (this.dexRecommendationSlots.Length != 4)
        {
            throw new ArgumentException(
                "Exactly four Pokédex recommendation slots are required.",
                nameof(dexRecommendationSlots));
        }

        InitialState = initialState;
        StartAdvance = startAdvance;
        EndAdvance = endAdvance;
        Context = context;
        Profile = profile;
        Filter = filter;
        Environment = environment;
        AuraKnockouts = auraKnockouts;
        HiddenMaximumStep = hiddenMaximumStep;
    }

    public RngState InitialState { get; }
    public ulong StartAdvance { get; }
    public ulong EndAdvance { get; }
    public EncounterCatalogRequest Context { get; }
    public RngProfile Profile { get; }
    public EncounterFilter Filter { get; }
    public OverworldEnvironmentSettings Environment { get; }
    public int AuraKnockouts { get; }
    public int HiddenMaximumStep { get; }
    public IReadOnlyList<short> DexRecommendationSlots => dexRecommendationSlots;
}

public sealed record OverworldEncounterResult(
    ulong Advance,
    uint Jump,
    byte Step,
    char Animation,
    string Species,
    string Shiny,
    bool BrilliantAura,
    byte Level,
    string Ability,
    string Nature,
    PokemonGender Gender,
    RngIndividualValues IndividualValues,
    string Mark,
    uint EncryptionConstant,
    uint PersonalityId,
    byte Height,
    string HeightDescription,
    string Item,
    string EggMove,
    RngState State);

public interface IOverworldEncounterService
{
    Task<IReadOnlyList<OverworldEncounterResult>> SearchAsync(
        OverworldSearchRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
