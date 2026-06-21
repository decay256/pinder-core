using System.Collections.Generic;
using System.IO;
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
    /// #1095 — Rule change: a "shadow trap" (a SUCCESSFUL main roll paired with a
    /// shadow check MISS that applies the shadow overlay) NO LONGER demotes the
    /// turn to a forced failure. Instead it TRUNCATES the positive interest delta
    /// to a maximum of 1 ("tainted, capped"). The roll verdict stays SUCCESS,
    /// momentum keeps incrementing, and success-gated downstream stays on the
    /// success path. Horniness halving (floor, delta>0) runs AFTER the shadow
    /// truncation, so shadow→1 then floor(1/2)=0 nets 0 but is STILL not a failure.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue1095_ShadowTrapTruncateTests
    {
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

        private static (GameSession session, ShadowTrapLlm llm) MakeSession(int horninessDie = 0)
        {
            var instructions = LoadYaml();
            var playerStats = MakeStats(allStats: 5, shadowOnPair: 10, pairStat: ShadowStatType.Despair);
            var player = MakeProfile("Sable", playerStats);
            var datee = MakeProfile("Brick", MakeStats(allStats: 0));

            // Game dice (consumed in order): horniness 1d10, main d20, timing.
            //  - horninessDie == 0  → SessionHorniness = 0 → horniness overlay does NOT
            //    fire (PeekAsync returns NotPerformed), isolating the pure shadow
            //    truncation (delta → 1). This is the default for the shadow-only cases.
            //  - horninessDie  > 0  → SessionHorniness > 0 → horniness overlay fires and
            //    halves the post-shadow delta (floor), used by the combined case.
            // main d20 = 20 (Nat 20 → guaranteed success), timing = 50.
            var dice = new FixedDice(horninessDie, 20, 50);
            // Steering RNG: steering roll 1 (fail steering), shadow roll 1 (miss → DC=10).
            var steeringRng = new FixedRandom(1, 1);

            var llm = new ShadowTrapLlm();
            var playerShadows = new SessionShadowTracker(playerStats);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                steeringRng: steeringRng,
                statDeliveryInstructions: instructions,
                playerShadows: playerShadows);
            var session = new GameSession(player, datee, llm, dice, new NullTrapRegistry(), config);
            return (session, llm);
        }

        /// <summary>
        /// Case 1: shadow trap on a success — a positive base delta (Nat 20 success
        /// → ≥ 4) is truncated to exactly 1. The roll verdict stays SUCCESS and the
        /// momentum streak increments through the trap turn. (No horniness here:
        /// SessionHorniness defaults to 0, so no overlay fires.)
        /// </summary>
        [Fact]
        public async Task ShadowTrap_OnSuccess_TruncatesToOne_VerdictSuccess_MomentumIncrements()
        {
            var (session, llm) = MakeSession();

            await session.StartTurnAsync();
            int rizzIdx = FindIndex(llm.LastOptions, StatType.Rizz);
            int momentumBefore = session.CreateSnapshot().MomentumStreak;
            var result = await session.ResolveTurnAsync(rizzIdx);
            int momentumAfter = session.CreateSnapshot().MomentumStreak;

            // Shadow trap actually fired.
            Assert.True(result.Roll.IsSuccess);
            Assert.True(result.ShadowCheck.OverlayApplied);
            Assert.True(llm.ShadowCorruptionCalled);

            // #1095: positive delta truncated to exactly 1 (no horniness → no further halving).
            Assert.Equal(1, result.InterestDelta);

            // #1095: base (pre-shadow) delta is the positive success delta and is ≥ 1.
            Assert.True(result.BaseInterestDelta >= 1,
                $"base success delta should be positive; got {result.BaseInterestDelta}");

            // #1095: verdict stays SUCCESS — NOT demoted to Miss.
            Assert.Equal(RollVerdict.Success, result.Roll.Check.FinalVerdict);
            Assert.Equal(FailureTier.Success, result.Roll.Check.FinalTier);

            // #1095: momentum streak keeps incrementing through the shadow-trap turn.
            Assert.Equal(momentumBefore + 1, momentumAfter);
        }

        /// <summary>
        /// Case 5: the #942 invariant (IsSuccess && BaseInterestDelta &lt; 0 ⇒ throw)
        /// is NOT tripped by a shadow trap — only the FINAL delta is truncated;
        /// BaseInterestDelta stays the positive success delta. Completing the turn
        /// without an InvariantViolationException proves the invariant holds.
        /// </summary>
        [Fact]
        public async Task ShadowTrap_OnSuccess_DoesNotTrip942Invariant()
        {
            var (session, llm) = MakeSession();

            await session.StartTurnAsync();
            int rizzIdx = FindIndex(llm.LastOptions, StatType.Rizz);

            // Must not throw InvariantViolationException (#942).
            var result = await session.ResolveTurnAsync(rizzIdx);

            Assert.True(result.Roll.IsSuccess);
            // #942: a success roll keeps a non-negative base interest delta.
            Assert.True(result.BaseInterestDelta >= 0,
                $"#942: success roll must keep BaseInterestDelta ≥ 0; got {result.BaseInterestDelta}");
            // The truncation lives only in the FINAL delta.
            Assert.Equal(1, result.InterestDelta);
        }

        /// <summary>
        /// Audit invariant: interest before + final delta == interest after, for the
        /// shadow-trap (capped) path. Guards the centrally-applied ShadowCorrection.
        /// </summary>
        [Fact]
        public async Task ShadowTrap_OnSuccess_InterestAuditBalances()
        {
            var (session, llm) = MakeSession();

            await session.StartTurnAsync();
            int rizzIdx = FindIndex(llm.LastOptions, StatType.Rizz);
            int interestBefore = session.CreateSnapshot().Interest;
            var result = await session.ResolveTurnAsync(rizzIdx);
            int interestAfter = session.CreateSnapshot().Interest;

            Assert.True(result.ShadowCheck.OverlayApplied);
            Assert.Equal(interestBefore + result.InterestDelta, interestAfter);
        }

        /// <summary>
        /// Case 2 (the headline worked example): shadow trap on a success FOLLOWED by
        /// horniness check. Base positive delta (Nat 20 → ≥ 4) is first truncated by
        /// the shadow trap to 1, then horniness appends a question (#1209, no penalty).
        /// Net final interest delta == 1. The turn is STILL NOT a failure: the roll
        /// verdict stays SUCCESS and the momentum streak still increments.
        /// (horninessDie = 5 → SessionHorniness = 5 → check misses.)
        /// </summary>
        [Fact]
        public async Task ShadowTrap_ThenHorniness_NetsOne_StillSuccess_MomentumIncrements()
        {
            var (session, llm) = MakeSession(horninessDie: 5);

            await session.StartTurnAsync();
            int rizzIdx = FindIndex(llm.LastOptions, StatType.Rizz);
            int momentumBefore = session.CreateSnapshot().MomentumStreak;
            var result = await session.ResolveTurnAsync(rizzIdx);
            int momentumAfter = session.CreateSnapshot().MomentumStreak;

            // Shadow trap fired AND horniness check missed.
            Assert.True(result.Roll.IsSuccess);
            Assert.True(result.ShadowCheck.OverlayApplied);
            Assert.True(result.HorninessCheck.IsMiss);

            // shadow → 1, horniness no penalty.
            Assert.Equal(1, result.InterestDelta);

            // Still a success: verdict NOT demoted to Miss.
            Assert.Equal(RollVerdict.Success, result.Roll.Check.FinalVerdict);
            Assert.Equal(FailureTier.Success, result.Roll.Check.FinalTier);

            // Base (pre-shadow) delta stays the positive success delta (≥ 1) — #942 holds.
            Assert.True(result.BaseInterestDelta >= 1,
                $"base success delta should be positive; got {result.BaseInterestDelta}");

            // Momentum streak still increments.
            Assert.Equal(momentumBefore + 1, momentumAfter);
        }

        private sealed class ShadowTrapLlm : ILlmAdapter, IStatefulLlmAdapter
        {
            public bool ShadowCorruptionCalled { get; private set; }
            public DialogueOption[] LastOptions { get; private set; } = System.Array.Empty<DialogueOption>();

            public Task<StatefulDateeResult> GetDateeResponseAsync(
                DateeContext context,
                IReadOnlyList<ConversationMessage> history,
                System.Threading.CancellationToken ct = default)
                => Task.FromResult(new StatefulDateeResult(
                    new DateeResponse("..."),
                    new ConversationMessage[]
                    {
                        ConversationMessage.User(string.Empty),
                        ConversationMessage.Assistant("..."),
                    }));

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
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


            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult(new DateeResponse("..."));

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult<string?>(null);

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
            {
                ShadowCorruptionCalled = true;
                return Task.FromResult(message + " [shadow:" + shadow + "]");
            }

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
                => Task.FromResult(message);

        public Task<string> GetSuccessImprovementAsync(SuccessImprovementContext context, CancellationToken ct = default) => Task.FromResult(context.DeliveredMessage);

            public Task<string> GetHorninessQuestionAsync(HorninessQuestionContext context, CancellationToken ct = default) => Task.FromResult("question?");
        public Task<string> GetSteeringQuestionAsync(SteeringContext context, System.Threading.CancellationToken ct = default)
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
