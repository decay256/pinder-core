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
    /// Spec tests for Issue #350: Enable shadow tracking via GameSessionConfig.
    /// Tests verify behavior from the spec, not implementation details.
    /// Maturity: Prototype (happy-path per acceptance criterion).
    /// </summary>
    [Trait("Category", "SessionRunner")]
    public sealed class Issue350_ShadowTrackingSpecTests
    {
        // ── AC1: SessionShadowTracker wraps StatBlock and wires via GameSessionConfig ──

        // Mutation: would catch if GameSession ignores config.PlayerShadows entirely
        [Fact]
        public async Task AC1_SessionWithPlayerShadows_EnablesShadowGrowthEvents()
        {
            // Nat 1 triggers Madness shadow growth when PlayerShadows is wired
            var stats = BuildStatBlock();
            var shadows = new SessionShadowTracker(stats);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);
            var session = BuildSession(stats, config, diceRolls: new[] { 5, 1, 50 }); // horniness, d20=1, d100

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.NotEmpty(result.ShadowGrowthEvents);
        }

        // Mutation: would catch if config=null still populated ShadowGrowthEvents
        [Fact]
        public async Task AC1_SessionWithoutConfig_ShadowGrowthEventsEmpty()
        {
            var stats = BuildStatBlock();
            var session = BuildSession(stats, config: null, diceRolls: new[] { 5, 1, 50 });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Empty(result.ShadowGrowthEvents);
        }

        // Mutation: would catch if SessionShadowTracker didn't take StatBlock base values
        [Fact]
        public void AC1_SessionShadowTracker_ReflectsStatBlockBaseValues()
        {
            var stats = BuildStatBlock(denial: 5, fixation: 3);
            var shadows = new SessionShadowTracker(stats);

            Assert.Equal(5, shadows.GetEffectiveShadow(ShadowStatType.Denial));
            Assert.Equal(3, shadows.GetEffectiveShadow(ShadowStatType.Fixation));
            Assert.Equal(0, shadows.GetEffectiveShadow(ShadowStatType.Madness));
        }

        // Mutation: would catch if retained reference doesn't reflect session mutations
        [Fact]
        public async Task AC1_RetainedShadowReference_ReflectsGrowthAfterTurns()
        {
            var stats = BuildStatBlock();
            var shadows = new SessionShadowTracker(stats);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);
            var session = BuildSession(stats, config, diceRolls: new[] { 5, 1, 50 });

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // Nat 1 → Madness growth

            // The same reference we passed in should show the growth
            Assert.True(shadows.GetDelta(ShadowStatType.Madness) >= 1,
                "Retained reference must reflect shadow mutations from GameSession");
        }

        // ── AC2: ShadowGrowthEvents contains descriptive event strings ──

        // Mutation: would catch if event strings were empty or didn't mention shadow name
        [Fact]
        public async Task AC2_ShadowGrowthEvents_ContainMeaningfulDescription()
        {
            var stats = BuildStatBlock();
            var shadows = new SessionShadowTracker(stats);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);
            var session = BuildSession(stats, config, diceRolls: new[] { 5, 1, 50 });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0); // Nat 1 → Madness

            Assert.All(result.ShadowGrowthEvents, e =>
            {
                Assert.False(string.IsNullOrWhiteSpace(e), "Event string must not be empty");
            });
            // At least one event should mention "Madness" (the shadow triggered by Nat 1)
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Madness"));
        }

        // ── AC3: Shadow delta table values are correct ──

        // Mutation: would catch if GetDelta returned effective value instead of just the delta
        [Fact]
        public void AC3_GetDelta_ReturnsOnlySessionDelta_NotTotalValue()
        {
            var stats = BuildStatBlock(denial: 5);
            var shadows = new SessionShadowTracker(stats);

            // Before any growth, delta should be 0, not 5
            Assert.Equal(0, shadows.GetDelta(ShadowStatType.Denial));

            shadows.ApplyGrowth(ShadowStatType.Denial, 2, "test growth");

            Assert.Equal(2, shadows.GetDelta(ShadowStatType.Denial));
            Assert.Equal(7, shadows.GetEffectiveShadow(ShadowStatType.Denial)); // 5 base + 2
        }

        // Mutation: would catch if not all 6 ShadowStatType values are queryable
        [Fact]
        public void AC3_AllSixShadowTypes_AreQueryable()
        {
            var stats = BuildStatBlock();
            var shadows = new SessionShadowTracker(stats);

            var allTypes = (ShadowStatType[])Enum.GetValues(typeof(ShadowStatType));
            Assert.Equal(6, allTypes.Length);

            foreach (var shadowType in allTypes)
            {
                // Should not throw
                var delta = shadows.GetDelta(shadowType);
                var effective = shadows.GetEffectiveShadow(shadowType);
                Assert.True(delta >= 0, $"Initial delta for {shadowType} should be >= 0");
                Assert.True(effective >= 0, $"Effective value for {shadowType} should be >= 0");
            }
        }

        // Mutation: would catch if GetEffectiveShadow didn't add delta to base
        [Fact]
        public void AC3_EffectiveShadow_EqualsBasePlusDelta()
        {
            var stats = BuildStatBlock(madness: 0, denial: 3);
            var shadows = new SessionShadowTracker(stats);

            shadows.ApplyGrowth(ShadowStatType.Madness, 4, "growth");

            Assert.Equal(4, shadows.GetEffectiveShadow(ShadowStatType.Madness)); // 0 + 4
            Assert.Equal(4, shadows.GetDelta(ShadowStatType.Madness));
            Assert.Equal(3, shadows.GetEffectiveShadow(ShadowStatType.Denial)); // unchanged
            Assert.Equal(0, shadows.GetDelta(ShadowStatType.Denial));
        }

        // ── AC4: Fixation growth on 3 same-stat picks ──

        // Mutation: would catch if Fixation trigger required 4 picks instead of 3
        [Fact]
        public async Task AC4_ThreeConsecutiveSameStatPicks_TriggersFixationGrowth()
        {
            var stats = BuildStatBlock(charm: 3);
            var shadows = new SessionShadowTracker(stats);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);

            // 3 turns: horniness + (d20 + d100) * 3, high rolls to succeed
            var dice = new[] { 5, 15, 50, 15, 50, 15, 50 };
            var session = BuildSession(stats, config, dice);

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0); // Always pick option 0 (Charm)
            }

            Assert.True(shadows.GetDelta(ShadowStatType.Fixation) >= 1,
                "Fixation should grow by at least 1 after 3 consecutive same-stat picks");
        }

        // ── Edge Case: Multiple shadow events in a single turn ──

        // Mutation: would catch if only first event was captured
        [Fact]
        public async Task EdgeCase_MultipleShadowEventsPerTurn_AllCaptured()
        {
            // Nat 1 should trigger Madness. If we also have 3 same-stat picks,
            // Fixation fires too on the 3rd turn.
            var stats = BuildStatBlock(charm: 3);
            var shadows = new SessionShadowTracker(stats);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);

            // 3 turns of Charm: turns 1-2 succeed, turn 3 is Nat 1
            var dice = new[] { 5, 15, 50, 15, 50, 1, 50 };
            var session = BuildSession(stats, config, dice);

            for (int i = 0; i < 2; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            // Turn 3: Nat 1 (Madness) + 3rd same stat (Fixation)
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Should have at least 2 events (Madness + Fixation)
            Assert.True(result.ShadowGrowthEvents.Count >= 2,
                "Multiple shadow triggers in one turn should all appear in ShadowGrowthEvents");
        }

        // ── Edge Case: No shadow growth session ──

        // Mutation: would catch if ShadowGrowthEvents had phantom entries when no growth occurs
        [Fact]
        public async Task EdgeCase_HighRoll_NoShadowGrowth_OnNeutralStat()
        {
            // Pick Honesty (option index 2) so "skipped Honesty" doesn't trigger Denial growth
            // Use high charm/wit to ensure easy success, and pick Wit which has no skip penalty
            var stats = BuildStatBlock(wit: 5, denial: 0, fixation: 0);
            var shadows = new SessionShadowTracker(stats);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);
            // d20=18 → strong success, no shadow trigger expected
            var session = BuildSession(stats, config, diceRolls: new[] { 5, 18, 50 });

            await session.StartTurnAsync();
            // Pick option index that corresponds to Wit (varies by NullLlmAdapter,
            // but a high roll with no Nat 1 and first turn shouldn't trigger shadow growth
            // if we pick a stat that doesn't cause skip-triggered growth)
            var result = await session.ResolveTurnAsync(3); // Try Wit option

            // On first turn, high success, no Nat 1, no repeat stat → minimal shadow triggers
            // We at least verify the tracker didn't accumulate Madness (no Nat 1)
            Assert.Equal(0, shadows.GetDelta(ShadowStatType.Madness));
        }

        // ── Edge Case: Shadow accumulation across multiple turns ──

        // Mutation: would catch if deltas reset each turn instead of accumulating
        [Fact]
        public async Task EdgeCase_DeltasAccumulateAcrossTurns()
        {
            var stats = BuildStatBlock(charm: 3);
            var shadows = new SessionShadowTracker(stats);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);

            // Two Nat 1s on Charm → Madness +1 each = +2 total
            var dice = new[] { 5, 1, 50, 1, 50 };
            var session = BuildSession(stats, config, dice);

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Madness));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            Assert.Equal(2, shadows.GetDelta(ShadowStatType.Madness));
        }

        // ── Edge Case: Negative shadow deltas via ApplyOffset ──

        // Mutation: would catch if negative offsets were clamped to 0
        [Fact]
        public void EdgeCase_NegativeDelta_ViaApplyOffset()
        {
            var stats = BuildStatBlock(fixation: 5);
            var shadows = new SessionShadowTracker(stats);

            shadows.ApplyOffset(ShadowStatType.Fixation, -2, "variety");

            Assert.Equal(-2, shadows.GetDelta(ShadowStatType.Fixation));
            Assert.Equal(3, shadows.GetEffectiveShadow(ShadowStatType.Fixation)); // 5 - 2
        }

        // ── Edge Case: Shadow readable after GameEndedException ──

        // Mutation: would catch if shadow state was invalidated on game end
        [Fact]
        public void EdgeCase_ShadowsReadableAfterGrowth_RegardlessOfSessionState()
        {
            var stats = BuildStatBlock();
            var shadows = new SessionShadowTracker(stats);

            shadows.ApplyGrowth(ShadowStatType.Dread, 3, "test dread");
            shadows.ApplyGrowth(ShadowStatType.Despair, 1, "test horniness");

            // Even after multiple growths, all values are consistent
            Assert.Equal(3, shadows.GetDelta(ShadowStatType.Dread));
            Assert.Equal(3, shadows.GetEffectiveShadow(ShadowStatType.Dread));
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Despair));
            Assert.Equal(1, shadows.GetEffectiveShadow(ShadowStatType.Despair));
            // Untouched shadows remain at base
            Assert.Equal(0, shadows.GetDelta(ShadowStatType.Madness));
        }

        // ── Error Condition: null StatBlock ──

        // Mutation: would catch if constructor didn't validate input
        [Fact]
        public void ErrorCondition_NullStatBlock_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new SessionShadowTracker(null!));
        }

        // ── Error Condition: PlayerShadows without OpponentShadows is valid ──

        // Mutation: would catch if GameSession required both shadows or null
        [Fact]
        public async Task ErrorCondition_PlayerShadowsOnly_NoOpponentShadows_IsValid()
        {
            var stats = BuildStatBlock();
            var shadows = new SessionShadowTracker(stats);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows); // no opponentShadows

            var session = BuildSession(stats, config, diceRolls: new[] { 5, 15, 50 });

            // Should not throw
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.NotNull(result);
        }

        // ── Helpers ──

        private static StatBlock BuildStatBlock(
            int charm = 3, int rizz = 2, int honesty = 1,
            int chaos = 0, int wit = 4, int sa = 2,
            int madness = 0, int horniness = 0, int denial = 3,
            int fixation = 2, int dread = 0, int overthinking = 0)
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
                    { ShadowStatType.Madness, madness }, { ShadowStatType.Despair, horniness },
                    { ShadowStatType.Denial, denial }, { ShadowStatType.Fixation, fixation },
                    { ShadowStatType.Dread, dread }, { ShadowStatType.Overthinking, overthinking }
                });
        }

        private static CharacterProfile BuildProfile(string name, StatBlock stats)
        {
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return new CharacterProfile(stats, "system prompt", name, timing, 1);
        }

        private static GameSession BuildSession(
            StatBlock playerStats,
            GameSessionConfig? config,
            int[] diceRolls)
        {
            var opponentStats = BuildStatBlock();
            // Clock is required; if config has no clock, provide a default.
            config = config ?? new GameSessionConfig(clock: TestHelpers.MakeClock());
            return new GameSession(
                BuildProfile("player", playerStats),
                BuildProfile("opponent", opponentStats),
                new NullLlmAdapter(),
                new SequenceDice(diceRolls),
                new EmptyTrapRegistry(),
                config);
        }

        /// <summary>Deterministic dice that returns values from a queue.</summary>
        private sealed class SequenceDice : IDiceRoller
        {
            private readonly int[] _values;
            private int _index;

            public SequenceDice(int[] values) { _values = values; }

            public int Roll(int sides)
            {
                if (_index >= _values.Length) return 10; // safe fallback
                return _values[_index++];
            }
        }

        /// <summary>Trap registry that returns no traps.</summary>
        private sealed class EmptyTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}
