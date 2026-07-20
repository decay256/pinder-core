using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
            return TestHelpers.MakeCharacterProfile(
                stats,
                "system prompt",
                name,
                timing,
                1,
                psychiatricDiagnosis: new Dictionary<string, string>
                {
                    ["derived_feeling"] = "curious",
                    ["defense_reaction"] = "guarded",
                },
                backstory: TestHelpers.MakeBackstory(),
                stakeLines: TestHelpers.MakeStakeLines());
        }

        private (GameSession Session, SessionShadowTracker Tracker) BuildSession(params int[] diceRolls)
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
                new StubDice(diceRolls.Length > 0 ? diceRolls : new[] { 10 }),
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
        public async Task RestoreState_PreservesTrackerIdentity_ForSubsequentGrowthAndDrain()
        {
            var (session, tracker) = BuildSession(5, 1, 50);
            
            var resimData = new ResimulateData
            {
                TargetInterest = 10,
                ShadowValues = new Dictionary<string, int>
                {
                    { ShadowStatType.Fixation.ToString(), 7 }
                }
            };

            session.RestoreState(resimData, new StubTrapRegistry());

            Assert.Equal(7, tracker.GetEffectiveShadow(ShadowStatType.Fixation));

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(7, tracker.GetEffectiveShadow(ShadowStatType.Fixation));
            Assert.Equal(1, tracker.GetEffectiveShadow(ShadowStatType.Madness));
            Assert.Contains(result.ShadowGrowthEvents, entry => entry.Contains("Madness"));
            Assert.Empty(tracker.DrainGrowthEvents());
        }

        private sealed class StubDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            private int _lastValue;

            public StubDice(IEnumerable<int> values)
            {
                _values = new Queue<int>(values);
                _lastValue = _values.Last();
            }

            public int Roll(int sides)
            {
                if (_values.Count > 0)
                    _lastValue = _values.Dequeue();

                return _lastValue;
            }
        }

        private sealed class StubTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}
