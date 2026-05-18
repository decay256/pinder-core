using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for #927 — <see cref="RollCheckResult.FinalVerdict"/> +
    /// <see cref="RollCheckResult.FinalTier"/> as the engine-side single source
    /// of truth for the post-shadow-corruption verdict.
    ///
    /// Existing <see cref="RollCheckResult.IsSuccess"/> / <see cref="RollCheckResult.Tier"/>
    /// keep pre-demotion semantics (back-compat is load-bearing); the new fields
    /// reflect any in-engine override (shadow-corruption demotion).
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue927_FinalVerdictTierTests
    {
        // ─── Unit-level: defaults + ApplyFinalOverride on RollCheckResult ────

        [Fact]
        public void PlainSuccess_DefaultsToSuccessFinalAndNoTier()
        {
            // d20=15 + mods=4 vs DC=12 → success
            var check = RollCheckResult.Synthesise(
                dieRoll: 15, secondDieRoll: null, usedDieRoll: 15,
                statModifier: 3, levelBonus: 1, dc: 12);

            Assert.True(check.IsSuccess);
            Assert.Equal(FailureTier.Success, check.Tier);

            // #927: defaults mirror the pre-shadow outcome.
            Assert.Equal(RollVerdict.Success, check.FinalVerdict);
            Assert.Equal(FailureTier.Success, check.FinalTier);
        }

        [Fact]
        public void PlainMiss_DefaultsToMissFinalAndPreservesTier()
        {
            // d20=4 + mods=1 vs DC=15 → miss by 10 (Catastrophe)
            var check = RollCheckResult.Synthesise(
                dieRoll: 4, secondDieRoll: null, usedDieRoll: 4,
                statModifier: 0, levelBonus: 1, dc: 15);

            Assert.False(check.IsSuccess);
            Assert.Equal(FailureTier.Catastrophe, check.Tier);

            // #927: defaults mirror the pre-shadow outcome.
            Assert.Equal(RollVerdict.Miss, check.FinalVerdict);
            Assert.Equal(FailureTier.Catastrophe, check.FinalTier);
        }

        [Fact]
        public void ApplyFinalOverride_DemotesSuccessToMissWithTier_PreservesBackCompatFields()
        {
            // Plain success: d20=20 + 0 vs DC=10
            var check = RollCheckResult.Synthesise(
                dieRoll: 20, secondDieRoll: null, usedDieRoll: 20,
                statModifier: 0, levelBonus: 0, dc: 10);

            Assert.True(check.IsSuccess);
            Assert.Equal(FailureTier.Success, check.Tier);

            // Simulate the shadow-corruption demotion that GameSession performs.
            check.ApplyFinalOverride(RollVerdict.Miss, FailureTier.Catastrophe);

            // Pre-shadow fields MUST NOT change (back-compat is load-bearing).
            Assert.True(check.IsSuccess);
            Assert.Equal(FailureTier.Success, check.Tier);

            // Post-override fields reflect the demotion.
            Assert.Equal(RollVerdict.Miss, check.FinalVerdict);
            Assert.Equal(FailureTier.Catastrophe, check.FinalTier);
        }

        [Fact]
        public void Serialisation_EmitsSnakeCaseFinalVerdictAndFinalTier()
        {
            var check = RollCheckResult.Synthesise(
                dieRoll: 20, secondDieRoll: null, usedDieRoll: 20,
                statModifier: 0, levelBonus: 0, dc: 10);
            check.ApplyFinalOverride(RollVerdict.Miss, FailureTier.TropeTrap);

            string json = JsonSerializer.Serialize(check);

            Assert.Contains("\"final_verdict\":\"Miss\"", json);
            Assert.Contains("\"final_tier\":\"TropeTrap\"", json);
        }

        // ─── Integration-level: GameSession shadow-corruption populates fields ─

        [Fact]
        public async Task GameSession_SuccessDemotedByShadow_PopulatesFinalMissAndFinalTier()
        {
            // Mirrors Issue365_ShadowOnFailedRollTests.SuccessRoll_ShadowMiss…
            // — d20=20 success demoted by shadow miss.
            var instructions = LoadYaml();

            var playerStats = MakeStats(allStats: 5, shadowOnPair: 10, pairStat: ShadowStatType.Despair);
            var player = MakeProfile("Sable", playerStats);
            var opponent = MakeProfile("Brick", MakeStats(allStats: 0));

            // horniness=5, main d20=20 (auto-success / Nat 20), timing=50
            var dice = new FixedDice(5, 20, 50);
            // Steering RNG: steering roll 1 (fail), shadow roll 1 (miss)
            var steeringRng = new FixedRandom(1, 1);

            var llm = new ShadowCapturingLlm();
            var playerShadows = new SessionShadowTracker(playerStats);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                steeringRng: steeringRng,
                statDeliveryInstructions: instructions,
                playerShadows: playerShadows);
            var session = new GameSession(player, opponent, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            int rizzIdx = FindIndex(llm.LastOptions, StatType.Rizz);
            var result = await session.ResolveTurnAsync(rizzIdx);

            // Pre-demotion fields stay TRUE / Success (back-compat).
            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(FailureTier.Success, result.Roll.Tier);
            Assert.True(result.Roll.Check.IsSuccess);
            Assert.Equal(FailureTier.Success, result.Roll.Check.Tier);

            // Shadow overlay fired.
            Assert.True(result.ShadowCheck.OverlayApplied);

            // #927: FinalVerdict / FinalTier on the option-roll's Check reflect
            // the post-shadow demotion. The shadow tier is whatever the shadow
            // ladder produced for missMargin=9 (DC=10 - roll=1 = 9 → TropeTrap).
            Assert.Equal(RollVerdict.Miss, result.Roll.Check.FinalVerdict);
            Assert.Equal(result.ShadowCheck.Tier, result.Roll.Check.FinalTier);
            Assert.NotEqual(FailureTier.Success, result.Roll.Check.FinalTier);
        }

        [Fact]
        public async Task GameSession_FailedRoll_ShadowMiss_FinalVerdictRemainsOriginalMiss()
        {
            // Failed main roll + shadow miss → overlay fires but final verdict
            // already a miss before shadow; FinalVerdict stays Miss, FinalTier
            // stays the ORIGINAL pre-shadow tier (the demotion override only
            // converts success → miss; it does not retier an already-failed roll).
            var instructions = LoadYaml();

            var playerStats = MakeStats(allStats: 2, shadowOnPair: 10, pairStat: ShadowStatType.Despair);
            var player = MakeProfile("Sable", playerStats);
            var opponent = MakeProfile("Brick", MakeStats(allStats: 2));

            // d20=1 (Nat 1 — guaranteed failure)
            var dice = new FixedDice(5, 1, 50);
            var steeringRng = new FixedRandom(1, 1);

            var llm = new ShadowCapturingLlm();
            var playerShadows = new SessionShadowTracker(playerStats);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                steeringRng: steeringRng,
                statDeliveryInstructions: instructions,
                playerShadows: playerShadows);
            var session = new GameSession(player, opponent, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            int rizzIdx = FindIndex(llm.LastOptions, StatType.Rizz);
            var result = await session.ResolveTurnAsync(rizzIdx);

            // Main roll failed.
            Assert.False(result.Roll.IsSuccess);
            // Shadow overlay fired on the failed roll.
            Assert.True(result.ShadowCheck.OverlayApplied);

            // #927: pre-shadow miss → FinalVerdict stays Miss; FinalTier stays
            // whatever the option-roll's pre-shadow tier was on the Check.
            Assert.Equal(RollVerdict.Miss, result.Roll.Check.FinalVerdict);
            Assert.Equal(result.Roll.Check.Tier, result.Roll.Check.FinalTier);
        }

        [Fact]
        public async Task GameSession_NoShadowOverlay_FinalVerdictMatchesPreShadow()
        {
            // Player has NO paired shadow active → no shadow corruption can fire.
            // FinalVerdict / FinalTier should mirror the original outcome.
            var instructions = LoadYaml();

            var playerStats = MakeStats(allStats: 5, shadowOnPair: 0, pairStat: ShadowStatType.Despair);
            var player = MakeProfile("Sable", playerStats);
            var opponent = MakeProfile("Brick", MakeStats(allStats: 0));

            // d20=20 (auto-success)
            var dice = new FixedDice(5, 20, 50);
            var steeringRng = new FixedRandom(1, 1);

            var llm = new ShadowCapturingLlm();
            var playerShadows = new SessionShadowTracker(playerStats);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                steeringRng: steeringRng,
                statDeliveryInstructions: instructions,
                playerShadows: playerShadows);
            var session = new GameSession(player, opponent, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            int rizzIdx = FindIndex(llm.LastOptions, StatType.Rizz);
            var result = await session.ResolveTurnAsync(rizzIdx);

            Assert.True(result.Roll.IsSuccess);
            Assert.False(result.ShadowCheck.OverlayApplied);

            // #927: no demotion → defaults stand.
            Assert.Equal(RollVerdict.Success, result.Roll.Check.FinalVerdict);
            Assert.Equal(FailureTier.Success, result.Roll.Check.FinalTier);
        }

        [Fact]
        public async Task GameSession_MultipleTurns_OnlyTurnWithOverlayHasFinalDemoted()
        {
            // Run two consecutive turns. Turn 1: success with no shadow.
            // Turn 2: success demoted by shadow. The two turns' RollCheckResult
            // instances are independent — only turn 2 has FinalVerdict=Miss.
            var instructions = LoadYaml();

            // Player starts WITHOUT shadow; we'll bump shadow between turns.
            var playerStats = MakeStats(allStats: 5, shadowOnPair: 0, pairStat: ShadowStatType.Despair);
            var player = MakeProfile("Sable", playerStats);
            var opponent = MakeProfile("Brick", MakeStats(allStats: 0));

            // Turn 1 dice: horniness=5, d20=20, timing=50.
            // Turn 2 dice: horniness=5, d20=20, timing=50.
            var dice = new FixedDice(5, 20, 50, 5, 20, 50);
            // Turn 1 steering=1 (no shadow → only steering called once).
            // Turn 2 steering=1, shadow d20=1.
            var steeringRng = new FixedRandom(1, 1, 1);

            var llm = new ShadowCapturingLlm();
            var playerShadows = new SessionShadowTracker(playerStats);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                steeringRng: steeringRng,
                statDeliveryInstructions: instructions,
                playerShadows: playerShadows);
            var session = new GameSession(player, opponent, llm, dice, new NullTrapRegistry(), config);

            // Turn 1.
            await session.StartTurnAsync();
            int rizzIdx1 = FindIndex(llm.LastOptions, StatType.Rizz);
            var turn1 = await session.ResolveTurnAsync(rizzIdx1);

            Assert.True(turn1.Roll.IsSuccess);
            Assert.False(turn1.ShadowCheck.OverlayApplied);
            Assert.Equal(RollVerdict.Success, turn1.Roll.Check.FinalVerdict);
            Assert.Equal(FailureTier.Success, turn1.Roll.Check.FinalTier);

            // Bump shadow so turn 2 has an active paired shadow.
            playerShadows.ApplyGrowth(ShadowStatType.Despair, +10, "test setup");

            // Turn 2.
            await session.StartTurnAsync();
            int rizzIdx2 = FindIndex(llm.LastOptions, StatType.Rizz);
            var turn2 = await session.ResolveTurnAsync(rizzIdx2);

            Assert.True(turn2.Roll.IsSuccess);
            Assert.True(turn2.ShadowCheck.OverlayApplied);
            Assert.Equal(RollVerdict.Miss, turn2.Roll.Check.FinalVerdict);
            Assert.Equal(turn2.ShadowCheck.Tier, turn2.Roll.Check.FinalTier);

            // Turn 1's Check is independent — was NOT retroactively mutated.
            Assert.Equal(RollVerdict.Success, turn1.Roll.Check.FinalVerdict);
            Assert.Equal(FailureTier.Success, turn1.Roll.Check.FinalTier);
        }

        // ─── Fixtures (copied from Issue365_ShadowOnFailedRollTests) ──────────

        private static StatDeliveryInstructions LoadYaml()
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
            string fallback = "/tmp/work-W1b/pinder-core/data/delivery-instructions.yaml";
            return StatDeliveryInstructions.LoadFrom(File.ReadAllText(fallback));
        }

        private static StatBlock MakeStats(int allStats = 2, int shadowOnPair = 0, ShadowStatType pairStat = ShadowStatType.Despair)
        {
            var stats = new Dictionary<StatType, int>
            {
                { StatType.Charm, allStats },
                { StatType.Rizz, allStats },
                { StatType.Honesty, allStats },
                { StatType.Chaos, allStats },
                { StatType.Wit, allStats },
                { StatType.SelfAwareness, allStats }
            };
            var shadow = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };
            shadow[pairStat] = shadowOnPair;
            return new StatBlock(stats, shadow);
        }

        private static CharacterProfile MakeProfile(string name, StatBlock stats)
        {
            return new CharacterProfile(
                stats, $"You are {name}.", name,
                new TimingProfile(5, 0.0f, 0.0f, "neutral"), level: 1);
        }

        private static int FindIndex(DialogueOption[] options, StatType stat)
        {
            for (int i = 0; i < options.Length; i++)
                if (options[i].Stat == stat) return i;
            return 0;
        }

        private sealed class ShadowCapturingLlm : ILlmAdapter, IStatefulLlmAdapter
        {
            public bool ShadowCorruptionCalled { get; private set; }
            public DialogueOption[] LastOptions { get; private set; } = System.Array.Empty<DialogueOption>();

            public Task<StatefulOpponentResult> GetOpponentResponseAsync(
                OpponentContext context,
                IReadOnlyList<ConversationMessage> history,
                CancellationToken ct = default)
                => Task.FromResult(new StatefulOpponentResult(
                    new OpponentResponse("..."),
                    new ConversationMessage[]
                    {
                        ConversationMessage.User(string.Empty),
                        ConversationMessage.Assistant("..."),
                    }));

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
            {
                LastOptions = new[]
                {
                    new DialogueOption(StatType.Charm, "Hey there"),
                    new DialogueOption(StatType.Rizz, "Nice vibes"),
                    new DialogueOption(StatType.Wit, "Clever remark"),
                    new DialogueOption(StatType.Honesty, "Real talk")
                };
                return Task.FromResult(LastOptions);
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context, CancellationToken ct = default)
            {
                string intended = context.ChosenOption.IntendedText;
                return Task.FromResult(context.Outcome == FailureTier.Success
                    ? intended
                    : $"[{context.Outcome}] {intended}");
            }

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context, CancellationToken ct = default)
                => Task.FromResult(new OpponentResponse("..."));

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default)
                => Task.FromResult<string?>(null);

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default)
            {
                ShadowCorruptionCalled = true;
                return Task.FromResult(message + " [shadow:" + shadow + "]");
            }

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> GetSteeringQuestionAsync(SteeringContext context, CancellationToken ct = default)
                => Task.FromResult("steering question");
        }

        private sealed class FixedRandom : System.Random
        {
            private readonly Queue<int> _values;
            public FixedRandom(params int[] values) { _values = new Queue<int>(values); }
            public override int Next(int minValue, int maxValue) => _values.Count > 0 ? _values.Dequeue() : minValue;
        }

        private sealed class NullTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}
