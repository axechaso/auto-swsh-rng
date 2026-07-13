using AutoSwshRng.Core.Rng;
using PKHeX.Core;
using OwoowEnvironment = owoow.Core.RNG.Generators.Misc.Environment;
using OwoowMenuClose = owoow.Core.RNG.Generators.Misc.MenuClose;
using OwoowWeather = owoow.Core.Enums.WeatherType;

namespace AutoSwshRng.Upstream;

public sealed class OwoowCalibrationService : ICalibrationService
{
    public Task<CalibrationResult> CalculateMenuCloseAsync(
        MenuCloseCalibrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var rng = Create(request.State);
        var advances = OwoowMenuClose.GetAdvances(
            ref rng,
            request.NonPlayerCharacters,
            request.HoldDirection,
            Map(request.Weather));
        return Task.FromResult(CreateResult(advances, rng));
    }

    public Task<CalibrationResult> CalculateRainAsync(
        RainCalibrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var rng = Create(request.State);
        var advances = OwoowEnvironment.GetRainAdvances(ref rng, request.Ticks);
        return Task.FromResult(CreateResult(advances, rng));
    }

    public Task<CalibrationResult> CalculateAreaLoadAsync(
        AreaLoadCalibrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var rng = Create(request.State);
        var advances = OwoowEnvironment.GetAreaLoadAdvances(ref rng, request.AreaRolls);
        advances += OwoowEnvironment.GetAreaLoadNPCAdvances(
            ref rng,
            request.NonPlayerCharacters);
        return Task.FromResult(CreateResult(advances, rng));
    }

    public Task<CalibrationResult> CalculateFlyAsync(
        FlyCalibrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var rng = Create(request.State);
        uint advances = 0;
        advances += OwoowEnvironment.GetRainAdvances(ref rng, request.RainTicksBeforeMap);
        advances += OwoowEnvironment.GetMapMemoryRollAdvances(ref rng);
        advances += OwoowEnvironment.GetRainAdvances(ref rng, request.RainTicksAfterMenu);
        advances += OwoowEnvironment.GetAreaLoadAdvances(ref rng, request.AreaRolls);
        advances += OwoowEnvironment.GetAreaLoadNPCAdvances(
            ref rng,
            request.AreaNonPlayerCharacters);
        advances += OwoowEnvironment.GetRainAdvances(
            ref rng,
            request.RainTicksDuringAreaLoad);
        advances += OwoowMenuClose.GetAdvances(
            ref rng,
            request.MenuNonPlayerCharacters,
            request.HoldDirection,
            Map(request.Weather));
        advances += OwoowEnvironment.GetRainAdvances(
            ref rng,
            request.RainTicksBeforeEncounter);
        return Task.FromResult(CreateResult(advances, rng));
    }

    private static Xoroshiro128Plus Create(RngState state) =>
        new(state.Seed0, state.Seed1);

    private static CalibrationResult CreateResult(uint advances, Xoroshiro128Plus rng)
    {
        var state = rng.GetState();
        return new CalibrationResult(advances, new RngState(state.s0, state.s1));
    }

    private static OwoowWeather Map(Weather weather) => weather switch
    {
        Weather.Any => OwoowWeather.AllWeather,
        Weather.Normal => OwoowWeather.NormalWeather,
        Weather.Overcast => OwoowWeather.Overcast,
        Weather.Raining => OwoowWeather.Raining,
        Weather.Thunderstorm => OwoowWeather.Thunderstorm,
        Weather.IntenseSun => OwoowWeather.IntenseSun,
        Weather.Snowing => OwoowWeather.Snowing,
        Weather.Snowstorm => OwoowWeather.Snowstorm,
        Weather.Sandstorm => OwoowWeather.Sandstorm,
        Weather.HeavyFog => OwoowWeather.HeavyFog,
        _ => throw new ArgumentOutOfRangeException(nameof(weather), weather, "Unknown weather."),
    };
}
