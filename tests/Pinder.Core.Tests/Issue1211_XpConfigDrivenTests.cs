using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Progression;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class Issue1211_XpConfigDrivenTests
    {
        [Fact]
        public void RiskMultiplier_Reckless_Is10x_Fallback()
        {
            var rec = new SessionXpRecorder(new XpLedger(), null);
            var result = rec.ApplyRiskTierMultiplier(10, RiskTier.Reckless);
            Assert.Equal(100, result);
        }

        [Fact]
        public void RiskMultiplier_FullLadder_Fallback()
        {
            var rec = new SessionXpRecorder(new XpLedger(), null);
            Assert.Equal(10, rec.ApplyRiskTierMultiplier(10, RiskTier.Safe));
            Assert.Equal(15, rec.ApplyRiskTierMultiplier(10, RiskTier.Medium));
            Assert.Equal(20, rec.ApplyRiskTierMultiplier(10, RiskTier.Hard));
            Assert.Equal(30, rec.ApplyRiskTierMultiplier(10, RiskTier.Bold));
            Assert.Equal(100, rec.ApplyRiskTierMultiplier(10, RiskTier.Reckless));
        }

        [Fact]
        public async Task Terminal_DateSecured_Multiplies_CollectedXp_By3x()
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 24);
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0, playerStatValue: 3, config: config);
            
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(GameOutcome.DateSecured, result.Outcome);

            var events = session.XpLedger.Events;
            Assert.DoesNotContain(events, e => e.Source == "DateSecured" && e.Amount == 50);

            var rollEvents = events.Where(e => e.Source.StartsWith("Success") || e.Source.StartsWith("Nat20") || e.Source.StartsWith("Nat1") || e.Source.StartsWith("Failure")).ToList();
            var C = rollEvents.Sum(e => e.Amount);

            Assert.Equal(3 * C, session.XpLedger.TotalXp);
        }

        [Fact]
        public async Task Terminal_Unmatched_Is1x_NoBonusEvent()
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 1);
            var session = MakeSession(diceRoll: 2, dateeStatValue: 0, playerStatValue: 3, config: config);
            
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(GameOutcome.Unmatched, result.Outcome);

            var events = session.XpLedger.Events;
            Assert.DoesNotContain(events, e => e.Source == "ConversationComplete");
            
            var rollEvents = events.Where(e => e.Source.StartsWith("Success") || e.Source.StartsWith("Nat20") || e.Source.StartsWith("Nat1") || e.Source.StartsWith("Failure")).ToList();
            var C = rollEvents.Sum(e => e.Amount);

            Assert.Equal(C, session.XpLedger.TotalXp);
        }

        // ====================== Helpers ======================

        private static GameSession MakeSession(
            int diceRoll,
            int dateeStatValue,
            int playerStatValue = 3,
            GameSessionConfig? config = null)
        {
            var playerStats = MakeStatBlock(allStats: playerStatValue);
            var player = MakeProfile("player", playerStats);

            var dateeStats = MakeStatBlock(allStats: dateeStatValue);
            var datee = MakeProfile("datee", dateeStats);

            config = config ?? new GameSessionConfig(clock: TestHelpers.MakeClock());

            return new GameSession(
                player,
                datee,
                new NullLlmAdapter(),
                new ConstantDice(diceRoll),
                new NullTrapRegistry(),
                config);
        }

        private static StatBlock MakeStatBlock(int allStats = 2)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, allStats }, { StatType.Rizz, allStats },
                    { StatType.Honesty, allStats }, { StatType.Chaos, allStats },
                    { StatType.Wit, allStats }, { StatType.SelfAwareness, allStats }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static CharacterProfile MakeProfile(string name, StatBlock stats)
        {
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return TestHelpers.MakeCharacterProfile(stats, "system prompt", name, timing, 1);
        }

        private sealed class ConstantDice : IDiceRoller
        {
            private readonly int _value;
            public ConstantDice(int value) => _value = value;
            public int Roll(int sides) => _value;
        }

        private sealed class NullTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}