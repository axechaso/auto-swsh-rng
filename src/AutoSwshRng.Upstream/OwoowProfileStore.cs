using System.Globalization;
using System.Text.Json;
using AutoSwshRng.Core.Common;
using AutoSwshRng.Core.Profiles;
using owoow.Core.Interfaces;

namespace AutoSwshRng.Upstream;

public sealed class OwoowProfileStore : IProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string path;

    public OwoowProfileStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A profile store path is required.", nameof(path));
        }

        this.path = Path.GetFullPath(path);
    }

    public async Task<RngApplicationSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path))
        {
            return new RngApplicationSettings(null, []);
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<ProfileDocument>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            if (document is null)
            {
                throw new InvalidDataException("The profile document is empty.");
            }

            var profiles = document.Profiles.Select(MapProfile).ToArray();
            return new RngApplicationSettings(
                document.ActiveProfileName,
                profiles,
                document.MaxSearchTasksPowerOfTwo);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException
                or IOException
                or InvalidDataException
                or ArgumentException
                or FormatException)
        {
            throw new UpstreamOperationException(
                UpstreamErrorCode.InvalidData,
                $"Unable to load profile settings from '{path}'.",
                exception);
        }
    }

    public async Task SaveAsync(
        RngApplicationSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var document = new ProfileDocument
            {
                ActiveProfileName = settings.ActiveProfileName,
                MaxSearchTasksPowerOfTwo = settings.MaxSearchTasksPowerOfTwo,
                Profiles = settings.Profiles.Select(MapProfile).ToList(),
            };

            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new UpstreamOperationException(
                UpstreamErrorCode.UpstreamFailure,
                $"Unable to save profile settings to '{path}'.",
                exception);
        }
    }

    private static RngProfile MapProfile(Profile profile)
    {
        return new RngProfile(
            profile.Name,
            (GameVersion)profile.GameVersion,
            int.Parse(profile.TID, NumberStyles.None, CultureInfo.InvariantCulture),
            int.Parse(profile.SID, NumberStyles.None, CultureInfo.InvariantCulture),
            profile.HasShinyCharm,
            profile.HasMarkCharm);
    }

    private static Profile MapProfile(RngProfile profile)
    {
        return new Profile
        {
            Name = profile.Name,
            GameVersion = (int)profile.Game,
            TID = profile.TrainerId.ToString("D5", CultureInfo.InvariantCulture),
            SID = profile.SecretId.ToString("D5", CultureInfo.InvariantCulture),
            HasShinyCharm = profile.HasShinyCharm,
            HasMarkCharm = profile.HasMarkCharm,
        };
    }

    private sealed class ProfileDocument
    {
        public string? ActiveProfileName { get; set; }
        public int MaxSearchTasksPowerOfTwo { get; set; } = 2;
        public List<Profile> Profiles { get; set; } = [];
    }
}
