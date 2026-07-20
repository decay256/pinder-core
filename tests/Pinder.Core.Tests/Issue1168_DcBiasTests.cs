using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Pinder.Core.Traps;
using Pinder.Core.I18n;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class Issue1168_DcBiasTests
    {
        private static CharacterProfile MakeProfile(string name, int statVal = 2, int shadowVal = 0)
        {
            var stats = TestHelpers.MakeStatBlock(statVal, shadowVal);
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return TestHelpers.MakeCharacterProfile(stats, "system prompt", name, timing, 1);
        }

        private class DummyLlm : ILlmAdapter
        {
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
            {
                return Task.FromResult(new[] {
                    new DialogueOption(StatType.Charm, "Option 1"),
                    new DialogueOption(StatType.Rizz, "Option 2")
                });
            }

            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, System.Threading.CancellationToken ct = default)
            {
                return Task.FromResult(new DateeResponse("Response text", null, null));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, System.Threading.CancellationToken ct = default)
            {
                return Task.FromResult<string?>("Beat");
            }

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
            {
                return Task.FromResult(message);
            }

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
            {
                return Task.FromResult(message);
            }

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
            {
                return Task.FromResult(message);
            }
        
        public Task<string> ApplyFailureCorruptionAsync(string message, string instruction, Pinder.Core.Stats.StatType stat, Pinder.Core.Rolls.FailureTier tier, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
        {
            return Task.FromResult(message);
        }
}

        // 1. RollEngine.ApplyDcBias
        [Fact]
        public void ApplyDcBias_Zero_IsNoOp()
        {
            Assert.Equal(18, RollEngine.ApplyDcBias(18, 0));
        }

        [Fact]
        public void ApplyDcBias_Positive_LowersDc_Easier()
        {
            // Positive bias lowers effective DC (making check easier)
            Assert.Equal(15, RollEngine.ApplyDcBias(18, 3));
        }

        [Fact]
        public void ApplyDcBias_Negative_RaisesDc_Harder()
        {
            Assert.Equal(21, RollEngine.ApplyDcBias(18, -3));
        }

        // 2. Cross-independence: setting shadow_dc_bias does not shift main or horniness checks.
        [Fact]
        public void AllThreeBiases_AreFullyIndependent()
        {
            var rng = new Random(42);
            var consequenceCatalog = null as IConsequenceCatalog;

            // Instantiate engines with distinct biases
            var shadowEngine = new ShadowCheckEngine(rng, consequenceCatalog, shadowDcBias: 4);
            var horninessEngine = new HorninessEngine(rng, consequenceCatalog, horninessDcBias: 7);
            
            // Check Shadow DC: At shadowValue = 8, base DC is 8. Bias +4 -> DC = 4.
            var shadowResult = shadowEngine.Check(ShadowStatType.Madness, 8);
            Assert.Equal(4, shadowResult.DC);

            // Check Horniness DC: At value = 12, base DC is 12. Bias +7 -> DC = 5.
            var playerShadows = new SessionShadowTracker(TestHelpers.MakeStatBlock());
            var (horninessResult, _) = horninessEngine.PeekAsync(12, playerShadows, null);
            Assert.Equal(5, horninessResult.DC);

            // Check Main Roll (RollResolutionStage / GameSession with globalDcBias)
            // If globalDcBias is 3, base DC for a defender with stat 2 is 16 + 2 = 18. Bias +3 -> effective DC = 15.
            var player = MakeProfile("Player", 2, 0);
            var datee = MakeProfile("Datee", 2, 0);
            var dice = new FixedDice(
                5,    // Horniness session roll
                10,   // turn 1 roll
                50    // d100 ghost
            );

            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                globalDcBias: 3,
                shadowDcBias: 0,     // Ensure shadow/horniness biases don't bleed into main roll
                horninessDcBias: 0
            );

            var session = new GameSession(player, datee, new DummyLlm(), dice, new NullTrapRegistry(), config);
            
            // Resolve Turn and inspect resulting Roll
            var turnStart = session.StartTurnAsync().GetAwaiter().GetResult();
            var turnResult = session.ResolveTurnAsync(0).GetAwaiter().GetResult();

            // Effective DC should be: 18 - dcAdjustment = 18 - 3 = 15.
            Assert.Equal(15, turnResult.Roll.DC);
        }

        // 3. Symmetric Sign Behavior: positive bias -> lower effective DC -> higher success rate.
        [Fact]
        public void PositiveBias_MakesAllThreeChecksEasier()
        {
            var rng = new Random(42);
            var playerShadows = new SessionShadowTracker(TestHelpers.MakeStatBlock());

            // SHADOW: At shadowValue = 5, base DC is 5. Positive bias = 5 -> DC = 0 (easier).
            var shadowEngineWithBias = new ShadowCheckEngine(rng, shadowDcBias: 5);
            var shadowResultWithBias = shadowEngineWithBias.Check(ShadowStatType.Madness, 5);
            Assert.Equal(0, shadowResultWithBias.DC);

            // HORNINESS: At value = 5, base DC is 5. Positive bias = 5 -> DC = 0 (easier).
            var horninessEngineWithBias = new HorninessEngine(rng, horninessDcBias: 5);
            var (horninessResultWithBias, _) = horninessEngineWithBias.PeekAsync(5, playerShadows, null);
            Assert.Equal(0, horninessResultWithBias.DC);

            // MAIN ROLL: base DC = 18. Positive globalDcBias = 5 -> DC = 13 (easier).
            var player = MakeProfile("Player", 2, 0);
            var datee = MakeProfile("Datee", 2, 0);
            var dice = new FixedDice(5, 10, 50);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), globalDcBias: 5);
            var session = new GameSession(player, datee, new DummyLlm(), dice, new NullTrapRegistry(), config);
            session.StartTurnAsync().GetAwaiter().GetResult();
            var turnResult = session.ResolveTurnAsync(0).GetAwaiter().GetResult();
            Assert.Equal(13, turnResult.Roll.DC);
        }

        // 4. global_dc_bias flip refactor:
        // - At value 0: behaviour byte-identical to pre-refactor (base DC with SA=2 is 18).
        // - At positive value: is now easier.
        [Fact]
        public void GlobalDcBias_Zero_IsByteIdentical_PositiveIsEasier()
        {
            var player = MakeProfile("Player", 2, 0);
            var datee = MakeProfile("Datee", 2, 0);

            // Zero Bias
            {
                var dice = new FixedDice(5, 10, 50);
                var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), globalDcBias: 0);
                var session = new GameSession(player, datee, new DummyLlm(), dice, new NullTrapRegistry(), config);
                session.StartTurnAsync().GetAwaiter().GetResult();
                var turnResult = session.ResolveTurnAsync(0).GetAwaiter().GetResult();
                
                // DC with SA=2 is 16 + 2 = 18.
                Assert.Equal(18, turnResult.Roll.DC);
            }

            // Positive Bias makes it EASIER (lowers effective DC).
            {
                var dice = new FixedDice(5, 10, 50);
                var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), globalDcBias: 3);
                var session = new GameSession(player, datee, new DummyLlm(), dice, new NullTrapRegistry(), config);
                session.StartTurnAsync().GetAwaiter().GetResult();
                var turnResult = session.ResolveTurnAsync(0).GetAwaiter().GetResult();
                
                // DC with globalDcBias=3 should be 18 - 3 = 15.
                Assert.Equal(15, turnResult.Roll.DC);
            }
        }
    }
}
