using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for shadow threshold effects on gameplay (#45).
    /// Covers: Dread T3 starting interest, T2 disadvantage, Denial T3 option removal,
    /// Fixation T3 forced stat, ShadowThresholds in DialogueContext, backward compatibility.
    /// Maturity: Prototype (happy-path tests).
    /// </summary>
    public class ShadowThresholdGameSessionTests
    {
        // ============== AC5: Dread T3 → Starting Interest 8 ==============

        [Fact]
        public async Task DreadT3_StartsAt8Interest()
        {
            var shadows = MakeShadowTracker(dread: 18);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows);

            var turn = await session.StartTurnAsync();
            Assert.Equal(8, turn.State.Interest);
        }

        [Fact]
        public async Task DreadT2_StartsAt10Interest()
        {
            // Dread at 12 (T2) should NOT change starting interest
            var shadows = MakeShadowTracker(dread: 12);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows);

            var turn = await session.StartTurnAsync();
            Assert.Equal(10, turn.State.Interest);
        }

        [Fact]
        public async Task ExplicitStartingInterest_OverridesDreadT3()
        {
            // If config has explicit StartingInterest, that takes priority
            var shadows = MakeShadowTracker(dread: 20);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                startingInterest: 5);

            var turn = await session.StartTurnAsync();
            Assert.Equal(5, turn.State.Interest);
        }

        // ============== AC2: T2 Disadvantage on paired stat ==============

        [Fact]
        public async Task DenialT2_HonestyRollsWithDisadvantage()
        {
            // Denial at 12 → Honesty has disadvantage → rolls twice, takes lower
            var shadows = MakeShadowTracker(denial: 12);
            // d20 rolls: first=18, second=5 (disadvantage takes 5), then ComputeDelay dice
            var session = MakeSession(
                diceValues: new[] { 18, 5, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Honesty, "Let me be honest...") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // With disadvantage, should use the lower roll (5)
            Assert.Equal(5, result.Roll.UsedDieRoll);
        }

        [Fact]
        public async Task DreadT2_WitRollsWithDisadvantage()
        {
            var shadows = MakeShadowTracker(dread: 14);
            // d20: first=19, second=3 → uses 3
            var session = MakeSession(
                diceValues: new[] { 19, 3, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Wit, "Witty remark") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(3, result.Roll.UsedDieRoll);
        }

        [Fact]
        public async Task MadnessT2_CharmRollsWithDisadvantage()
        {
            var shadows = MakeShadowTracker(madness: 15);
            // d20: first=17, second=4 → uses 4
            var session = MakeSession(
                diceValues: new[] { 17, 4, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Charm, "Hey there") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(4, result.Roll.UsedDieRoll);
        }

        [Fact]
        public async Task NoShadowDisadvantage_WhenBelowT2()
        {
            // Denial at 11 (T1) should NOT cause disadvantage
            var shadows = MakeShadowTracker(denial: 11);
            // Only one d20 roll needed (no disadvantage = single roll)
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Honesty, "Honestly...") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Should use the single roll value
            Assert.Equal(15, result.Roll.UsedDieRoll);
        }

        [Fact]
        public async Task UnpairedStat_NoDisadvantage_WhenOtherShadowAtT2()
        {
            // Denial at 12 affects Honesty, NOT Charm
            var shadows = MakeShadowTracker(denial: 12);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Charm, "Charming!") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Charm is not affected by Denial → single roll
            Assert.Equal(15, result.Roll.UsedDieRoll);
        }

        // ============== AC3: Denial T3 → Honesty options removed ==============

        [Fact]
        public async Task DenialT3_RemovesHonestyOptions()
        {
            var shadows = MakeShadowTracker(denial: 18);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llmOptions: new[]
                {
                    new DialogueOption(StatType.Charm, "Hi"),
                    new DialogueOption(StatType.Honesty, "Truth"),
                    new DialogueOption(StatType.Wit, "Clever")
                });

            var turn = await session.StartTurnAsync();

            // Honesty option should be removed
            Assert.Equal(2, turn.Options.Length);
            Assert.DoesNotContain(turn.Options, o => o.Stat == StatType.Honesty);
            Assert.Contains(turn.Options, o => o.Stat == StatType.Charm);
            Assert.Contains(turn.Options, o => o.Stat == StatType.Wit);
        }

        [Fact]
        public async Task DenialT3_AllHonesty_FallbackToChaos()
        {
            var shadows = MakeShadowTracker(denial: 19);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llmOptions: new[]
                {
                    new DialogueOption(StatType.Honesty, "Truth 1"),
                    new DialogueOption(StatType.Chaos, "Wild"),
                    new DialogueOption(StatType.Honesty, "Truth 2")
                });

            var turn = await session.StartTurnAsync();

            // Only the Chaos option should remain
            Assert.Single(turn.Options);
            Assert.Equal(StatType.Chaos, turn.Options[0].Stat);
        }

        [Fact]
        public async Task DenialT3_AllHonestyNoChaos_KeepsFirst()
        {
            var shadows = MakeShadowTracker(denial: 20);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llmOptions: new[]
                {
                    new DialogueOption(StatType.Honesty, "Truth 1"),
                    new DialogueOption(StatType.Honesty, "Truth 2"),
                    new DialogueOption(StatType.Honesty, "Truth 3")
                });

            var turn = await session.StartTurnAsync();

            // Fallback: keep first option (no Chaos available)
            Assert.Single(turn.Options);
            Assert.Equal("Truth 1", turn.Options[0].IntendedText);
        }

        // ============== AC4: Fixation T3 → Forced stat ==============

        [Fact]
        public async Task FixationT3_ForcesSameStatAsLastTurn()
        {
            var shadows = MakeShadowTracker(fixation: 18);
            // Turn 1: pick Charm (index 0). Turn 2: all options forced to Charm.
            // Dice: turn1 d20=15, delay=50, turn2 d20=15, delay=50
            var session = MakeSession(
                diceValues: new[] { 15, 50, 15, 50 },
                shadows: shadows,
                llmOptions: new[]
                {
                    new DialogueOption(StatType.Charm, "Hey"),
                    new DialogueOption(StatType.Wit, "Clever"),
                    new DialogueOption(StatType.Honesty, "Truth")
                });

            // Turn 1
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // picks Charm

            // Turn 2: all options should be forced to Charm
            var turn2 = await session.StartTurnAsync();
            Assert.All(turn2.Options, o => Assert.Equal(StatType.Charm, o.Stat));
        }

        [Fact]
        public async Task FixationT3_FirstTurn_NoForce()
        {
            var shadows = MakeShadowTracker(fixation: 20);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llmOptions: new[]
                {
                    new DialogueOption(StatType.Charm, "Hey"),
                    new DialogueOption(StatType.Wit, "Clever"),
                    new DialogueOption(StatType.Honesty, "Truth")
                });

            var turn = await session.StartTurnAsync();

            // First turn: no forced stat
            Assert.Equal(StatType.Charm, turn.Options[0].Stat);
            Assert.Equal(StatType.Wit, turn.Options[1].Stat);
            Assert.Equal(StatType.Honesty, turn.Options[2].Stat);
        }

        // ============== AC7: ShadowThresholds populated in DialogueContext ==============

        [Fact]
        public async Task ShadowThresholds_PopulatedInDialogueContext()
        {
            Dictionary<ShadowStatType, int>? capturedThresholds = null;
            var shadows = MakeShadowTracker(dread: 14, denial: 6, fixation: 0);

            var llm = new CapturingLlmAdapter(ctx =>
            {
                capturedThresholds = ctx.ShadowThresholds;
            });

            var session = MakeSessionWithLlm(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llm: llm);

            await session.StartTurnAsync();

            Assert.NotNull(capturedThresholds);
            Assert.Equal(2, capturedThresholds![ShadowStatType.Dread]);     // 14 → T2
            Assert.Equal(1, capturedThresholds[ShadowStatType.Denial]);     // 6 → T1
            Assert.Equal(0, capturedThresholds[ShadowStatType.Fixation]);   // 0 → T0
        }

        // ============== AC9: No effects when SessionShadowTracker is null ==============

        [Fact]
        public async Task NoShadowTracker_DefaultBehavior()
        {
            // No shadows configured → interest starts at 10, no disadvantage, no filtering
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: null);

            var turn = await session.StartTurnAsync();

            Assert.Equal(10, turn.State.Interest);
            // NullLlmAdapter returns 4 options
            Assert.Equal(4, turn.Options.Length);
        }

        [Fact]
        public async Task NoShadowTracker_DialogueContextHasNullThresholds()
        {
            Dictionary<ShadowStatType, int>? capturedThresholds = null;
            bool contextChecked = false;

            var llm = new CapturingLlmAdapter(ctx =>
            {
                capturedThresholds = ctx.ShadowThresholds;
                contextChecked = true;
            });

            var session = MakeSessionWithLlm(
                diceValues: new[] { 15, 50 },
                shadows: null,
                llm: llm);

            await session.StartTurnAsync();

            Assert.True(contextChecked);
            Assert.Null(capturedThresholds);
        }

        // ============== Edge: Multiple T2 simultaneous ==============

        [Fact]
        public async Task MultipleT2_BothStatsGetDisadvantage()
        {
            // Dread T2 → Wit disadvantage, Denial T2 → Honesty disadvantage
            var shadows = MakeShadowTracker(dread: 12, denial: 12);

            // Test Honesty: d20 first=19, second=2 → uses 2
            var session = MakeSession(
                diceValues: new[] { 19, 2, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Honesty, "Truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);
            Assert.Equal(2, result.Roll.UsedDieRoll);
        }

        // ============== Edge: Advantage and disadvantage cancel ==============

        [Fact]
        public async Task AdvantageAndShadowDisadvantage_Cancel()
        {
            // Interest in VeryIntoIt (grants advantage) + Denial T2 (Honesty disadvantage)
            // They cancel → normal single roll
            var shadows = MakeShadowTracker(denial: 14);
            var session = MakeSession(
                diceValues: new[] { 12, 50 },
                shadows: shadows,
                startingInterest: 18, // VeryIntoIt → advantage
                llmOptions: new[] { new DialogueOption(StatType.Honesty, "Truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // With adv+disadv canceling, should be a single roll = 12
            Assert.Equal(12, result.Roll.UsedDieRoll);
        }

        // ============ Helpers ============

        private static SessionShadowTracker MakeShadowTracker(
            int dread = 0, int denial = 0, int fixation = 0,
            int madness = 0, int overthinking = 0, int horniness = 0)
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 2 }, { StatType.Rizz, 2 }, { StatType.Honesty, 2 },
                    { StatType.Chaos, 2 }, { StatType.Wit, 2 }, { StatType.SelfAwareness, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Dread, dread }, { ShadowStatType.Denial, denial },
                    { ShadowStatType.Fixation, fixation }, { ShadowStatType.Madness, madness },
                    { ShadowStatType.Overthinking, overthinking }, { ShadowStatType.Horniness, horniness }
                });
            return new SessionShadowTracker(stats);
        }

        private static StatBlock MakeStatBlock(int allStats = 2, int allShadow = 0)
        {
            var stats = new Dictionary<StatType, int>
            {
                { StatType.Charm, allStats }, { StatType.Rizz, allStats }, { StatType.Honesty, allStats },
                { StatType.Chaos, allStats }, { StatType.Wit, allStats }, { StatType.SelfAwareness, allStats }
            };
            var shadow = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, allShadow }, { ShadowStatType.Horniness, allShadow },
                { ShadowStatType.Denial, allShadow }, { ShadowStatType.Fixation, allShadow },
                { ShadowStatType.Dread, allShadow }, { ShadowStatType.Overthinking, allShadow }
            };
            return new StatBlock(stats, shadow);
        }

        private static CharacterProfile MakeProfile(string name, StatBlock? stats = null)
        {
            stats = stats ?? MakeStatBlock();
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return new CharacterProfile(stats, "system prompt", name, timing, 1);
        }

        private static GameSession MakeSession(
            int[] diceValues,
            SessionShadowTracker? shadows,
            DialogueOption[]? llmOptions = null,
            int? startingInterest = null)
        {
            var config = new GameSessionConfig(
                playerShadows: shadows,
                startingInterest: startingInterest);

            ILlmAdapter llm = llmOptions != null
                ? new CustomOptionsLlmAdapter(llmOptions)
                : (ILlmAdapter)new NullLlmAdapter();

            var allDice = new int[diceValues.Length + 1];
            allDice[0] = 5;
            Array.Copy(diceValues, 0, allDice, 1, diceValues.Length);

            return new GameSession(
                MakeProfile("player"),
                MakeProfile("opponent"),
                llm,
                new SafeQueueDice(allDice),
                new NullTrapRegistry(),
                config);
        }

        private static GameSession MakeSessionWithLlm(
            int[] diceValues,
            SessionShadowTracker? shadows,
            ILlmAdapter llm)
        {
            var config = new GameSessionConfig(playerShadows: shadows);

            var allDice2 = new int[diceValues.Length + 1];
            allDice2[0] = 5;
            Array.Copy(diceValues, 0, allDice2, 1, diceValues.Length);

            return new GameSession(
                MakeProfile("player"),
                MakeProfile("opponent"),
                llm,
                new SafeQueueDice(allDice2),
                new NullTrapRegistry(),
                config);
        }

        /// <summary>Dice that returns values from a queue, falls back to 10.</summary>
        private sealed class SafeQueueDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            public SafeQueueDice(int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides) => _values.Count > 0 ? _values.Dequeue() : 10;
        }

        /// <summary>LLM adapter that returns fixed options.</summary>
        private sealed class CustomOptionsLlmAdapter : ILlmAdapter
        {
            private readonly DialogueOption[] _options;
            public CustomOptionsLlmAdapter(DialogueOption[] options) => _options = options;

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
                => Task.FromResult(_options);
            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
        }

        /// <summary>LLM adapter that captures DialogueContext for inspection.</summary>
        private sealed class CapturingLlmAdapter : ILlmAdapter
        {
            private readonly Action<DialogueContext> _onGetOptions;
            public CapturingLlmAdapter(Action<DialogueContext> onGetOptions) => _onGetOptions = onGetOptions;

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                _onGetOptions(context);
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Hey"),
                    new DialogueOption(StatType.Wit, "Clever")
                });
            }
            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
        }
    }
}
