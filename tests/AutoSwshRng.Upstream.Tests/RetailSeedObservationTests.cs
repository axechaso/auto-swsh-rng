using AutoSwshRng.Core.Rng;
using AutoSwshRng.Upstream;

namespace AutoSwshRng.Upstream.Tests;

public class RetailSeedObservationTests
{
    [Test]
    public async Task SolvedStateIsAfterThe128thBitAndPredictsAnIndependentSequence()
    {
        var seedService = new OwoowRetailSeedService();
        var anchor = new RngState(0x123456789ABCDEF0, owoow.Core.RNG.Util.XOROSHIRO_CONST);
        var calibration = await seedService.GenerateAnimationSequenceAsync(
            new AnimationSequenceRequest(anchor, 0, 128));
        var independent = await seedService.GenerateAnimationSequenceAsync(
            new AnimationSequenceRequest(anchor, 128, 32));
        var calibrationBits = ToBits(calibration.Observations);
        var independentBits = ToBits(independent.Observations);

        var solved = await seedService.SolveObservationAsync(
            new RetailSeedRequest(calibrationBits));
        var verified = await seedService.VerifyObservationsAsync(
            new AnimationVerificationRequest(solved.StateAfterObservations, independentBits));

        Assert.Multiple(() =>
        {
            Assert.That(solved.RawAlgorithmState, Is.EqualTo(independent.InitialState));
            Assert.That(solved.StateBeforeObservations, Is.EqualTo(anchor));
            Assert.That(solved.StateAfterObservations, Is.EqualTo(independent.InitialState));
            Assert.That(solved.ObservationCount, Is.EqualTo(128));
            Assert.That(
                solved.RawStateBoundary,
                Is.EqualTo(SeedObservationBoundary.StateAfterLastObservation));
            Assert.That(verified.Matches, Is.True);
            Assert.That(verified.FirstMismatchIndex, Is.EqualTo(-1));
            Assert.That(verified.StateAfterObservations, Is.EqualTo(Advance(anchor, 160)));
        });
    }

    [Test]
    public async Task ACorruptedSeedObservationFailsIndependentVerification()
    {
        var seedService = new OwoowRetailSeedService();
        var anchor = new RngState(0xF0123456789ABCDE, 0x123456789ABCDEF0);
        var calibration = await seedService.GenerateAnimationSequenceAsync(
            new AnimationSequenceRequest(anchor, 0, 128));
        var independent = await seedService.GenerateAnimationSequenceAsync(
            new AnimationSequenceRequest(anchor, 128, 64));
        var corrupted = calibration.Observations.ToArray();
        corrupted[64] ^= 1;

        var solved = await seedService.SolveObservationAsync(
            new RetailSeedRequest(ToBits(corrupted)));
        var verified = await seedService.VerifyObservationsAsync(
            new AnimationVerificationRequest(
                solved.StateAfterObservations,
                ToBits(independent.Observations)));

        Assert.That(verified.Matches, Is.False);
        Assert.That(verified.FirstMismatchIndex, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task RelocationIncludesTheLastStartAndKeepsOverlappingMatches()
    {
        var seedService = new OwoowRetailSeedService();
        var anchor = new RngState(0x3141592653589793, 0x2718281828459045);
        var source = await seedService.GenerateAnimationSequenceAsync(
            new AnimationSequenceRequest(anchor, 0, 4096));
        var (pattern, firstStart, lastStart) = FindOverlappingPattern(source.Observations);

        var result = await seedService.LocateAsync(
            new AnimationRelocationRequest(
                anchor,
                (ulong)firstStart,
                (ulong)lastStart,
                pattern));
        var expectedStarts = Enumerable
            .Range(firstStart, lastStart - firstStart + 1)
            .Where(start => Matches(source.Observations, start, pattern))
            .Select(start => (ulong)start)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(result.Complete, Is.True);
            Assert.That(result.WindowStart, Is.EqualTo((ulong)firstStart));
            Assert.That(result.WindowEnd, Is.EqualTo((ulong)lastStart));
            Assert.That(
                result.Candidates.Select(candidate => candidate.FirstObservedAdvance),
                Is.EqualTo(expectedStarts));
            Assert.That(expectedStarts, Does.Contain((ulong)firstStart));
            Assert.That(expectedStarts, Does.Contain((ulong)lastStart));
            Assert.That(
                result.Candidates.All(candidate =>
                    candidate.StateAfterObservedAdvance
                    == candidate.FirstObservedAdvance + (ulong)pattern.Length),
                Is.True);
        });

        foreach (var candidate in result.Candidates)
        {
            Assert.That(candidate.StateBeforeObservations, Is.EqualTo(Advance(anchor, candidate.FirstObservedAdvance)));
            Assert.That(candidate.StateAfterObservations, Is.EqualTo(Advance(anchor, candidate.StateAfterObservedAdvance)));
        }
    }

    [Test]
    public async Task BoundedDistanceRequiresAnExactStateMatchAndSupportsZero()
    {
        var positionService = new OwoowXoroshiroService();
        var start = new RngState(0x76543210FEDCBA98, 0x0123456789ABCDEF);
        var end = await positionService.AdvanceAsync(start, 3);
        var missing = await positionService.MeasureDistanceAsync(
            new RngDistanceRequest(start, end, 2));
        var found = await positionService.MeasureDistanceAsync(
            new RngDistanceRequest(start, end, 3));
        var unchanged = await positionService.MeasureDistanceAsync(
            new RngDistanceRequest(start, start, 0));
        var zeroAdvance = await positionService.AdvanceAsync(start, 0);

        Assert.Multiple(() =>
        {
            Assert.That(missing.Found, Is.False);
            Assert.That(missing.Distance, Is.Null);
            Assert.That(found.Found, Is.True);
            Assert.That(found.Distance, Is.EqualTo(3));
            Assert.That(found.VerifiedEndState, Is.EqualTo(end));
            Assert.That(unchanged.Found, Is.True);
            Assert.That(unchanged.Distance, Is.EqualTo(0));
            Assert.That(zeroAdvance, Is.EqualTo(start));
        });
    }

    private static string ToBits(IEnumerable<byte> observations) =>
        string.Concat(observations.Select(value => value.ToString()));

    private static RngState Advance(RngState state, ulong amount)
    {
        var result = owoow.Core.RNG.Util.XoroshiroJump(state.Seed0, state.Seed1, amount);
        return new RngState(result.s0, result.s1);
    }

    private static bool Matches(IReadOnlyList<byte> sequence, int start, string pattern)
    {
        for (var index = 0; index < pattern.Length; index++)
        {
            if (sequence[start + index] != pattern[index] - '0')
            {
                return false;
            }
        }

        return true;
    }

    private static (string Pattern, int First, int Second) FindOverlappingPattern(
        IReadOnlyList<byte> sequence)
    {
        for (var length = 3; length <= 24; length++)
        {
            var latestStartByPattern = new Dictionary<string, int>();
            for (var start = 128; start + length <= sequence.Count; start++)
            {
                var pattern = ToBits(sequence.Skip(start).Take(length));
                if (latestStartByPattern.TryGetValue(pattern, out var previous)
                    && start - previous < length)
                {
                    return (pattern, previous, start);
                }

                latestStartByPattern[pattern] = start;
            }
        }

        throw new AssertionException("Expected a repeated overlapping animation pattern.");
    }
}
