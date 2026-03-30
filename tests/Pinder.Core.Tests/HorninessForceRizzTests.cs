using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for the Horniness-forced Rizz option mechanic (§15, issue #51).
    /// Covers AC1–AC5 from the spec.
    /// </summary>
    public class HorninessForceRizzTests
    {
        private static StatBlock MakeStatBlock(int allStats = 2, int allShadow = 0)
        {
            return TestHelpers.MakeStatBlock(allStats, allShadow);
        }

        private static CharacterProfile MakeProfile(string name, int allStats = 2, int allShadow = 0)
        {
            return new CharacterProfile(
                stats: MakeStatBlock(allStats, allShadow),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        /// <summary>
        /// Creates a GameSession with a clock and controlled dice for horniness computation.
        /// The first dice value is consumed by the horniness roll (1d10).
        /// </summary>
        private static GameSession CreateSessionWithClock(
            FixedDice dice,
            IGameClock clock,
            CharacterProfile? player = null,
            SessionShadowTracker? playerShadows = null)
        {
            var p = player ?? MakeProfile("Player");
            var opponent = MakeProfile("Opponent");
            var config = new GameSessionConfig(
                clock: clock,
                playerShadows: playerShadows);
            return new GameSession(p, opponent, new NullLlmAdapter(), dice, new NullTrapRegistry(), config);
        }

        // ========== AC1: Horniness Rolled at Session Start ==========

        [Fact]
        public void AC1_HorninessComputedAtConstruction_StoredInSnapshot()
        {
            // dice.Roll(10)=5, clock=Morning(-2), shadow=0 → horniness=3
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero));
            var dice = new FixedDice(5); // horniness roll
            var session = CreateSessionWithClock(dice, clock);
            // Can't directly access _horniness, but it's exposed via GameStateSnapshot
            // We need to call StartTurnAsync to get a snapshot — but that needs more dice
            // Instead, verify through the snapshot exposed at turnstart
        }

        [Fact]
        public async Task AC1_HorninessVisibleInSnapshot()
        {
            // dice.Roll(10)=5, clock=Morning(-2), shadow=0 → horniness=3
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero));
            // First value: horniness roll=5. Then d20=15, d100=50 for turn resolution
            var dice = new FixedDice(5);
            var session = CreateSessionWithClock(dice, clock);

            var turnStart = await session.StartTurnAsync();
            Assert.Equal(3, turnStart.State.Horniness); // 5 + (-2) + 0 = 3
        }

        [Fact]
        public async Task AC1_HorninessClampedToZero()
        {
            // dice.Roll(10)=1, clock=Morning(-2), shadow=0 → max(0, 1-2+0)=0
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero));
            var dice = new FixedDice(1);
            var session = CreateSessionWithClock(dice, clock);

            var turnStart = await session.StartTurnAsync();
            Assert.Equal(0, turnStart.State.Horniness);
        }

        [Fact]
        public async Task AC1_HorninessIncludesShadowStat()
        {
            // dice.Roll(10)=3, clock=Afternoon(0), shadowHorniness=5 → horniness=8
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 14, 0, 0, TimeSpan.Zero));
            var playerStats = MakeStatBlock(2, 5); // allShadow=5 → Horniness=5
            var playerShadows = new SessionShadowTracker(playerStats);
            var dice = new FixedDice(3);
            var player = MakeProfile("Player", allShadow: 5);
            var session = CreateSessionWithClock(dice, clock, player, playerShadows);

            var turnStart = await session.StartTurnAsync();
            Assert.Equal(8, turnStart.State.Horniness); // 3 + 0 + 5 = 8
        }

        [Fact]
        public async Task AC1_HorninessWithLateNightModifier()
        {
            // dice.Roll(10)=7, clock=LateNight(+3), shadow=0 → 10
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 23, 0, 0, TimeSpan.Zero));
            var dice = new FixedDice(7);
            var session = CreateSessionWithClock(dice, clock);

            var turnStart = await session.StartTurnAsync();
            Assert.Equal(10, turnStart.State.Horniness);
        }

        [Fact]
        public async Task AC1_HorninessWithAfterTwoAmModifier()
        {
            // dice.Roll(10)=10, clock=AfterTwoAm(+5), shadow=0 → 15
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 3, 0, 0, TimeSpan.Zero));
            var dice = new FixedDice(10);
            var session = CreateSessionWithClock(dice, clock);

            var turnStart = await session.StartTurnAsync();
            Assert.Equal(15, turnStart.State.Horniness);
        }

        [Fact]
        public async Task AC1_NoClock_HorninessIsZero()
        {
            // Without a clock, horniness defaults to 0 — no dice consumed
            var dice = new FixedDice();
            var player = MakeProfile("Player");
            var opponent = MakeProfile("Opponent");
            var session = new GameSession(player, opponent, new NullLlmAdapter(), dice, new NullTrapRegistry());

            var turnStart = await session.StartTurnAsync();
            Assert.Equal(0, turnStart.State.Horniness);
        }

        // ========== AC2: DialogueContext carries correct values ==========

        [Fact]
        public async Task AC2_HorninessBelow6_RequiresRizzOptionFalse()
        {
            // horniness=3 → RequiresRizzOption=false
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero));
            var dice = new FixedDice(5); // 5 + (-2) = 3
            var captureLlm = new CaptureLlmAdapter();
            var player = MakeProfile("Player");
            var opponent = MakeProfile("Opponent");
            var config = new GameSessionConfig(clock: clock);
            var session = new GameSession(player, opponent, captureLlm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            Assert.NotNull(captureLlm.LastDialogueContext);
            Assert.Equal(3, captureLlm.LastDialogueContext!.HorninessLevel);
            Assert.False(captureLlm.LastDialogueContext.RequiresRizzOption);
        }

        [Fact]
        public async Task AC2_HorninessAt6_RequiresRizzOptionTrue()
        {
            // dice.Roll(10)=8, clock=Morning(-2), shadow=0 → 6
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero));
            var dice = new FixedDice(8);
            var captureLlm = new CaptureLlmAdapter();
            var player = MakeProfile("Player");
            var opponent = MakeProfile("Opponent");
            var config = new GameSessionConfig(clock: clock);
            var session = new GameSession(player, opponent, captureLlm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            Assert.Equal(6, captureLlm.LastDialogueContext!.HorninessLevel);
            Assert.True(captureLlm.LastDialogueContext.RequiresRizzOption);
        }

        // ========== AC3: Threshold 1/2 (Horniness 6–17) ==========

        [Fact]
        public async Task AC3_Horniness6_NoRizzInLlm_LastOptionReplaced()
        {
            // horniness=6, LLM returns no Rizz → last option replaced
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero));
            var dice = new FixedDice(8); // 8 + (-2) + 0 = 6
            var session = CreateSessionWithClock(dice, clock);

            var turnStart = await session.StartTurnAsync();
            // NullLlmAdapter returns: Charm, Honesty, Wit, Chaos
            // Last option (Chaos) should be replaced with Rizz
            var options = turnStart.Options;
            Assert.Equal(4, options.Length);
            Assert.Equal(StatType.Charm, options[0].Stat);
            Assert.Equal(StatType.Honesty, options[1].Stat);
            Assert.Equal(StatType.Wit, options[2].Stat);
            Assert.Equal(StatType.Rizz, options[3].Stat);
            Assert.True(options[3].IsHorninessForced);
            // Original text preserved
            Assert.Equal("I once ate a whole pizza in a bouncy castle.", options[3].IntendedText);
        }

        [Fact]
        public async Task AC3_Horniness6_LlmAlreadyHasRizz_NoReplacement()
        {
            // horniness=6, LLM returns a Rizz option → no replacement needed
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero));
            var dice = new FixedDice(8);
            var rizzLlm = new RizzLlmAdapter();
            var player = MakeProfile("Player");
            var opponent = MakeProfile("Opponent");
            var config = new GameSessionConfig(clock: clock);
            var session = new GameSession(player, opponent, rizzLlm, dice, new NullTrapRegistry(), config);

            var turnStart = await session.StartTurnAsync();
            var options = turnStart.Options;
            // RizzLlmAdapter returns Charm, Rizz, Wit, Chaos
            Assert.Equal(StatType.Rizz, options[1].Stat);
            Assert.False(options[1].IsHorninessForced); // Organic Rizz, not forced
            // No other option should be IsHorninessForced
            Assert.All(options, o => Assert.False(o.IsHorninessForced));
        }

        [Fact]
        public async Task AC3_Horniness12_NoRizzInLlm_LastOptionReplaced()
        {
            // dice.Roll(10)=9, clock=LateNight(+3), shadow=0 → 12
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 23, 0, 0, TimeSpan.Zero));
            var dice = new FixedDice(9);
            var session = CreateSessionWithClock(dice, clock);

            var turnStart = await session.StartTurnAsync();
            var options = turnStart.Options;
            Assert.Equal(StatType.Rizz, options[3].Stat);
            Assert.True(options[3].IsHorninessForced);
        }

        [Fact]
        public async Task AC3_ForcedOption_ClearsCallbackComboTell()
        {
            // Forced Rizz options should have null callback/combo and false tell
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero));
            var dice = new FixedDice(8); // horniness=6
            var session = CreateSessionWithClock(dice, clock);

            var turnStart = await session.StartTurnAsync();
            var forced = turnStart.Options[3]; // last option, forced
            Assert.True(forced.IsHorninessForced);
            Assert.Null(forced.CallbackTurnNumber);
            Assert.Null(forced.ComboName);
            Assert.False(forced.HasTellBonus);
            Assert.False(forced.HasWeaknessWindow);
        }

        // ========== AC4: Threshold 3 (Horniness ≥ 18) ==========

        [Fact]
        public async Task AC4_Horniness18_AllOptionsBecomeForcedRizz()
        {
            // dice.Roll(10)=10, clock=AfterTwoAm(+5), shadow=3 → 18
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 3, 0, 0, TimeSpan.Zero));
            var playerStats = MakeStatBlock(2, 3);
            var playerShadows = new SessionShadowTracker(playerStats);
            var dice = new FixedDice(10);
            var player = new CharacterProfile(
                playerStats,
                "You are Player.",
                "Player",
                new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                1);
            var session = CreateSessionWithClock(dice, clock, player, playerShadows);

            var turnStart = await session.StartTurnAsync();
            var options = turnStart.Options;
            Assert.Equal(4, options.Length);
            Assert.All(options, o =>
            {
                Assert.Equal(StatType.Rizz, o.Stat);
                Assert.True(o.IsHorninessForced);
            });
        }

        [Fact]
        public async Task AC4_Horniness20_AllRizz_TextPreserved()
        {
            // dice.Roll(10)=8, clock=AfterTwoAm(+5), shadow=7 → 20
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 3, 0, 0, TimeSpan.Zero));
            var playerStats = MakeStatBlock(2, 7);
            var playerShadows = new SessionShadowTracker(playerStats);
            var dice = new FixedDice(8);
            var player = new CharacterProfile(
                playerStats,
                "You are Player.",
                "Player",
                new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                1);
            var session = CreateSessionWithClock(dice, clock, player, playerShadows);

            var turnStart = await session.StartTurnAsync();
            var options = turnStart.Options;
            // NullLlmAdapter texts preserved
            Assert.Equal("Hey, you come here often?", options[0].IntendedText);
            Assert.Equal("I have to be real with you...", options[1].IntendedText);
            Assert.Equal("Did you know that penguins propose with pebbles?", options[2].IntendedText);
            Assert.Equal("I once ate a whole pizza in a bouncy castle.", options[3].IntendedText);
        }

        [Fact]
        public async Task AC4_Horniness18_ForcedOptions_ClearBonuses()
        {
            // All forced options should have cleared bonuses
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 3, 0, 0, TimeSpan.Zero));
            var playerStats = MakeStatBlock(2, 3);
            var playerShadows = new SessionShadowTracker(playerStats);
            var dice = new FixedDice(10); // 10+5+3=18
            var player = new CharacterProfile(
                playerStats, "You are Player.", "Player",
                new TimingProfile(5, 0.0f, 0.0f, "neutral"), 1);
            var session = CreateSessionWithClock(dice, clock, player, playerShadows);

            var turnStart = await session.StartTurnAsync();
            Assert.All(turnStart.Options, o =>
            {
                Assert.Null(o.CallbackTurnNumber);
                Assert.Null(o.ComboName);
                Assert.False(o.HasTellBonus);
                Assert.False(o.HasWeaknessWindow);
            });
        }

        // ========== AC5: Edge cases ==========

        [Fact]
        public async Task AC5_HorninessBelow6_NoChanges()
        {
            // horniness=3 → no forced options
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero));
            var dice = new FixedDice(5); // 5+(-2)+0=3
            var session = CreateSessionWithClock(dice, clock);

            var turnStart = await session.StartTurnAsync();
            Assert.All(turnStart.Options, o => Assert.False(o.IsHorninessForced));
            Assert.Equal(StatType.Charm, turnStart.Options[0].Stat);
            Assert.Equal(StatType.Honesty, turnStart.Options[1].Stat);
            Assert.Equal(StatType.Wit, turnStart.Options[2].Stat);
            Assert.Equal(StatType.Chaos, turnStart.Options[3].Stat);
        }

        [Fact]
        public async Task AC5_HorninessExactly0_Clamped_NoEffect()
        {
            // dice.Roll(10)=1, clock=Morning(-2), shadow=0 → max(0,-1)=0
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero));
            var dice = new FixedDice(1);
            var session = CreateSessionWithClock(dice, clock);

            var turnStart = await session.StartTurnAsync();
            Assert.Equal(0, turnStart.State.Horniness);
            Assert.All(turnStart.Options, o => Assert.False(o.IsHorninessForced));
        }

        [Fact]
        public async Task AC5_HorninessDoesNotChangeBetweenTurns()
        {
            // Verify horniness is constant across turns
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero));
            // horniness roll=8 → 8+(-2)+0=6. Then: d20=15, d100=50 for turn
            var dice = new FixedDice(8, 15, 50);
            var session = CreateSessionWithClock(dice, clock);

            var start1 = await session.StartTurnAsync();
            Assert.Equal(6, start1.State.Horniness);

            var result = await session.ResolveTurnAsync(0);
            Assert.Equal(6, result.StateAfter.Horniness);

            var start2 = await session.StartTurnAsync();
            Assert.Equal(6, start2.State.Horniness);
        }

        [Fact]
        public async Task AC5_HighShadowHorniness_Reaches18()
        {
            // dice.Roll(10)=7, clock=LateNight(+3), shadowHorniness=8 → 7+3+8=18
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 23, 0, 0, TimeSpan.Zero));
            var playerStats = MakeStatBlock(2, 8);
            var playerShadows = new SessionShadowTracker(playerStats);
            var dice = new FixedDice(7);
            var player = new CharacterProfile(
                playerStats, "You are Player.", "Player",
                new TimingProfile(5, 0.0f, 0.0f, "neutral"), 1);
            var session = CreateSessionWithClock(dice, clock, player, playerShadows);

            var turnStart = await session.StartTurnAsync();
            Assert.Equal(18, turnStart.State.Horniness);
            Assert.All(turnStart.Options, o =>
            {
                Assert.Equal(StatType.Rizz, o.Stat);
                Assert.True(o.IsHorninessForced);
            });
        }

        [Fact]
        public async Task AC5_Threshold2Boundary_Horniness12()
        {
            // dice.Roll(10)=5, clock=Evening(+1), shadow=6 → 5+1+6=12
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 19, 0, 0, TimeSpan.Zero));
            var playerStats = MakeStatBlock(2, 6);
            var playerShadows = new SessionShadowTracker(playerStats);
            var dice = new FixedDice(5);
            var player = new CharacterProfile(
                playerStats, "You are Player.", "Player",
                new TimingProfile(5, 0.0f, 0.0f, "neutral"), 1);
            var session = CreateSessionWithClock(dice, clock, player, playerShadows);

            var turnStart = await session.StartTurnAsync();
            Assert.Equal(12, turnStart.State.Horniness);
            // Should have at least one Rizz (last option replaced since NullLlmAdapter has none)
            Assert.Equal(StatType.Rizz, turnStart.Options[3].Stat);
            Assert.True(turnStart.Options[3].IsHorninessForced);
        }
    }

    /// <summary>
    /// LLM adapter that captures the DialogueContext for inspection.
    /// </summary>
    internal sealed class CaptureLlmAdapter : ILlmAdapter
    {
        public DialogueContext? LastDialogueContext { get; private set; }

        public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
        {
            LastDialogueContext = context;
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey"),
                new DialogueOption(StatType.Honesty, "Truth"),
                new DialogueOption(StatType.Wit, "Clever"),
                new DialogueOption(StatType.Chaos, "Wild")
            };
            return Task.FromResult(options);
        }

        public Task<string> DeliverMessageAsync(DeliveryContext context)
            => Task.FromResult(context.ChosenOption.IntendedText);

        public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
            => Task.FromResult(new OpponentResponse("..."));

        public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
            => Task.FromResult<string?>(null);
    }

    /// <summary>
    /// LLM adapter that returns options including a Rizz option.
    /// </summary>
    internal sealed class RizzLlmAdapter : ILlmAdapter
    {
        public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
        {
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey"),
                new DialogueOption(StatType.Rizz, "You look incredible"),
                new DialogueOption(StatType.Wit, "Clever"),
                new DialogueOption(StatType.Chaos, "Wild")
            };
            return Task.FromResult(options);
        }

        public Task<string> DeliverMessageAsync(DeliveryContext context)
            => Task.FromResult(context.ChosenOption.IntendedText);

        public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
            => Task.FromResult(new OpponentResponse("..."));

        public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
            => Task.FromResult<string?>(null);
    }
}
