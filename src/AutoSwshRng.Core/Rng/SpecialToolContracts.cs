using System.Collections.ObjectModel;
using AutoSwshRng.Core.Common;
using AutoSwshRng.Core.Profiles;

namespace AutoSwshRng.Core.Rng;

public enum SpecialToolKind
{
    LotoId,
    CramOMatic,
    WattTrader,
    DiggingPa,
    DiggingBro,
    WailordRespawn,
}

public enum LotoPrizeFilter
{
    MasterBall,
    RareCandy,
    PPMax,
    PPUp,
    MoomooMilk,
    Any,
}

public enum SuccessFilter
{
    Yes,
    No,
    Any,
}

public enum CramInputItem
{
    BlackApricorn,
    BlueApricorn,
    GreenApricorn,
    PinkApricorn,
    RedApricorn,
    WhiteApricorn,
    YellowApricorn,
    SweetIngredient,
}

public enum CramPrizeFilter
{
    SportBall,
    SafariBall,
    ApricornBall,
    ShopBall,
    StarSweet,
    RibbonSweet,
    StrawberrySweet,
    Any,
}

public enum DiggingBroReward : byte
{
    GoldBottleCap,
    BottleCap,
    NormalGem,
    StickyBarb,
    LightClay,
    LaggingTail,
    IronBall,
    MetalCoat,
    IceStone,
    DawnStone,
    DuskStone,
    ShinyStone,
    MoonStone,
    SunStone,
    FossilizedFish,
    FossilizedDrake,
    FossilizedDino,
    FossilizedBird,
    WishingPiece,
    CometShard,
    RareBone,
}

public sealed record SpecialToolMenuClose(
    bool Enabled,
    uint NonPlayerCharacters,
    bool HoldDirection,
    Weather Weather)
{
    public static SpecialToolMenuClose None { get; } =
        new(false, 0, false, Weather.Normal);
}

public sealed class SpecialToolSearchRequest
{
    private readonly string[] lotoIds;
    private readonly CramInputItem[] cramInputs;
    private readonly ReadOnlyDictionary<DiggingBroReward, byte> diggingBroMinimumRewards;

