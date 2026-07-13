namespace AutoSwshRng.Core.Rng;

public enum Weather
{
    Any = -1,
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

public sealed record MenuCloseCalibrationRequest
{
    public MenuCloseCalibrationRequest(
        RngState state,
        uint nonPlayerCharacters,
        bool holdDirection,
        Weather weather)
    {
        ValidateWeather(weather);
        State = state;
        NonPlayerCharacters = nonPlayerCharacters;
        HoldDirection = holdDirection;
        Weather = weather;
    }

    public RngState State { get; }
    public uint NonPlayerCharacters { get; }
    public bool HoldDirection { get; }
    public Weather Weather { get; }

    internal static void ValidateWeather(Weather weather, bool allowAny = false)
    {
        if (!Enum.IsDefined(weather) || (!allowAny && weather == Weather.Any))
        {
            throw new ArgumentOutOfRangeException(nameof(weather));
        }
    }
}

public sealed record RainCalibrationRequest(RngState State, uint Ticks);

public sealed record AreaLoadCalibrationRequest(
    RngState State,
    uint AreaRolls,
    uint NonPlayerCharacters);

public sealed record FlyCalibrationRequest
{
    public FlyCalibrationRequest(
        RngState state,
        uint rainTicksBeforeMap,
        uint rainTicksAfterMenu,
        uint areaRolls,
        uint areaNonPlayerCharacters,
        uint rainTicksDuringAreaLoad,
        uint menuNonPlayerCharacters,
        bool holdDirection,
        Weather weather,
        uint rainTicksBeforeEncounter)
    {
        MenuCloseCalibrationRequest.ValidateWeather(weather);
        State = state;
        RainTicksBeforeMap = rainTicksBeforeMap;
        RainTicksAfterMenu = rainTicksAfterMenu;
        AreaRolls = areaRolls;
        AreaNonPlayerCharacters = areaNonPlayerCharacters;
        RainTicksDuringAreaLoad = rainTicksDuringAreaLoad;
        MenuNonPlayerCharacters = menuNonPlayerCharacters;
        HoldDirection = holdDirection;
        Weather = weather;
        RainTicksBeforeEncounter = rainTicksBeforeEncounter;
    }

    public RngState State { get; }
    public uint RainTicksBeforeMap { get; }
    public uint RainTicksAfterMenu { get; }
    public uint AreaRolls { get; }
    public uint AreaNonPlayerCharacters { get; }
    public uint RainTicksDuringAreaLoad { get; }
    public uint MenuNonPlayerCharacters { get; }
    public bool HoldDirection { get; }
    public Weather Weather { get; }
    public uint RainTicksBeforeEncounter { get; }
}

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
