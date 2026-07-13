using AutoSwshRng.Core.Connection;
using AutoSwshRng.Core.Encounters;
using AutoSwshRng.Core.Profiles;
using AutoSwshRng.Core.Rng;
using AutoSwshRng.Core.SpreadFinder;
using AutoSwshRng.Upstream;

namespace AutoSwshRng.App.Controls;

public sealed class OwoowUiServices
{
    public OwoowUiServices(
        IOwoowConnectionService connection,
        IProfileStore profiles,
        IEncounterCatalogService encounterCatalog,
        IOverworldEncounterService encounters,
        ICalibrationService calibration,
        IRetailSeedService retailSeeds,
        IXoroshiroService xoroshiro,
        ISpreadFinderService spreadFinder,
        ISpecialRngToolService specialTools,
        ILotoIdStore? lotoIds = null)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        EncounterCatalog = encounterCatalog ?? throw new ArgumentNullException(nameof(encounterCatalog));
        Encounters = encounters ?? throw new ArgumentNullException(nameof(encounters));
        Calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
        RetailSeeds = retailSeeds ?? throw new ArgumentNullException(nameof(retailSeeds));
        Xoroshiro = xoroshiro ?? throw new ArgumentNullException(nameof(xoroshiro));
        SpreadFinder = spreadFinder ?? throw new ArgumentNullException(nameof(spreadFinder));
        SpecialTools = specialTools ?? throw new ArgumentNullException(nameof(specialTools));
        LotoIds = lotoIds ?? new VolatileLotoIdStore();
    }

    public IOwoowConnectionService Connection { get; }
    public IProfileStore Profiles { get; }
    public IEncounterCatalogService EncounterCatalog { get; }
    public IOverworldEncounterService Encounters { get; }
    public ICalibrationService Calibration { get; }
    public IRetailSeedService RetailSeeds { get; }
    public IXoroshiroService Xoroshiro { get; }
    public ISpreadFinderService SpreadFinder { get; }
    public ISpecialRngToolService SpecialTools { get; }
    public ILotoIdStore LotoIds { get; }

    public static OwoowUiServices CreateDefault()
    {
        var profilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutoSwshRng",
            "owoow-profiles.json");
        var lotoIdPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutoSwshRng",
            "owoow-loto-ids.json");

        return new OwoowUiServices(
            new OwoowConnectionService(),
            new OwoowProfileStore(profilePath),
            new OwoowEncounterCatalogService(),
            new OwoowOverworldEncounterService(),
            new OwoowCalibrationService(),
            new OwoowRetailSeedService(),
            new OwoowXoroshiroService(),
            new OwoowSpreadFinderService(),
            new OwoowSpecialRngToolService(),
            new OwoowLotoIdStore(lotoIdPath));
    }

    private sealed class VolatileLotoIdStore : ILotoIdStore
    {
        private IReadOnlyList<string> ids = [];

        public Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ids);
        }

        public Task SaveAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            cancellationToken.ThrowIfCancellationRequested();
            this.ids = ids.ToArray();
            return Task.CompletedTask;
        }
    }
}
