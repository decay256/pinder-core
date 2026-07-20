using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests;

[Trait("Category", "Core")]
public class Issue1247_HorninessAccountingAgreementTests
{
    private const int OverlayFiredSeed = 1;
    private const int OverlayNotFiredSeed = 0;

    private static StatDeliveryInstructions LoadDeliveryInstructions()
    {
        string dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 10; i++)
        {
            string candidate = Path.Combine(dir, "data", "delivery-instructions.yaml");
            if (File.Exists(candidate))
                return StatDeliveryInstructions.LoadFrom(File.ReadAllText(candidate));
            dir = Path.GetDirectoryName(dir)!;
            if (dir == null) break;
        }
        string fallback = Path.Combine("/root/.openclaw/workspace/pinder-core", "data", "delivery-instructions.yaml");
        return StatDeliveryInstructions.LoadFrom(File.ReadAllText(fallback));
    }

    private static CharacterProfile MakeProfile(string name, int allStats = 2)
    {
        return TestHelpers.MakeCharacterProfile(
            stats: TestHelpers.MakeStatBlock(allStats),
            assembledSystemPrompt: $"You are {name}.",
            displayName: name,
            timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
            level: 1);
    }

    private static GameSession MakeSession(
        int startingInterest,
        int sessionHorniness,
        int steeringSeed,
        StatDeliveryInstructions? instructions = null,
        int mainRoll = 15)
    {
        var dice = new FixedDice(sessionHorniness, mainRoll, 50);
        var shadows = new SessionShadowTracker(TestHelpers.MakeStatBlock());
        var rng = new Random(steeringSeed);
        var config = new GameSessionConfig(
            clock: TestHelpers.MakeClock(),
            playerShadows: shadows,
            steeringRng: rng,
            startingInterest: startingInterest,
            statDeliveryInstructions: instructions);

        return new GameSession(
            MakeProfile("Player"), MakeProfile("Datee"),
            new NullLlmAdapter(), dice, new NullTrapRegistry(), config);
    }

    [Fact]
    public async Task Overlay_Fires_PositiveInterest_FourWayAgreement()
    {
        var instructions = LoadDeliveryInstructions();
        int startingInterest = 14;
        var session = MakeSession(
            startingInterest: startingInterest,
            sessionHorniness: 15,
            steeringSeed: OverlayFiredSeed,
            instructions: instructions,
            mainRoll: 18); // Ensuring success and positive interest delta

        var turn = await session.StartTurnAsync();
        var result = await session.ResolveTurnAsync(0);

        Assert.True(result.HorninessCheck.OverlayApplied, "Expected horniness overlay to fire");

        // preHorninessDelta
        int preHorninessDelta = result.InterestDelta - result.HorninessInterestPenalty;
        Assert.True(preHorninessDelta > 0, "Expected a positive pre-horniness delta for this test");

        // Rule: Net interest applied is halved positive delta: floor(delta/2)
        int expectedPenalty = (int)Math.Floor(preHorninessDelta / 2.0) - preHorninessDelta;
        
        // (c) result.HorninessInterestPenalty is negative and equals expectedPenalty
        Assert.True(result.HorninessInterestPenalty < 0, "Penalty should be negative");
        Assert.Equal(expectedPenalty, result.HorninessInterestPenalty);

        // (a) result.InterestDelta == result.InterestBreakdown.Sum(x => x.Delta)
        int breakdownSum = result.InterestBreakdown.Sum(x => x.Delta);
        Assert.Equal(result.InterestDelta, breakdownSum);

        // (b) result.StateAfter.Interest == interestBefore + result.InterestDelta
        Assert.Equal(startingInterest + result.InterestDelta, result.StateAfter.Interest);

        // (d) result.InterestBreakdown contains an item with Source == "horniness_trope_trap"
        var horninessItem = result.InterestBreakdown.FirstOrDefault(x => x.Source == "horniness_trope_trap");
        Assert.NotNull(horninessItem);
        Assert.Equal(result.HorninessInterestPenalty, horninessItem.Delta);

        // (e) positive pre-horniness components sum to preHorninessDelta
        int preHorninessBreakdownSum = result.InterestBreakdown
            .Where(x => x.Source != "horniness_trope_trap")
            .Sum(x => x.Delta);
        Assert.Equal(preHorninessDelta, preHorninessBreakdownSum);
    }

    [Fact]
    public async Task Overlay_Fires_NonPositiveDelta_NoPenalty()
    {
        var instructions = LoadDeliveryInstructions();
        int startingInterest = 14;
        var session = MakeSession(
            startingInterest: startingInterest,
            sessionHorniness: 15,
            steeringSeed: OverlayFiredSeed,
            instructions: instructions,
            mainRoll: 10); // Low roll ensures negative/zero interest delta

        var turn = await session.StartTurnAsync();
        var result = await session.ResolveTurnAsync(0);

        Assert.True(result.HorninessCheck.OverlayApplied, "Expected overlay to fire");
        
        // Result shouldn't have positive preHorninessDelta
        int preHorninessDelta = result.InterestDelta - result.HorninessInterestPenalty;
        Assert.True(preHorninessDelta <= 0, "Expected a non-positive delta for this test");

        Assert.Equal(0, result.HorninessInterestPenalty);
        var horninessItem = result.InterestBreakdown.FirstOrDefault(x => x.Source == "horniness_trope_trap");
        Assert.Null(horninessItem);

        Assert.Equal(startingInterest + result.InterestDelta, result.StateAfter.Interest);
        Assert.Equal(result.InterestDelta, result.InterestBreakdown.Sum(x => x.Delta));
    }

    [Fact]
    public async Task Overlay_NotFired_ControlTurn_NoPenalty()
    {
        var instructions = LoadDeliveryInstructions();
        int startingInterest = 14;
        var session = MakeSession(
            startingInterest: startingInterest,
            sessionHorniness: 15,
            steeringSeed: OverlayNotFiredSeed,
            instructions: instructions,
            mainRoll: 18);

        var turn = await session.StartTurnAsync();
        var result = await session.ResolveTurnAsync(0);

        Assert.False(result.HorninessCheck.OverlayApplied, "Expected horniness overlay NOT to fire");

        Assert.Equal(0, result.HorninessInterestPenalty);
        var horninessItem = result.InterestBreakdown.FirstOrDefault(x => x.Source == "horniness_trope_trap");
        Assert.Null(horninessItem);

        Assert.Equal(startingInterest + result.InterestDelta, result.StateAfter.Interest);
        Assert.Equal(result.InterestDelta, result.InterestBreakdown.Sum(x => x.Delta));
    }
}
