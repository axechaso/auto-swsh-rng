namespace AutoSwshRng.Core.SpreadFinder;

public sealed record SpreadSearchResult(
    uint Seed,
    uint EncryptionConstant,
    IndividualValues IndividualValues,
    byte Height,
    SpreadScale Scale);
