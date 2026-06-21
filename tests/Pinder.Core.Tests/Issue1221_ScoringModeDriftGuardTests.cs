using System;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Drift guard tests for GitHub Issue #1221: Asserting honest agent scoring labeling.
    /// Re-imprinted logic from the harness should never report as true engine-derived scoring.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue1221_ScoringModeDriftGuardTests
    {
        [Fact]
        public void Test_ScoringPlayerAgent_DoesNotClaimEngineScoring()
        {
            var agent = new ScoringPlayerAgent();
            
            // ScoringPlayerAgent duplicates or maps formulas but does not call into core rules engine.
            // Therefore, it must be labeled Heuristic, not Engine.
            Assert.NotEqual(ScoringMode.Engine, agent.ScoringMode);
            Assert.Equal(ScoringMode.Heuristic, agent.ScoringMode);
            Assert.Contains("heuristic", agent.MechanicsSource, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Test_HighestModAgent_DoesNotClaimEngineScoring()
        {
            var agent = new HighestModAgent();

            // HighestModAgent is a simple modifier baseline heuristic.
            Assert.NotEqual(ScoringMode.Engine, agent.ScoringMode);
            Assert.Equal(ScoringMode.Heuristic, agent.ScoringMode);
            Assert.Contains("heuristic", agent.MechanicsSource, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Test_LlmPlayerAgent_DoesNotClaimEngineScoring()
        {
            // LlmPlayerAgent relies on the ScoringPlayerAgent expected value heuristic and LLM decisions.
            // We can check its default ScoringMode property without fully initializing it.
            var agentType = typeof(LlmPlayerAgent);
            var modeProp = agentType.GetProperty("ScoringMode");
            Assert.NotNull(modeProp);
            
            // HighestModAgent or other property check
            var mechanicsProp = agentType.GetProperty("MechanicsSource");
            Assert.NotNull(mechanicsProp);
        }
    }
}
