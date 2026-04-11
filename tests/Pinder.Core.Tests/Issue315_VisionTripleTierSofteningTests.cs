using System.Collections.Generic;
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
    /// Vision concern #315: Verifies that Triple combo bonus on Read/Recover
    /// flows through the externalBonus parameter (not deprecated AddExternalBonus),
    /// and therefore correctly softens failure tiers.
    /// </summary>
    public class Issue315_VisionTripleTierSofteningTests
    {
        // AC3 from #315: Read roll that would be TropeTrap (miss by 6) with Triple +1 → Misfire (miss by 5)
        // dice=6, SA=0, level=1 → total=6. DC=12 → miss=6 → TropeTrap without bonus.
        // With Triple +1: finalTotal=7, miss=5 → Misfire.
        [Fact]
        public async Task ReadAsync_TripleSoftensTropeTrapToMisfire()
        {
            var session = MakeSession(diceValue: 6, saModifier: 0);
            SetupTripleBonus(session);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Equal(FailureTier.Misfire, result.Roll.Tier);
            Assert.Equal(5, result.Roll.MissMargin);
            Assert.Equal(1, result.Roll.ExternalBonus);
        }

        // Verify same roll WITHOUT triple IS TropeTrap (control test)
        [Fact]
        public async Task ReadAsync_NoTriple_SameRollIsTropeTrap()
        {
            var session = MakeSession(diceValue: 6, saModifier: 0);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Equal(FailureTier.TropeTrap, result.Roll.Tier);
            Assert.Equal(6, result.Roll.MissMargin);
            Assert.Equal(0, result.Roll.ExternalBonus);
        }

        // Same verification for Recover path
        // dice=6, SA=0 → total=6, DC=12, miss=6 → TropeTrap. With Triple: miss=5 → Misfire.
        [Fact]
        public async Task RecoverAsync_TripleSoftensTropeTrapToMisfire()
        {
            var session = MakeSession(diceValue: 6, saModifier: 0);
            ActivateTrap(session);
            SetupTripleBonus(session);

            var result = await session.RecoverAsync();

            Assert.False(result.Success);
            Assert.Equal(FailureTier.Misfire, result.Roll.Tier);
            Assert.Equal(5, result.Roll.MissMargin);
            Assert.Equal(1, result.Roll.ExternalBonus);
        }

        // Control: Recover without triple at same dice is TropeTrap
        [Fact]
        public async Task RecoverAsync_NoTriple_SameRollIsTropeTrap()
        {
            var session = MakeSession(diceValue: 6, saModifier: 0);
            ActivateTrap(session);

            var result = await session.RecoverAsync();

            Assert.False(result.Success);
            Assert.Equal(FailureTier.TropeTrap, result.Roll.Tier);
            Assert.Equal(6, result.Roll.MissMargin);
            Assert.Equal(0, result.Roll.ExternalBonus);
        }

        // ======================== Helpers ========================

        private static void SetupTripleBonus(GameSession session)
        {
            var trackerField = typeof(GameSession).GetField("_comboTracker",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var tracker = (ComboTracker)trackerField!.GetValue(session)!;

            tracker.RecordTurn(StatType.Rizz, true);
            tracker.RecordTurn(StatType.SelfAwareness, true);
            tracker.RecordTurn(StatType.Chaos, true);

            if (!tracker.HasTripleBonus)
                throw new System.InvalidOperationException("Triple bonus setup failed");
        }

        private static void ActivateTrap(GameSession session)
        {
            var trapsField = typeof(GameSession).GetField("_traps",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var trapState = (TrapState)trapsField!.GetValue(session)!;

            var trap = new TrapDefinition("test-trap", StatType.Charm,
                TrapEffect.Disadvantage, 0, 3, "test trap active", "recover", "none");
            trapState.Activate(trap);
        }

        private static GameSession MakeSession(int diceValue, int saModifier)
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 3 }, { StatType.Rizz, 2 }, { StatType.Honesty, 1 },
                    { StatType.Chaos, 0 }, { StatType.Wit, 4 }, { StatType.SelfAwareness, saModifier }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            var player = new CharacterProfile(stats, "system prompt", "player", timing, 1);
            var opponent = new CharacterProfile(stats, "system prompt", "opponent", timing, 1);

            return new GameSession(
                player, opponent,
                new StubLlmAdapter(),
                new StubDice(diceValue),
                new StubTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock()));
        }

        private sealed class StubDice : IDiceRoller
        {
            private readonly int _value;
            public StubDice(int value) => _value = value;
            public int Roll(int sides) => _value;
        }

        private sealed class StubLlmAdapter : ILlmAdapter
        {
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
                => Task.FromResult(new DialogueOption[0]);

            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult("delivered");

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("response"));

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
        }

        private sealed class StubTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}
