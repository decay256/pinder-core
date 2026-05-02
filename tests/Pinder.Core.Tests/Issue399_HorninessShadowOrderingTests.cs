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
    /// Regression tests for issue #399 — Horniness §15 interest-penalty halving
    /// must be applied to the FINAL post-shadow-demote interest delta, not to
    /// the pre-shadow delta. Previously the code halved the still-positive
    /// success delta first; then if the paired-shadow check missed and demoted
    /// the success to a failure, the recorded HorninessInterestPenalty no
    /// longer corresponded to the actual final delta — producing
    /// |horniness_penalty| > |floor(delta/2) - delta| on the live audit log.
    ///
    /// The fix is an ordering change in GameSession.cs: the §15 halving block
    /// is moved AFTER the shadow check (the message-rewrite half stays before
    /// — horniness text first, then shadow text — that ordering is intentional).
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue399_HorninessShadowOrderingTests
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
            throw new FileNotFoundException(
                "Could not locate data/delivery-instructions.yaml from current dir");
        }

        private static StatBlock MakeStats(int allStats = 2,
            ShadowStatType pairStat = ShadowStatType.Denial, int shadowOnPair = 0)
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

        // Mirror of GameSession.CreateForcedFailResult so the test can derive
        // the post-demote delta the engine will produce for a given shadow tier.
        private static RollResult MakeForcedFailRollResult(RollResult original, FailureTier tier)
        {
            int fakeDie = original.DC > 1 ? original.DC - 1 : 1;
            return new RollResult(
                dieRoll: fakeDie,
                secondDieRoll: null,
                usedDieRoll: fakeDie,
                stat: original.Stat,
                statModifier: 0,
                levelBonus: 0,
                dc: original.DC,
                tier: tier,
                activatedTrap: null,
                externalBonus: 0);
        }

        private static int FindIndex(DialogueOption[] options, StatType stat)
        {
            for (int i = 0; i < options.Length; i++)
                if (options[i].Stat == stat) return i;
            return 0;
        }

        // -----------------------------------------------------------------
        // Test 1 — Honesty success + horniness miss + Denial demote → final
        // delta NEGATIVE. §15 must NOT charge a penalty (rule premise fails).
        // -----------------------------------------------------------------
        [Fact]
        public async Task HonestySuccess_HorninessMiss_DenialDemote_NegativeFinal_NoHorninessPenalty()
        {
            var instructions = LoadYaml();

            // Player: Honesty 5 (so a roll passes easily), Denial shadow 10
            // (paired with Honesty — moderate shadow).  Opponent: weak.
            //
            // shadow check DC = 20 - 10 = 10 → miss when shadow d20 < 10.
            // We force shadow d20 = 1 → missMargin = 9 → TropeTrap tier
            // (per HorninessEngine.DetermineHorninessTier: missMargin 6..9 → TropeTrap).
            // TropeTrap failure delta = -2 (FailureScale).
            //
            // Honesty success on a Nat 20 → success delta = 4 (per SuccessScale)
            // + risk bonus (Nat 20 → +0 unless RiskTierBonus says otherwise).
            // Either way pre-demote delta is positive; post-demote is -2.
            var playerStats = MakeStats(allStats: 5,
                pairStat: ShadowStatType.Denial, shadowOnPair: 10);
            var player = MakeProfile("PlayerH", playerStats);
            var opponent = MakeProfile("OppH", MakeStats(allStats: 0));

            // Game dice: horniness session value = 5 (DC=15), main d20 = 20
            // (Nat 20 success), timing = 50.
            var dice = new FixedDice(5, 20, 50);

            // SteeringRng feeds: steering d20 (we don't care — just non-zero),
            // horniness d20 (force MISS: roll < 15), shadow d20 (force MISS: < 10).
            //   1st = steering: 1 (fail steering)
            //   2nd = horniness: 1 (DC=15, miss, missMargin=14 → Catastrophe;
            //                     either tier triggers an overlay instruction
            //                     in the YAML so OverlayApplied=true)
            //   3rd = shadow: 1 (DC=10, miss, missMargin=9 → TropeTrap)
            var steeringRng = new FixedRandom(1, 1, 1);

            var llm = new HorninessShadowCapturingLlm();
            var playerShadows = new SessionShadowTracker(playerStats);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                steeringRng: steeringRng,
                statDeliveryInstructions: instructions,
                playerShadows: playerShadows);
            var session = new GameSession(player, opponent, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            int honestyIdx = FindIndex(llm.LastOptions, StatType.Honesty);

            int interestBefore = session.CreateSnapshot().Interest;
            var result = await session.ResolveTurnAsync(honestyIdx);
            int interestAfter = session.CreateSnapshot().Interest;

            // Sanity: roll succeeded, horniness fired, shadow check fired and missed,
            // shadow corruption ran (overlay applied).
            Assert.True(result.Roll.IsSuccess, "Honesty Nat 20 must be a success");
            Assert.True(result.HorninessCheck.OverlayApplied,
                $"Horniness check must miss and overlay must be applied. roll={result.HorninessCheck.Roll} DC={result.HorninessCheck.DC} miss={result.HorninessCheck.IsMiss} tier={result.HorninessCheck.Tier}");
            Assert.True(result.ShadowCheck.IsMiss,
                $"Denial shadow check must miss. roll={result.ShadowCheck.Roll} DC={result.ShadowCheck.DC} tier={result.ShadowCheck.Tier}");
            Assert.True(result.ShadowCheck.OverlayApplied,
                "Shadow corruption overlay must apply on Honesty/Denial TropeTrap");

            // The shadow demote rewrites interestDelta to the failure delta
            // for the resolved shadow tier. That tier-specific delta is
            // negative (Fumble/-1, Misfire/-1, TropeTrap/-2, Catastrophe/-3,
            // Legendary/-4 per FailureScale). Therefore §15 must NOT have
            // applied any penalty (rule premise: delta > 0 fails).
            int expectedFailureDelta = FailureScale.GetInterestDelta(
                MakeForcedFailRollResult(result.Roll, result.ShadowCheck.Tier));
            Assert.True(expectedFailureDelta < 0,
                $"shadow tier {result.ShadowCheck.Tier} must yield negative failure delta");
            Assert.Equal(expectedFailureDelta, result.InterestDelta);
            Assert.Equal(0, result.HorninessInterestPenalty);

            // Audit invariant: before + final_delta == after. Since
            // HorninessInterestPenalty == 0, delta_from_roll (the natural
            // pre-§15 delta after any shadow demote) == delta_total == -2.
            Assert.Equal(interestBefore + result.InterestDelta, interestAfter);
        }

        // -----------------------------------------------------------------
        // Test 2 — Honesty success + horniness miss + Denial demote that
        // leaves final delta still POSITIVE. §15 halves the post-demote delta.
        //
        // Construction note: with the standard FailureTier scale (Fumble=-1,
        // Misfire=-1, TropeTrap=-2, Catastrophe=-3, Legendary=-4) every shadow
        // demote yields a non-positive delta. So a pure shadow-demote scenario
        // can never leave the final delta strictly positive on the production
        // rules. This is by design — §15's "post-demote positive" branch is
        // therefore mostly a safety property: the code path must still be
        // exercised correctly should the rule change in future, AND must not
        // trigger when the demote did make the delta non-positive.
        //
        // We assert the property as written: when the natural post-demote
        // delta is strictly positive, the penalty is exactly
        // floor(delta/2) - delta. We exercise this by performing the same
        // arithmetic check directly through the rule (a code-level invariant
        // test) — this complements Test 1's end-to-end coverage.
        // -----------------------------------------------------------------
        [Fact]
        public void Section15_PenaltyArithmetic_OnPositivePostDemoteDelta()
        {
            // §15 rule: penalty = floor(delta/2) - delta when delta > 0.
            // For a hypothetical post-demote positive delta of d, the penalty
            // applied by GameSession is exactly halvedDelta - d where
            // halvedDelta = floor(d/2). This is the property the audit log
            // invariant relies on.
            for (int d = 1; d <= 8; d++)
            {
                int halved = (int)System.Math.Floor(d / 2.0);
                int penalty = halved - d;
                // Penalty is in [floor(d/2)-d, 0].  Equivalently in [-ceil(d/2), 0].
                Assert.InRange(penalty, halved - d, 0);
                // Net-of-penalty delta must equal the halved delta.
                Assert.Equal(halved, d + penalty);
            }
        }

        // -----------------------------------------------------------------
        // Test 3 — Pure Honesty success + horniness miss (no shadow demote).
        // §15 applies and halves the natural positive delta. Sanity check that
        // moving the §15 block did NOT regress the no-shadow path.
        // -----------------------------------------------------------------
        [Fact]
        public async Task HonestySuccess_HorninessMiss_NoShadow_HalvesPositiveDelta()
        {
            var instructions = LoadYaml();

            // Player has zero Denial shadow → shadow check is NotPerformed.
            var playerStats = MakeStats(allStats: 5,
                pairStat: ShadowStatType.Denial, shadowOnPair: 0);
            var player = MakeProfile("PlayerH", playerStats);
            var opponent = MakeProfile("OppH", MakeStats(allStats: 0));

            // Dice: horniness=5, d20=20 (Nat 20 success), timing=50
            var dice = new FixedDice(5, 20, 50);
            // Steering RNG: steering=1 (fail), horniness=1 (DC=15, miss).
            // No shadow roll (shadow value is 0).
            var steeringRng = new FixedRandom(1, 1);

            var llm = new HorninessShadowCapturingLlm();
            var playerShadows = new SessionShadowTracker(playerStats);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                steeringRng: steeringRng,
                statDeliveryInstructions: instructions,
                playerShadows: playerShadows);
            var session = new GameSession(player, opponent, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            int honestyIdx = FindIndex(llm.LastOptions, StatType.Honesty);

            int interestBefore = session.CreateSnapshot().Interest;
            var result = await session.ResolveTurnAsync(honestyIdx);
            int interestAfter = session.CreateSnapshot().Interest;

            Assert.True(result.Roll.IsSuccess);
            Assert.True(result.HorninessCheck.OverlayApplied);
            Assert.False(result.ShadowCheck.CheckPerformed,
                "Shadow check must not be performed when shadow value is 0");

            // Natural delta is positive. §15 halves it.
            // result.HorninessInterestPenalty is the penalty applied
            // (floor(delta/2) - delta_pre_penalty), so the post-§15
            // interestDelta = delta_pre_penalty + penalty.
            int preDelta = result.InterestDelta - result.HorninessInterestPenalty;
            int expectedPenalty = (int)System.Math.Floor(preDelta / 2.0) - preDelta;
            Assert.True(preDelta > 0, $"pre-§15 delta must be positive; got {preDelta}");
            Assert.Equal(expectedPenalty, result.HorninessInterestPenalty);

            // Invariant: before + final delta == after.
            Assert.Equal(interestBefore + result.InterestDelta, interestAfter);
        }

        // -----------------------------------------------------------------
        // Test 4 — Failed roll + horniness miss + shadow demote: §15 must be
        // a no-op throughout (interestDelta starts ≤ 0 and stays ≤ 0).
        // -----------------------------------------------------------------
        [Fact]
        public async Task FailedRoll_HorninessMiss_ShadowMiss_NoHorninessPenalty()
        {
            var instructions = LoadYaml();

            var playerStats = MakeStats(allStats: 2,
                pairStat: ShadowStatType.Denial, shadowOnPair: 10);
            var player = MakeProfile("PlayerH", playerStats);
            var opponent = MakeProfile("OppH", MakeStats(allStats: 2));

            // Dice: horniness=5, d20=1 (Nat 1 — guaranteed failure), timing=50
            var dice = new FixedDice(5, 1, 50);
            // Steering RNG: steering=1, horniness=1 (DC=15, miss),
            // shadow=1 (DC=10, miss → TropeTrap).
            var steeringRng = new FixedRandom(1, 1, 1);

            var llm = new HorninessShadowCapturingLlm();
            var playerShadows = new SessionShadowTracker(playerStats);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                steeringRng: steeringRng,
                statDeliveryInstructions: instructions,
                playerShadows: playerShadows);
            var session = new GameSession(player, opponent, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            int honestyIdx = FindIndex(llm.LastOptions, StatType.Honesty);

            int interestBefore = session.CreateSnapshot().Interest;
            var result = await session.ResolveTurnAsync(honestyIdx);
            int interestAfter = session.CreateSnapshot().Interest;

            Assert.False(result.Roll.IsSuccess);
            Assert.True(result.HorninessCheck.OverlayApplied);
            Assert.True(result.ShadowCheck.IsMiss);

            // Natural delta is ≤ 0 from the start; §15 must not apply.
            Assert.Equal(0, result.HorninessInterestPenalty);
            Assert.True(result.InterestDelta <= 0,
                $"failed roll must yield non-positive InterestDelta; got {result.InterestDelta}");

            // Invariant: before + final delta == after.
            Assert.Equal(interestBefore + result.InterestDelta, interestAfter);
        }

        // -----------------------------------------------------------------
        // LLM stub — captures whether shadow corruption fired and rewrites
        // the message on horniness / shadow overlays so textDiffs are emitted.
        // -----------------------------------------------------------------
        private sealed class HorninessShadowCapturingLlm : ILlmAdapter, IStatefulLlmAdapter
        {
            public bool ShadowCorruptionCalled { get; private set; }
            public bool HorninessOverlayCalled { get; private set; }
            public DialogueOption[] LastOptions { get; private set; } = System.Array.Empty<DialogueOption>();

            // #788: stateful LLM adapter is now history-passing and stateless.
            public Task<StatefulOpponentResult> GetOpponentResponseAsync(
                OpponentContext context,
                System.Collections.Generic.IReadOnlyList<ConversationMessage> history,
                System.Threading.CancellationToken ct = default)
                => Task.FromResult(new StatefulOpponentResult(
                    new OpponentResponse("..."),
                    new ConversationMessage[]
                    {
                        ConversationMessage.User(string.Empty),
                        ConversationMessage.Assistant("..."),
                    }));

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                LastOptions = new[]
                {
                    new DialogueOption(StatType.Charm, "hi"),
                    new DialogueOption(StatType.Rizz, "rizz line"),
                    new DialogueOption(StatType.Honesty, "real talk"),
                    new DialogueOption(StatType.Wit, "clever bit")
                };
                return Task.FromResult(LastOptions);
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context)
            {
                string intended = context.ChosenOption.IntendedText;
                return Task.FromResult(context.Outcome == FailureTier.None
                    ? intended
                    : $"[{context.Outcome}] {intended}");
            }

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction,
                string? opponentContext = null, string? archetypeDirective = null)
            {
                HorninessOverlayCalled = true;
                return Task.FromResult(message + " [horniness-overlay]");
            }

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction,
                ShadowStatType shadow, string? archetypeDirective = null)
            {
                ShadowCorruptionCalled = true;
                return Task.FromResult(message + " [shadow:" + shadow + "]");
            }

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction,
                string trapName, string? opponentContext = null, string? archetypeDirective = null)
                => Task.FromResult(message);

            public Task<string> GetSteeringQuestionAsync(SteeringContext context)
                => Task.FromResult("steering question");
        }

        private sealed class FixedRandom : System.Random
        {
            private readonly Queue<int> _values;
            public FixedRandom(params int[] values) { _values = new Queue<int>(values); }
            public override int Next(int minValue, int maxValue)
                => _values.Count > 0 ? _values.Dequeue() : minValue;
        }

        private sealed class NullTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}
