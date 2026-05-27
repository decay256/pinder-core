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
using Pinder.Core.TestCommon;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class ShadowReductionTests
    {
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
                session.State.Traps.Activate(trapDef);
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
    }
}
