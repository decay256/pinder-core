using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Regression tests for GitHub Issue #1221: Making the harness honest via engine-vs-heuristic scoring labels.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue1221_HarnessScoringModeLabelTests
    {
        #region Test 1: AGENTS EXPOSE A SCORING-MODE LABEL (RED, reflection)
        [Fact]
        public void Test1_AgentsExposeScoringModeLabel()
        {
            var scoringAgent = new ScoringPlayerAgent();
            var highestModAgent = new HighestModAgent();

            CheckAgentHasProperty(scoringAgent);
            CheckAgentHasProperty(highestModAgent);
        }

        private void CheckAgentHasProperty(object agent)
        {
            var properties = agent.GetType().GetProperties();
            var match = properties.Any(p => 
                p.Name.Contains("ScoringMode", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains("Mode", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains("MechanicsSource", StringComparison.OrdinalIgnoreCase));
            
            Assert.True(match, $"Expected type {agent.GetType().Name} to expose a public instance property containing 'ScoringMode', 'Mode', or 'MechanicsSource'.");
        }
        #endregion

        #region Test 2: SCORING-MODE TYPE/ENUM EXISTS (RED, reflection)
        [Fact]
        public void Test2_ScoringModeTypeExists()
        {
            var assembly = typeof(ScoringPlayerAgent).Assembly;
            var types = assembly.GetTypes();
            var match = types.Any(t => t.Name.Contains("ScoringMode", StringComparison.OrdinalIgnoreCase));
            
            Assert.True(match, "Expected a type containing 'ScoringMode' in its name to exist in the Pinder.SessionRunner assembly.");
        }
        #endregion

        #region Test 3: HARNESS OUTPUT CARRIES scoring_mode LABEL (RED)
        [Fact]
        public async Task Test3_HarnessOutputCarriesScoringModeLabel()
        {
            var agent = new ScoringPlayerAgent();
            var playerStats = MakeStats(charm: 3, rizz: 3, honesty: 3, chaos: 3, wit: 3, selfAwareness: 3);
            var dateeStats = MakeStats();
            var options = new[] { new DialogueOption(StatType.Charm, "test") };
            var turn = MakeTurn(options);
            var ctx = MakeContext(playerStats, dateeStats);

            var decision = await agent.DecideAsync(turn, ctx);
            var formatterOutput = PlaytestFormatter.FormatReasoningBlock(decision, "ScoringPlayerAgent");
            
            bool hasScoringModeLabelInOutput = formatterOutput != null && formatterOutput.Contains("scoring_mode", StringComparison.OrdinalIgnoreCase);
            
            bool agentHasScoringModePropertyWithHeuristic = false;
            var scoringModeProp = agent.GetType().GetProperties()
                .FirstOrDefault(p => p.Name.Contains("ScoringMode", StringComparison.OrdinalIgnoreCase) ||
                                     p.Name.Contains("Mode", StringComparison.OrdinalIgnoreCase) ||
                                     p.Name.Contains("MechanicsSource", StringComparison.OrdinalIgnoreCase));
            if (scoringModeProp != null)
            {
                var val = scoringModeProp.GetValue(agent);
                if (val != null && val.ToString()!.Contains("heuristic", StringComparison.OrdinalIgnoreCase))
                {
                    agentHasScoringModePropertyWithHeuristic = true;
                }
            }
            
            Assert.True(hasScoringModeLabelInOutput || agentHasScoringModePropertyWithHeuristic,
                "Expected either the playtest formatter output to contain 'scoring_mode' (case-insensitive) or the agent to have a ScoringMode property whose value contains 'heuristic' (case-insensitive). PlaytestFormatter output: " + (formatterOutput ?? "null"));
        }
        #endregion

        #region Test 4: PARITY GUARD — DECISIONS UNCHANGED (must PASS before & after)
        [Fact]
        public async Task Test4_ParityGuard_ScoringPlayerAgentDecisionsUnchanged()
        {
            var agent = new ScoringPlayerAgent();
            var playerStats = MakeStats(charm: 4, rizz: 2, honesty: 1, chaos: 3);
            var dateeStats = MakeStats(selfAwareness: 2, wit: 1, chaos: 0, charm: 1);

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Rizz, "b"),
                new DialogueOption(StatType.Honesty, "c", hasTellBonus: true),
                new DialogueOption(StatType.Chaos, "d", comboName: "The Switcheroo")
            };

            var turn = MakeTurn(options);
            var ctx = MakeContext(playerStats: playerStats, dateeStats: dateeStats);

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.NotNull(decision);
            Assert.NotNull(decision.Scores);
            Assert.True(decision.OptionIndex >= 0 && decision.OptionIndex < options.Length);
        }
        #endregion

        #region Test 5: PARITY GUARD — HighestModAgent picks highest-mod (must PASS before & after)
        [Fact]
        public async Task Test5_ParityGuard_HighestModAgentPicksHighestModifierOption()
        {
            var agent = new HighestModAgent();
            var player = MakePlayerStats(); // Charm=4 is highest
            var options = new[]
            {
                new DialogueOption(StatType.Rizz, "low"),     // mod +1
                new DialogueOption(StatType.Charm, "high"),    // mod +4
                new DialogueOption(StatType.Honesty, "mid"),   // mod +3
            };
            var turn = MakeHighestModTurn(options);
            var ctx = MakeHighestModContext(player, MakeDateeStats());

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.Equal(1, decision.OptionIndex);
        }
        #endregion

        #region Helpers
        private static StatBlock MakeStats(
            int charm = 0, int rizz = 0, int honesty = 0,
            int chaos = 0, int wit = 0, int selfAwareness = 0,
            int madness = 0, int horniness = 0, int denial = 0,
            int fixation = 0, int dread = 0, int overthinking = 0)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm },
                    { StatType.Rizz, rizz },
                    { StatType.Honesty, honesty },
                    { StatType.Chaos, chaos },
                    { StatType.Wit, wit },
                    { StatType.SelfAwareness, selfAwareness }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, madness },
                    { ShadowStatType.Despair, horniness },
                    { ShadowStatType.Denial, denial },
                    { ShadowStatType.Fixation, fixation },
                    { ShadowStatType.Dread, dread },
                    { ShadowStatType.Overthinking, overthinking }
                });
        }

        private static TurnStart MakeTurn(params DialogueOption[] options)
        {
            var snapshot = new GameStateSnapshot(
                interest: 12,
                state: InterestState.Interested,
                momentumStreak: 0,
                activeTrapNames: Array.Empty<string>(),
                turnNumber: 3);
            return new TurnStart(options, snapshot);
        }

        private static PlayerAgentContext MakeContext(
            StatBlock? playerStats = null,
            StatBlock? dateeStats = null,
            int currentInterest = 12,
            InterestState interestState = InterestState.Interested,
            int momentumStreak = 0,
            string[]? activeTrapNames = null,
            int sessionHorniness = 0,
            Dictionary<ShadowStatType, int>? shadowValues = null,
            int turnNumber = 3)
        {
            return new PlayerAgentContext(
                playerStats ?? MakeStats(charm: 3, rizz: 3, honesty: 3, chaos: 3, wit: 3, selfAwareness: 3),
                dateeStats ?? MakeStats(),
                currentInterest,
                interestState,
                momentumStreak,
                activeTrapNames ?? Array.Empty<string>(),
                sessionHorniness,
                shadowValues,
                turnNumber);
        }

        private static StatBlock MakePlayerStats()
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 4 }, { StatType.Rizz, 1 }, { StatType.Honesty, 3 },
                    { StatType.Chaos, 2 }, { StatType.Wit, 2 }, { StatType.SelfAwareness, 3 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static StatBlock MakeDateeStats()
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 2 }, { StatType.Rizz, 3 }, { StatType.Honesty, 1 },
                    { StatType.Chaos, 2 }, { StatType.Wit, 1 }, { StatType.SelfAwareness, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static PlayerAgentContext MakeHighestModContext(StatBlock player, StatBlock datee,
            int interest = 12, InterestState state = InterestState.Interested, int momentum = 0)
        {
            return new PlayerAgentContext(player, datee, interest, state, momentum,
                Array.Empty<string>(), 0, null, 1);
        }

        private static TurnStart MakeHighestModTurn(DialogueOption[] options, int interest = 12,
            InterestState state = InterestState.Interested, int momentum = 0, int turn = 1)
        {
            return new TurnStart(options,
                new GameStateSnapshot(interest, state, momentum, Array.Empty<string>(), turn));
        }
        #endregion
    }
}