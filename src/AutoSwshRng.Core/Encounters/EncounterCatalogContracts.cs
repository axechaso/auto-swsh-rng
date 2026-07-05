using AutoSwshRng.Core.Profiles;

namespace AutoSwshRng.Core.Encounters;

public enum EncounterKind
{
    Static,
    Symbol,
    Hidden,
    Fishing,
}

public sealed record EncounterCatalogRequest
{
    public EncounterCatalogRequest(
        GameVersion game,
        EncounterKind kind,
        string area,
        string weather,
        string leadAbility = "")
    {
        if (!Enum.IsDefined(game))
        {
            throw new ArgumentOutOfRangeException(nameof(game));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (string.IsNullOrWhiteSpace(area))
        {
            throw new ArgumentException("Area is required.", nameof(area));
        }

        if (string.IsNullOrWhiteSpace(weather))
        {
            throw new ArgumentException("Weather is required.", nameof(weather));
        }

        Game = game;
        Kind = kind;
        Area = area;
        Weather = weather;
        LeadAbility = leadAbility ?? string.Empty;
    }

    public GameVersion Game { get; }
    public EncounterKind Kind { get; }
    public string Area { get; }
    public string Weather { get; }
    public string LeadAbility { get; }
}

public sealed record PokemonPersonalData(
    byte EggMoveCount,
    IReadOnlyList<string> EggMoves,
    bool HasItems,
    IReadOnlyList<string> Items,
    short DeveloperId,
    short GenderRatio,
    IReadOnlyList<string> Types,
    IReadOnlyList<string> Abilities);

public sealed record EncounterSlot(
    int TableKey,
    string Species,
    int SlotMinimum,
    int SlotMaximum,
    int MinimumLevel,
    int MaximumLevel,
    int? FixedLevel,
    bool IsAbilityLocked,
    bool IsGenderLocked,
    bool IsShinyLocked,
    ulong LockedAbility,
    int GuaranteedIndividualValues,
    PokemonPersonalData Personal);

public sealed record EncounterTableResult(
    IReadOnlyList<EncounterSlot> Slots,
    IReadOnlyList<EncounterSlot> AbilityModifiedSlots,
    IReadOnlyList<EncounterSlot> StaticSlots);

public sealed record EncounterLookupResult(
    string Species,
    int SlotMinimum,
    int SlotMaximum,
    int Level,
    int MinimumLevel,
    int MaximumLevel,
    int EncounterRate,
    bool IsAbilityLocked,
    bool IsGenderLocked,
    bool IsShinyLocked,
    ulong LockedAbility,
    int GuaranteedIndividualValues,
    string Weather,
    string Area,
    string EncounterKind);

public interface IEncounterCatalogService
{
    Task<IReadOnlyList<string>> GetAreasAsync(
        GameVersion game,
        EncounterKind kind,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetWeatherAsync(
        GameVersion game,
        EncounterKind kind,
        string area,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetSpeciesAsync(
        GameVersion game,
        EncounterKind kind,
        string area,
        string weather,
        CancellationToken cancellationToken = default);

    Task<EncounterTableResult> GetTableAsync(
        EncounterCatalogRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EncounterLookupResult>> LookupAsync(
        GameVersion game,
        string species,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetDexRecommendationOptionsAsync(
        bool includeNone = true,
        CancellationToken cancellationToken = default);
}
