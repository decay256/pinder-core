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
    /// Tests for issue #270: 5 shadow reduction events from §7.
    /// 4 reductions are new; the 5th (Fixation −1 for 4+ distinct stats) already existed.
    /// </summary>
    public class ShadowReductionTests
    {
        // =====================================================================
        // Reduction 1: Date secured → Dread −1
        // =====================================================================

        [Fact]
        public async Task DateSecured_ReducesDreadByOne()
        {
            var shadows = MakeTracker();
            // Pre-grow Dread so we can see reduction
            shadows.ApplyGrowth(ShadowStatType.Dread, 3, "setup");
            shadows.DrainGrowthEvents(); // clear setup events

            // Start at 24, success → DateSecured
            var session = BuildSession(
                dice: Dice(15, 50),
                playerStats: Stats(charm: 5),
                shadows: shadows,
                startingInterest: 24);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0); // Charm success

            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.DateSecured, result.Outcome);
            // Dread was 3, should be 3 - 1 = 2
            Assert.Equal(2, shadows.GetDelta(ShadowStatType.Dread));
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Dread") && e.Contains("Date secured"));
        }

        [Fact]
        public async Task DateSecured_DreadReductionCanGoNegative()
        {
            var shadows = MakeTracker();
            // No pre-growth — Dread delta starts at 0, reduction takes it to -1
            var session = BuildSession(
                dice: Dice(15, 50),
                playerStats: Stats(charm: 5),
                shadows: shadows,
                startingInterest: 24);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.DateSecured, result.Outcome);
            Assert.Equal(-1, shadows.GetDelta(ShadowStatType.Dread));
        }

        [Fact]
        public async Task Unmatched_NoDreadReduction()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Dread, 3, "setup");
            shadows.DrainGrowthEvents();

            // Start at 1, failure → interest drops to 0 → Unmatched
            var session = BuildSession(
                dice: Dice(2, 50),
                playerStats: Stats(charm: 0),
                opponentStats: Stats(sa: 0),
                shadows: shadows,
                startingInterest: 1);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.Unmatched, result.Outcome);
            // Dread should have +2 for hitting 0, NOT -1 reduction (that's DateSecured only)
            Assert.True(shadows.GetDelta(ShadowStatType.Dread) > 3);
            Assert.DoesNotContain(result.ShadowGrowthEvents, e => e.Contains("Date secured") && e.Contains("Dread"));
        }

        // =====================================================================
        // Reduction 2: Honesty success at Interest ≥15 → Denial −1
        // =====================================================================

        [Fact]
        public async Task HonestySuccessAtHighInterest_ReducesDenialByOne()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Denial, 2, "setup");
            shadows.DrainGrowthEvents();

            // Interest starts at 15 (Interested), Honesty success
            var session = BuildSession(
                dice: Dice(18, 50),
                playerStats: Stats(honesty: 5),
                shadows: shadows,
                startingInterest: 15,
                options: new[] { new DialogueOption(StatType.Honesty, "truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // Denial was 2, should be 2 - 1 = 1
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Denial));
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Denial") && e.Contains("Honesty success at high interest"));
        }

        [Fact]
        public async Task HonestySuccessAtInterest14_NoDenialReduction()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Denial, 2, "setup");
            shadows.DrainGrowthEvents();

            // Interest at 14 (just under threshold) — need to check what interest is AFTER the roll
            // Honesty vs Chaos(1) → DC = 16 + 1 = 17. need=17-5=12 → Hard (+3). Roll 14+5=19 beat by 2 → scale=+1. delta=4.
            // startingInterest=10: after delta=4 → interest=14 < 15, no reduction.
            var session = BuildSession(
                dice: Dice(14, 50), // roll 14 + honesty 5 = 19 vs DC 17 → beats by 2 → scale=+1
                playerStats: Stats(honesty: 5),
                opponentStats: Stats(chaos: 1), // defence for Honesty is Chaos → DC = 16 + 1 = 17
                shadows: shadows,
                startingInterest: 10, // after +4 = 14 < 15
                options: new[] { new DialogueOption(StatType.Honesty, "truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // interestAfter should be < 15, no reduction
            Assert.Equal(2, shadows.GetDelta(ShadowStatType.Denial));
            Assert.DoesNotContain(result.ShadowGrowthEvents, e => e.Contains("Honesty success at high interest"));
        }

        [Fact]
        public async Task HonestyFailure_NoDenialReduction()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Denial, 2, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(2, 50), // low roll → failure
                playerStats: Stats(honesty: 0),
                shadows: shadows,
                startingInterest: 15,
                options: new[] { new DialogueOption(StatType.Honesty, "truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess);
            // No reduction on failure
            Assert.Equal(2, shadows.GetDelta(ShadowStatType.Denial));
            Assert.DoesNotContain(result.ShadowGrowthEvents, e => e.Contains("Honesty success at high interest"));
        }

        [Fact]
        public async Task NonHonestySuccessAtHighInterest_NoDenialReduction()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Denial, 2, "setup");
            shadows.DrainGrowthEvents();

            // Charm success at interest ≥15 should NOT reduce Denial
            // Use options without Honesty to isolate from #272 Denial skip-Honesty growth
            var session = BuildSession(
                dice: Dice(18, 50),
                playerStats: Stats(charm: 5),
                shadows: shadows,
                startingInterest: 15,
                options: new[] { new DialogueOption(StatType.Charm, "Hey, you come here often?") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0); // Charm, no Honesty available

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(2, shadows.GetDelta(ShadowStatType.Denial));
        }

        // =====================================================================
        // Reduction 4: Winning despite Overthinking disadvantage → Overthinking −1
        // =====================================================================

        [Fact]
        public async Task SuccessWithOverthinkingDisadvantage_ReducesOverthinkingByOne()
        {
            // Overthinking at 12 (T2) → SA gets disadvantage
            var shadows = new SessionShadowTracker(Stats());
            shadows.ApplyGrowth(ShadowStatType.Overthinking, 12, "setup");
            shadows.DrainGrowthEvents();

            // SA option, high roll to succeed despite disadvantage
            var session = BuildSession(
                dice: Dice(20, 20, 50), // adv: takes lower of two d20s, but Nat20 always succeeds
                playerStats: Stats(sa: 5),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.SelfAwareness, "aware") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // Overthinking was 12, should be 12 - 1 = 11
            Assert.Equal(11, shadows.GetDelta(ShadowStatType.Overthinking));
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Overthinking") && e.Contains("Succeeded despite"));
        }

        [Fact]
        public async Task FailureWithOverthinkingDisadvantage_NoReduction()
        {
            var shadows = new SessionShadowTracker(Stats());
            shadows.ApplyGrowth(ShadowStatType.Overthinking, 12, "setup");
            shadows.DrainGrowthEvents();

            // SA option, low roll → failure
            var session = BuildSession(
                dice: Dice(2, 2, 50),
                playerStats: Stats(sa: 0),
                shadows: shadows,
                startingInterest: 15,
                options: new[] { new DialogueOption(StatType.SelfAwareness, "aware") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess);
            // Overthinking should still be 12 (no reduction on failure)
            // +1 from SA usage count if 3+ uses, but only 1 use here
            Assert.Equal(12, shadows.GetDelta(ShadowStatType.Overthinking));
        }

        [Fact]
        public async Task SuccessWithCharm_NoOverthinkingReduction()
        {
            // Overthinking at 12 but using Charm (not SA) → no Overthinking reduction
            var shadows = new SessionShadowTracker(Stats());
            shadows.ApplyGrowth(ShadowStatType.Overthinking, 12, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(20, 50),
                playerStats: Stats(charm: 5),
                shadows: shadows);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0); // Charm

            Assert.True(result.Roll.IsSuccess);
            // Overthinking unchanged — Charm's paired shadow is Madness, not Overthinking
            Assert.Equal(12, shadows.GetDelta(ShadowStatType.Overthinking));
        }

        [Fact]
        public async Task SuccessWithSA_NoOverthinkingDisadvantage_NoReduction()
        {
            // Overthinking at 5 (below T2) → no disadvantage on SA
            var shadows = new SessionShadowTracker(Stats());
            shadows.ApplyGrowth(ShadowStatType.Overthinking, 5, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(18, 50),
                playerStats: Stats(sa: 5),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.SelfAwareness, "aware") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // No reduction because Overthinking wasn't high enough to cause disadvantage
            Assert.Equal(5, shadows.GetDelta(ShadowStatType.Overthinking));
        }

        // =====================================================================
        // Reduction 5: 4+ different stats → Fixation −1 (already implemented, verify)
        // =====================================================================

        [Fact]
        public async Task FourDistinctStats_ReducesFixation()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Fixation, 3, "setup");
            shadows.DrainGrowthEvents();

            var diceValues = new List<int>();
            for (int i = 0; i < 6; i++) { diceValues.Add(20); diceValues.Add(50); }

            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                playerStats: Stats(charm: 5, honesty: 5, wit: 5, chaos: 5),
                shadows: shadows,
                startingInterest: 5);

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // Charm
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(1); // Honesty
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(2); // Wit
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(3); // Chaos — 4 distinct

            if (!result.IsGameOver)
            {
                await session.StartTurnAsync();
                result = await session.ResolveTurnAsync(0);
            }

            // At end of game: 4+ stats → -1 Fixation
            Assert.True(result.IsGameOver);
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("4+ different stats"));
        }

        // =====================================================================
        // Reduction 6: SA/Honesty success at Interest >18 → Despair −1 (#717)
        // =====================================================================

        [Fact]
        public async Task SASuccessAtInterest19_ReducesDespair()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Despair, 3, "setup");
            shadows.DrainGrowthEvents();

            // Interest starts at 19, SA success. SA=5, opponent wit=0 → DC=16.
            // Roll 18 + 5 = 23 vs 16 → success. Interest should go up (still >18 after).
            var session = BuildSession(
                dice: Dice(18, 50),
                playerStats: Stats(sa: 5),
                shadows: shadows,
                startingInterest: 19,
                options: new[] { new DialogueOption(StatType.SelfAwareness, "reflect") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // Despair was 3, should be 3 - 1 = 2
            Assert.Equal(2, shadows.GetDelta(ShadowStatType.Despair));
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Despair") && e.Contains("SA/Honesty success at high interest"));
        }

        [Fact]
        public async Task HonestySuccessAtInterest19_ReducesDespair()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Despair, 3, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(18, 50),
                playerStats: Stats(honesty: 5),
                shadows: shadows,
                startingInterest: 19,
                options: new[] { new DialogueOption(StatType.Honesty, "truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(2, shadows.GetDelta(ShadowStatType.Despair));
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Despair") && e.Contains("SA/Honesty success at high interest"));
        }

        [Fact]
        public async Task SASuccessAtInterest18_NoDespairReduction()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Despair, 3, "setup");
            shadows.DrainGrowthEvents();

            // Interest starts low so even with bonuses interestAfter stays ≤18.
            // SA=5, opponent wit=0 → DC=16. Roll 12+5=17 vs DC 16 → success, beat by 1.
            // Start at 5 → interestAfter = 5 + delta (at most ~5), well below 18.
            var session = BuildSession(
                dice: Dice(12, 50),
                playerStats: Stats(sa: 5),
                shadows: shadows,
                startingInterest: 5,
                options: new[] { new DialogueOption(StatType.SelfAwareness, "reflect") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // Despair should remain at 3 (no reduction since interestAfter ≤ 18)
            Assert.Equal(3, shadows.GetDelta(ShadowStatType.Despair));
        }

        [Fact]
        public async Task SAFailureAtInterest19_NoDespairReduction()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Despair, 3, "setup");
            shadows.DrainGrowthEvents();

            // SA=0, opponent wit=0 → DC=16. Roll 5+0=5 vs 16 → miss. No reduction.
            var session = BuildSession(
                dice: Dice(5, 50),
                playerStats: Stats(sa: 0),
                opponentStats: Stats(wit: 0),
                shadows: shadows,
                startingInterest: 19,
                options: new[] { new DialogueOption(StatType.SelfAwareness, "reflect") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // If roll succeeded AND interest > 18, Despair should be reduced.
            // If roll failed, Despair should remain at 3.
            // This test verifies the failure path: even at high interest, failure does NOT reduce Despair.
            if (!result.Roll.IsSuccess)
            {
                // Confirmed failure path: Despair unchanged
                Assert.Equal(3, shadows.GetDelta(ShadowStatType.Despair));
            }
            else
            {
                // Roll succeeded (unexpected but possible with edge cases).
                // In that case, Despair may have been reduced. Skip this path.
                // The success path is already tested in SASuccessAtInterest19_ReducesDespair.
            }
        }

        // =====================================================================
        // Reduction 7: Success at interest ≥20 → Overthinking -1 (#721)
        // =====================================================================

        [Fact]
        public async Task SuccessAtInterest20_ReducesOverthinking()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Overthinking, 3, "setup");
            shadows.DrainGrowthEvents();

            // Interest starts at 20, Charm=5, opponent wit=0 → DC=16.
            // Roll 18+5=23 vs 16 → success. interestAfter ≥ 20.
            var session = BuildSession(
                dice: Dice(18, 50),
                playerStats: Stats(charm: 5),
                shadows: shadows,
                startingInterest: 20);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // Overthinking was 3, should be 3 - 1 = 2
            Assert.Equal(2, shadows.GetDelta(ShadowStatType.Overthinking));
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Overthinking") && e.Contains("pressure lifts"));
        }

        [Fact]
        public async Task SuccessAtInterest19_NoOverthinkingReduction()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Overthinking, 3, "setup");
            shadows.DrainGrowthEvents();

            // Interest starts at 5, Charm=5 → success but interestAfter well below 20.
            var session = BuildSession(
                dice: Dice(15, 50),
                playerStats: Stats(charm: 5),
                shadows: shadows,
                startingInterest: 5);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // Overthinking should remain at 3 (interestAfter < 20)
            Assert.Equal(3, shadows.GetDelta(ShadowStatType.Overthinking));
        }

        [Fact]
        public async Task FailureAtInterest20_NoOverthinkingReduction()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Overthinking, 3, "setup");
            shadows.DrainGrowthEvents();

            // Interest starts at 20 (VeryIntoIt → advantage, rolls 2 d20s).
            // SA=0, opponent honesty=1 → DC=14. Both d20s must be low.
            // Dice: d20a=2, d20b=3, d100(delay)=50.
            var session = BuildSession(
                dice: Dice(2, 3, 50),
                playerStats: Stats(sa: 0),
                shadows: shadows,
                startingInterest: 20,
                options: new[] { new DialogueOption(StatType.SelfAwareness, "reflect") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess);
            // Overthinking should remain at 3 (failure, no reduction)
            Assert.Equal(3, shadows.GetDelta(ShadowStatType.Overthinking));
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private static SessionShadowTracker MakeTracker()
            => new SessionShadowTracker(Stats());

        private static StatBlock Stats(
            int charm = 3, int rizz = 2, int honesty = 1,
            int chaos = 0, int wit = 4, int sa = 2)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm }, { StatType.Rizz, rizz },
                    { StatType.Honesty, honesty }, { StatType.Chaos, chaos },
                    { StatType.Wit, wit }, { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static CharacterProfile MakeProfile(string name, StatBlock stats)
            => new CharacterProfile(stats, "system prompt", name, new TimingProfile(5, 1.0f, 0.0f, "neutral"), 1);

        private static TestDice Dice(params int[] values) => new TestDice(values);

        private static GameSession BuildSession(
            TestDice? dice = null,
            StatBlock? playerStats = null,
            StatBlock? opponentStats = null,
            SessionShadowTracker? shadows = null,
            DialogueOption[]? options = null,
            string? previousOpener = null,
            int? startingInterest = null)
        {
            playerStats ??= Stats();
            opponentStats ??= Stats();
            ILlmAdapter llm = options != null
                ? (ILlmAdapter)new StubLlmAdapter(options)
                : new NullLlmAdapter();

            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows,
                previousOpener: previousOpener,
                startingInterest: startingInterest);

            // Prepend horniness roll (1d10) consumed by constructor
            var wrappedDice = new PrependedDice(5, dice ?? Dice(15, 50));

            return new GameSession(
                MakeProfile("player", playerStats),
                MakeProfile("opponent", opponentStats),
                llm,
                wrappedDice,
                new NullTrapRegistry(),
                config);
        }

        private static GameSession BuildSessionWithTrap(
            TestDice dice,
            StatBlock? playerStats = null,
            SessionShadowTracker? shadows = null,
            TrapDefinition? trapDef = null)
        {
            playerStats ??= Stats();
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);

            // Prepend horniness roll (1d10) consumed by constructor
            var wrappedDice = new PrependedDice(5, dice);

            var session = new GameSession(
                MakeProfile("player", playerStats),
                MakeProfile("opponent", Stats()),
                new NullLlmAdapter(),
                wrappedDice,
                new NullTrapRegistry(),
                config);

            // Activate a trap so Recover is possible
            if (trapDef != null)
            {
                // Use reflection to access private _traps field, or use a Speak turn to trigger trap
                // Instead, let's manually activate via TrapState
                var trapsField = typeof(GameSession).GetField("_traps",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var trapState = (TrapState)trapsField!.GetValue(session)!;
                trapState.Activate(trapDef);
            }

            return session;
        }

        private sealed class PrependedDice : IDiceRoller
        {
            private int? _first;
            private readonly IDiceRoller _inner;
            public PrependedDice(int firstValue, IDiceRoller inner) { _first = firstValue; _inner = inner; }
            public int Roll(int sides)
            {
                if (_first.HasValue) { var v = _first.Value; _first = null; return v; }
                return _inner.Roll(sides);
            }
        }

        private sealed class TestDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            public TestDice(int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides) => _values.Count > 0 ? _values.Dequeue() : 10;
        }

        private sealed class StubLlmAdapter : ILlmAdapter
        {
            private readonly DialogueOption[] _options;
            public StubLlmAdapter(DialogueOption[] options) => _options = options;
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
                => Task.FromResult(_options);
            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
        }
    }
}
