using System.Text.Json;
using AutoSwshRng.Core.Common;
using AutoSwshRng.Core.Rng;

namespace AutoSwshRng.Upstream;

public sealed class OwoowLotoIdStore : ILotoIdStore
{
    private readonly string path;

    public OwoowLotoIdStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A Loto-ID store path is required.", nameof(path));
        }

        this.path = Path.GetFullPath(path);
    }

    public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var ids = await JsonSerializer.DeserializeAsync<List<string>>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false) ?? [];
            Validate(ids);
            return ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException or ArgumentException)
        {
            throw new UpstreamOperationException(
                UpstreamErrorCode.InvalidData,
                $"Unable to load Loto-IDs from '{path}'.",
                exception);
        }
    }

    public async Task SaveAsync(
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        Validate(ids);
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, normalized, cancellationToken: cancellationToken)
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
                $"Unable to save Loto-IDs to '{path}'.",
                exception);
        }
    }

    private static void Validate(IEnumerable<string> ids)
    {
        if (ids.Any(id => id is null || id.Length != 6 || id.Any(character => !char.IsAsciiDigit(character))))
        {
            throw new ArgumentException("Every Loto-ID must contain exactly six digits.", nameof(ids));
        }
    }
}
