using AutoSwshRng.Core.Encounters;
using AutoSwshRng.Core.Common;
using AutoSwshRng.Core.Profiles;
using owoow.Core.EncounterTable;
using owoow.Core.Interfaces;
using OwoowEncounterKind = owoow.Core.Enums.EncounterType;
using OwoowEncounters = owoow.Core.Encounters;
using OwoowGame = owoow.Core.Enums.Game;

namespace AutoSwshRng.Upstream;

public sealed class OwoowEncounterCatalogService : IEncounterCatalogService
{
    public Task<IReadOnlyList<string>> GetAreasAsync(
        GameVersion game,
        EncounterKind kind,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(
            OwoowEncounters.GetAreaList(Map(game), Map(kind))
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    public Task<IReadOnlyList<string>> GetWeatherAsync(
        GameVersion game,
        EncounterKind kind,
        string area,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(
            OwoowEncounters.GetWeatherList(Map(game), Map(kind), area).ToArray());
    }

    public Task<IReadOnlyList<string>> GetSpeciesAsync(
        GameVersion game,
        EncounterKind kind,
        string area,
        string weather,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        ArgumentException.ThrowIfNullOrWhiteSpace(weather);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(
            OwoowEncounters.GetSpeciesList(Map(game), Map(kind), area, weather)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    public Task<EncounterTableResult> GetTableAsync(
        EncounterCatalogRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var game = Map(request.Game);
        var kind = Map(request.Kind);
        var areas = OwoowEncounters.GetAreaList(game, kind);
        if (!areas.Contains(request.Area, StringComparer.Ordinal))
        {
            throw new UpstreamOperationException(
                UpstreamErrorCode.Validation,
                $"Unknown encounter area '{request.Area}'.");
        }

        var weather = OwoowEncounters.GetWeatherList(
            game,
            kind,
            request.Area);
        if (!weather.Contains(request.Weather, StringComparer.Ordinal))
        {
            throw new UpstreamOperationException(
                UpstreamErrorCode.Validation,
                $"Unknown encounter weather '{request.Weather}' for area '{request.Area}'.");
        }

        var table = new EncounterTable(
            game,
            kind,
            request.Area,
            request.Weather,
            request.LeadAbility);

        return Task.FromResult(
            new EncounterTableResult(
                table.MainTable.Select(entry => Map(entry.Key, entry.Value)).ToArray(),
                table.AbilityTable.Select(entry => Map(entry.Key, entry.Value)).ToArray(),
                table.StaticTable.Select(entry => Map(entry.Key, entry.Value)).ToArray()));
    }

    public Task<IReadOnlyList<EncounterLookupResult>> LookupAsync(
        GameVersion game,
        string species,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(species);
        cancellationToken.ThrowIfCancellationRequested();
        var lookup = OwoowEncounters.GetEncounterLookupForGame(Map(game));
        var results = lookup.TryGetValue(species, out var entries)
            ? entries.Select(Map).ToArray()
            : [];
        return Task.FromResult<IReadOnlyList<EncounterLookupResult>>(results);
    }

    public Task<IReadOnlyList<string>> GetDexRecommendationOptionsAsync(
        bool includeNone = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(
            OwoowEncounters.GetDexRecOptions(includeNone).ToArray());
    }

    private static EncounterSlot Map(int key, IEncounterTableEntry entry)
    {
        return new EncounterSlot(
            key,
            entry.Species ?? string.Empty,
            entry.SlotMin,
            entry.SlotMax,
            entry.MinLevel,
            entry.MaxLevel,
            null,
            false,
            false,
            false,
            0,
            0,
            MapPersonal(entry));
    }

    private static EncounterSlot Map(int key, IEncounterStaticTableEntry entry)
    {
        return new EncounterSlot(
            key,
            entry.Species ?? string.Empty,
            0,
            0,
            entry.Level,
            entry.Level,
            entry.Level,
            entry.IsAbilityLocked,
            entry.IsGenderLocked,
            entry.IsShinyLocked,
            entry.Ability,
            entry.GuaranteedIVs,
            MapPersonal(entry));
    }

    private static PokemonPersonalData MapPersonal(IPersonal personal)
    {
        return new PokemonPersonalData(
            personal.EggMoveCount,
            personal.EggMoves?.ToArray() ?? [],
            personal.HasItems,
            personal.Items?.ToArray() ?? [],
            personal.DevId,
            personal.Gender,
            personal.Types?.ToArray() ?? [],
            personal.Abilities?.ToArray() ?? []);
    }

    private static EncounterLookupResult Map(EncounterLookupEntry entry)
    {
        return new EncounterLookupResult(
            entry.Species,
            entry.SlotMin,
            entry.SlotMax,
            entry.Level,
            entry.MinLevel,
            entry.MaxLevel,
            entry.EncounterRate,
            entry.IsAbilityLocked,
            entry.IsGenderLocked,
            entry.IsShinyLocked,
            entry.Ability,
            entry.GuaranteedIVs,
            entry.Weather,
            entry.Area,
            entry.EncounterType);
    }

    private static OwoowGame Map(GameVersion game) => game switch
    {
        GameVersion.Sword => OwoowGame.Sword,
        GameVersion.Shield => OwoowGame.Shield,
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, "Unknown game version."),
    };

    private static OwoowEncounterKind Map(EncounterKind kind) => kind switch
    {
        EncounterKind.Static => OwoowEncounterKind.Static,
        EncounterKind.Symbol => OwoowEncounterKind.Symbol,
        EncounterKind.Hidden => OwoowEncounterKind.Hidden,
        EncounterKind.Fishing => OwoowEncounterKind.Fishing,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown encounter kind."),
    };
}
