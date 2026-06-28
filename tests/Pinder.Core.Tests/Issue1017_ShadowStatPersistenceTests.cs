using System;
using System.Collections.Generic;
using Xunit;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.Core.TestCommon;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class Issue1017_ShadowStatPersistenceTests
    {
        private static CharacterProfile MakeProfile(string name, SessionShadowTracker? tracker = null)
        {
            var stats = TestHelpers.MakeStatBlock();
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return new CharacterProfile(stats, "system prompt", name, timing, 1);
        }

        private (GameSession Session, SessionShadowTracker Tracker) BuildSession()
        {
            var tracker = new SessionShadowTracker(TestHelpers.MakeStatBlock());
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                playerShadows: tracker
            );
            
            var session = new GameSession(
                MakeProfile("Player", tracker),
                MakeProfile("Datee"),
                new StubLlmAdapter(),
                new StubDice(10),
                new StubTrapRegistry(),
                config);
                
            return (session, tracker);
        }

        [Fact]
        public void CreateSnapshot_IncludesNonZeroPlayerShadowValues()
        {
            var (session, tracker) = BuildSession();
            tracker.ApplyGrowth(ShadowStatType.Dread, 5, "Test growth");

            var snapshot = session.CreateSnapshot();

            Assert.NotNull(snapshot.ShadowValues);
            Assert.True(snapshot.ShadowValues.ContainsKey(ShadowStatType.Dread.ToString()), "Snapshot should contain Dread shadow value");
            Assert.Equal(5, snapshot.ShadowValues[ShadowStatType.Dread.ToString()]);
        }

        [Fact]
        public void RestoreState_WithNonEmptyShadowValues_RestoresIntoTracker()
        {
            var (session, tracker) = BuildSession();
            
            var resimData = new ResimulateData
            {
                ShadowValues = new Dictionary<string, int>
                {
                    { ShadowStatType.Fixation.ToString(), 7 }
                }
            };

            session.RestoreState(resimData, new StubTrapRegistry());

            Assert.Equal(7, tracker.GetEffectiveShadow(ShadowStatType.Fixation));
        }

        private sealed class StubDice : IDiceRoller
        {
            private readonly int _value;
            public StubDice(int value = 10) => _value = value;
            public int Roll(int sides) => _value;
        }

        private sealed class StubTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}
