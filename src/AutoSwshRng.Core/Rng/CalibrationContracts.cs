namespace AutoSwshRng.Core.Rng;

public enum Weather
{
    Normal,
    Overcast,
    Raining,
    Thunderstorm,
    IntenseSun,
    Snowing,
    Snowstorm,
    Sandstorm,
    HeavyFog,
}

public sealed record MenuCloseCalibrationRequest(
    RngState State,
    uint NonPlayerCharacters,
    bool HoldDirection,
    Weather Weather);

public sealed record RainCalibrationRequest(RngState State, uint Ticks);

public sealed record AreaLoadCalibrationRequest(
    RngState State,
    uint AreaRolls,
    uint NonPlayerCharacters);

public sealed record FlyCalibrationRequest(
    RngState State,
    uint RainTicksBeforeMap,
    uint RainTicksAfterMenu,
    uint AreaRolls,
    uint AreaNonPlayerCharacters,
    uint RainTicksDuringAreaLoad,
    uint MenuNonPlayerCharacters,
    bool HoldDirection,
    Weather Weather,
    uint RainTicksBeforeEncounter);

public sealed record CalibrationResult(uint Advances, RngState State);

public interface ICalibrationService
{
    Task<CalibrationResult> CalculateMenuCloseAsync(
        MenuCloseCalibrationRequest request,
        CancellationToken cancellationToken = default);

    Task<CalibrationResult> CalculateRainAsync(
        RainCalibrationRequest request,
        CancellationToken cancellationToken = default);

    Task<CalibrationResult> CalculateAreaLoadAsync(
        AreaLoadCalibrationRequest request,
        CancellationToken cancellationToken = default);

    Task<CalibrationResult> CalculateFlyAsync(
        FlyCalibrationRequest request,
        CancellationToken cancellationToken = default);
}