    public SpecialToolSearchRequest(
        SpecialToolKind kind,
        RngState state,
        ulong startAdvance,
        ulong endAdvance,
        GameVersion game = GameVersion.Sword,
        IReadOnlyList<string>? lotoIds = null,
        LotoPrizeFilter lotoPrize = LotoPrizeFilter.Any,
        SuccessFilter success = SuccessFilter.Any,
        IReadOnlyList<CramInputItem>? cramInputs = null,
        CramPrizeFilter cramPrize = CramPrizeFilter.Any,
        bool bonusOnly = false,
        uint wattTraderSlotMinimum = 0,
        uint wattTraderSlotMaximum = 999,
        ulong diggingPaMinimumWatts = 0,
        int diggingBroMinimumTotal = 0,
        IReadOnlyDictionary<DiggingBroReward, byte>? diggingBroMinimumRewards = null,
        SpecialToolMenuClose? menuClose = null)
    {
        if (endAdvance < startAdvance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endAdvance),
                "End advance cannot be less than start advance.");
        }

        if (startAdvance == 0 && endAdvance == ulong.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endAdvance),
                "The inclusive advance range is too large.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (!Enum.IsDefined(game))
        {
            throw new ArgumentOutOfRangeException(nameof(game));
        }

        this.lotoIds = lotoIds?.ToArray() ?? [];
        if (this.lotoIds.Any(id =>
                id.Length != 6 || id.Any(character => !char.IsAsciiDigit(character))))
        {
            throw new ArgumentException("Every Loto-ID must contain exactly six digits.", nameof(lotoIds));
        }

        this.cramInputs = cramInputs?.ToArray()
            ?? Enumerable.Repeat(CramInputItem.BlackApricorn, 4).ToArray();
        if (this.cramInputs.Length != 4)
        {
            throw new ArgumentException("Exactly four Cram-o-matic inputs are required.", nameof(cramInputs));
        }

        if (this.cramInputs.Any(value => !Enum.IsDefined(value)))
        {
            throw new ArgumentOutOfRangeException(nameof(cramInputs));
        }

        if (wattTraderSlotMaximum < wattTraderSlotMinimum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wattTraderSlotMaximum),
                "Maximum Watt Trader slot cannot be less than minimum slot.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(diggingBroMinimumTotal);
        var rewardCopy = diggingBroMinimumRewards?.ToDictionary() ?? [];
        if (rewardCopy.Keys.Any(value => !Enum.IsDefined(value)))
        {
            throw new ArgumentOutOfRangeException(nameof(diggingBroMinimumRewards));
        }

        Kind = kind;
        State = state;
        StartAdvance = startAdvance;
        EndAdvance = endAdvance;
        Game = game;
        LotoPrize = lotoPrize;
        Success = success;
        CramPrize = cramPrize;
        BonusOnly = bonusOnly;
        WattTraderSlotMinimum = wattTraderSlotMinimum;
        WattTraderSlotMaximum = wattTraderSlotMaximum;
        DiggingPaMinimumWatts = diggingPaMinimumWatts;
        DiggingBroMinimumTotal = diggingBroMinimumTotal;
        this.diggingBroMinimumRewards =
            new ReadOnlyDictionary<DiggingBroReward, byte>(rewardCopy);
        MenuClose = menuClose ?? SpecialToolMenuClose.None;
    }

    public SpecialToolKind Kind { get; }
    public RngState State { get; }
    public ulong StartAdvance { get; }
    public ulong EndAdvance { get; }
    public GameVersion Game { get; }
    public IReadOnlyList<string> LotoIds => lotoIds;
    public LotoPrizeFilter LotoPrize { get; }
    public SuccessFilter Success { get; }
    public IReadOnlyList<CramInputItem> CramInputs => cramInputs;
    public CramPrizeFilter CramPrize { get; }
    public bool BonusOnly { get; }
    public uint WattTraderSlotMinimum { get; }
    public uint WattTraderSlotMaximum { get; }
    public ulong DiggingPaMinimumWatts { get; }
    public int DiggingBroMinimumTotal { get; }
    public IReadOnlyDictionary<DiggingBroReward, byte> DiggingBroMinimumRewards =>
        diggingBroMinimumRewards;
    public SpecialToolMenuClose MenuClose { get; }
}

public sealed class SpecialToolFrame
{
    private readonly ReadOnlyDictionary<DiggingBroReward, byte> rewards;

    public SpecialToolFrame(
        SpecialToolKind kind,
        ulong advance,
        uint jump,
        char animation,
        RngState state,
        string? identifier = null,
        string? primaryResult = null,
        string? secondaryResult = null,
        bool? bonus = null,
        ulong? watts = null,
        int? total = null,
        bool? success = null,
        IReadOnlyDictionary<DiggingBroReward, byte>? rewards = null)
    {
        Kind = kind;
        Advance = advance;
        Jump = jump;
        Animation = animation;
        State = state;
        Identifier = identifier;
        PrimaryResult = primaryResult;
        SecondaryResult = secondaryResult;
        Bonus = bonus;
        Watts = watts;
        Total = total;
        Success = success;
        this.rewards = new ReadOnlyDictionary<DiggingBroReward, byte>(
            rewards?.ToDictionary() ?? []);
    }

    public SpecialToolKind Kind { get; }
    public ulong Advance { get; }
    public uint Jump { get; }
    public char Animation { get; }
    public RngState State { get; }
    public string? Identifier { get; }
    public string? PrimaryResult { get; }
    public string? SecondaryResult { get; }
    public bool? Bonus { get; }
    public ulong? Watts { get; }
    public int? Total { get; }
    public bool? Success { get; }
    public IReadOnlyDictionary<DiggingBroReward, byte> Rewards => rewards;
}

public interface ISpecialRngToolService
{
    Task<IReadOnlyList<SpecialToolFrame>> SearchAsync(
        SpecialToolSearchRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
