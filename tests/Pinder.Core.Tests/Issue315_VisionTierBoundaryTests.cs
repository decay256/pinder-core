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
    /// Vision concern #315 — Additional tier boundary tests.
    /// Verifies that Triple combo bonus (+1) via externalBonus parameter
    /// correctly softens failure tiers at EVERY tier boundary on Read/Recover.
    /// 
    /// Failure tier boundaries (§5):
    ///   Nat1 → Legendary (special)
    ///   miss 1–2 → Fumble
    ///   miss 3–5 → Misfire
    ///   miss 6–9 → TropeTrap
    ///   miss 10+ → Catastrophe
    /// 
    /// DC for Read/Recover is 12. With SA=0, Total = dice roll.
    /// MissMargin = DC - FinalTotal = 12 - (dice + externalBonus).
    /// </summary>
    public class Issue315_VisionTierBoundaryTests
    {
        // ======================== Catastrophe → TropeTrap boundary ========================

        // What: Catastrophe boundary — miss by 10 with Triple +1 → miss by 9 = TropeTrap
        // Mutation: Fails if externalBonus doesn't affect tier (AddExternalBonus bug)
        [Fact]
        public async Task ReadAsync_CatastropheBoundary_TripleSoftensToTropeTrap()
        {
            // dice=2, SA=0 → Total=2, DC=12, miss=10 → Catastrophe
            // With Triple +1: FinalTotal=3, miss=9 → TropeTrap
            var session = MakeSession(diceValue: 2, saModifier: 0);
            SetupTripleBonus(session);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Equal(FailureTier.TropeTrap, result.Roll.Tier);
            Assert.Equal(9, result.Roll.MissMargin);
        }

        // Control: Without triple, same roll is Catastrophe
        // Mutation: Fails if control case doesn't establish Catastrophe baseline
        [Fact]
        public async Task ReadAsync_CatastropheBoundary_NoTriple_IsCatastrophe()
        {
            var session = MakeSession(diceValue: 2, saModifier: 0);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Equal(FailureTier.Catastrophe, result.Roll.Tier);
            Assert.Equal(10, result.Roll.MissMargin);
        }

        // ======================== Misfire → Fumble boundary ========================

        // What: Misfire boundary — miss by 3 with Triple +1 → miss by 2 = Fumble
        // Mutation: Fails if tier is still Misfire when externalBonus not applied at construction
        [Fact]
        public async Task ReadAsync_MisfireBoundary_TripleSoftensToFumble()
        {
            // dice=9, SA=0 → Total=9, DC=12, miss=3 → Misfire
            // With Triple +1: FinalTotal=10, miss=2 → Fumble
            var session = MakeSession(diceValue: 9, saModifier: 0);
            SetupTripleBonus(session);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Equal(FailureTier.Fumble, result.Roll.Tier);
            Assert.Equal(2, result.Roll.MissMargin);
        }

        // Control: Without triple, same roll is Misfire
        // Mutation: Fails if control baseline is wrong
        [Fact]
        public async Task ReadAsync_MisfireBoundary_NoTriple_IsMisfire()
        {
            var session = MakeSession(diceValue: 9, saModifier: 0);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Equal(FailureTier.Misfire, result.Roll.Tier);
            Assert.Equal(3, result.Roll.MissMargin);
        }

        // ======================== Fumble → Success boundary ========================

        // What: Fumble boundary — miss by 1 with Triple +1 → miss by 0 = Success
        // Mutation: Fails if externalBonus doesn't flip a Fumble into a success
        [Fact]
        public async Task ReadAsync_FumbleBoundary_TripleConvertsToSuccess()
        {
            // dice=11, SA=0 → Total=11, DC=12, miss=1 → Fumble
            // With Triple +1: FinalTotal=12, miss=0 → Success (FinalTotal >= DC)
            var session = MakeSession(diceValue: 11, saModifier: 0);
            SetupTripleBonus(session);

            var result = await session.ReadAsync();

            Assert.True(result.Success);
        }

        // Control: Without triple, same roll is Fumble
        // Mutation: Fails if Fumble baseline is wrong
        [Fact]
        public async Task ReadAsync_FumbleBoundary_NoTriple_IsFumble()
        {
            var session = MakeSession(diceValue: 11, saModifier: 0);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Equal(FailureTier.Fumble, result.Roll.Tier);
            Assert.Equal(1, result.Roll.MissMargin);
        }

        // ======================== Recover path tier boundaries ========================

        // What: Recover — Catastrophe softened to TropeTrap with Triple
        // Mutation: Fails if Recover path doesn't pass externalBonus to ResolveFixedDC
        [Fact]
        public async Task RecoverAsync_CatastropheBoundary_TripleSoftensToTropeTrap()
        {
            // dice=2, SA=0 → Total=2, DC=12, miss=10 → Catastrophe
            // With Triple +1: miss=9 → TropeTrap
            var session = MakeSession(diceValue: 2, saModifier: 0);
            ActivateTrap(session);
            SetupTripleBonus(session);

            var result = await session.RecoverAsync();

            Assert.False(result.Success);
            Assert.Equal(FailureTier.TropeTrap, result.Roll.Tier);
            Assert.Equal(9, result.Roll.MissMargin);
        }

        // What: Recover — Fumble boundary softened to success with Triple
        // Mutation: Fails if Recover externalBonus doesn't affect success determination
        [Fact]
        public async Task RecoverAsync_FumbleBoundary_TripleConvertsToSuccess()
        {
            // dice=11, SA=0 → miss=1 → Fumble; with Triple → miss=0 → Success
            var session = MakeSession(diceValue: 11, saModifier: 0);
            ActivateTrap(session);
            SetupTripleBonus(session);

            var result = await session.RecoverAsync();

            Assert.True(result.Success);
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
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
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
                new StubTrapRegistry());
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
