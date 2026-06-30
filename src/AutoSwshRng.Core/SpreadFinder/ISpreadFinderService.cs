namespace AutoSwshRng.Core.SpreadFinder;

public interface ISpreadFinderService
{
    Task<IReadOnlyList<SpreadSearchResult>> SearchAsync(
        SpreadSearchRequest request,
        CancellationToken cancellationToken = default);
}
