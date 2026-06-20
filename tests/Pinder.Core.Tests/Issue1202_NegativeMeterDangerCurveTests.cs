using System;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class Issue1202_NegativeMeterDangerCurveTests
    {
        [Fact]
        public void ShadowValue_IncreasingValue_IncreasesOrPreservesDangerDC()
        {
            var rng = new Random(42);
            var shadowEngine = new ShadowCheckEngine(rng, shadowDcBias: 0);

            var resultLow = shadowEngine.Check(ShadowStatType.Madness, 2);
            var resultHigh = shadowEngine.Check(ShadowStatType.Madness, 18);

            // TDD: This will fail under old implementation because resultLow.DC (18) is higher than resultHigh.DC (2).
            // Under new implementation, resultLow.DC should be <= resultHigh.DC.
            Assert.True(resultLow.DC < resultHigh.DC, $"Expected lower DC for lower shadow value. Got low={resultLow.DC}, high={resultHigh.DC}");
        }

        [Fact]
        public void SessionHorniness_IncreasingValue_IncreasesOrPreservesDangerDC()
        {
            var rng = new Random(42);
            var horninessEngine = new HorninessEngine(rng, horninessDcBias: 0);
            var playerShadows = new SessionShadowTracker(TestHelpers.MakeStatBlock());

            var (resultLow, _) = horninessEngine.PeekAsync(2, playerShadows, null);
            var (resultHigh, _) = horninessEngine.PeekAsync(18, playerShadows, null);

            // TDD: This will fail under old implementation because resultLow.Result.DC (18) is higher than resultHigh.Result.DC (2).
            // Under new implementation, resultLow.Result.DC should be <= resultHigh.Result.DC.
            Assert.True(resultLow.DC < resultHigh.DC, $"Expected lower DC for lower horniness value. Got low={resultLow.DC}, high={resultHigh.DC}");
        }

        [Fact]
        public void ShadowValue_ZeroOrLess_NotPerformed()
        {
            var rng = new Random(42);
            var shadowEngine = new ShadowCheckEngine(rng, shadowDcBias: 0);

            var resultZero = shadowEngine.Check(ShadowStatType.Madness, 0);
            var resultNeg = shadowEngine.Check(ShadowStatType.Madness, -5);

            Assert.False(resultZero.CheckPerformed);
            Assert.False(resultNeg.CheckPerformed);
        }

        [Fact]
        public void SessionHorniness_ZeroOrLess_NotPerformed()
        {
            var rng = new Random(42);
            var horninessEngine = new HorninessEngine(rng, horninessDcBias: 0);
            var playerShadows = new SessionShadowTracker(TestHelpers.MakeStatBlock());

            var (resultZero, _) = horninessEngine.PeekAsync(0, playerShadows, null);
            var (resultNeg, _) = horninessEngine.PeekAsync(-3, playerShadows, null);

            Assert.True(resultZero.Check == null);
            Assert.True(resultNeg.Check == null);
        }

        [Fact]
        public void NegativeBias_RaisesEffectiveDc_And_PositiveBias_LowersEffectiveDc()
        {
            var rng = new Random(42);
            var playerShadows = new SessionShadowTracker(TestHelpers.MakeStatBlock());

            // Shadow with value 10
            var shadowNoBias = new ShadowCheckEngine(rng, shadowDcBias: 0).Check(ShadowStatType.Madness, 10);
            var shadowPosBias = new ShadowCheckEngine(rng, shadowDcBias: 3).Check(ShadowStatType.Madness, 10);
            var shadowNegBias = new ShadowCheckEngine(rng, shadowDcBias: -3).Check(ShadowStatType.Madness, 10);

            // Under new design:
            // No bias DC = 10
            // Pos bias (+3) DC = 10 - 3 = 7 (easier/safer)
            // Neg bias (-3) DC = 10 - (-3) = 13 (harder/dangerous)
            Assert.Equal(10, shadowNoBias.DC);
            Assert.Equal(7, shadowPosBias.DC);
            Assert.Equal(13, shadowNegBias.DC);

            // Horniness with value 10
            var (horninessNoBias, _) = new HorninessEngine(rng, horninessDcBias: 0).PeekAsync(10, playerShadows, null);
            var (horninessPosBias, _) = new HorninessEngine(rng, horninessDcBias: 3).PeekAsync(10, playerShadows, null);
            var (horninessNegBias, _) = new HorninessEngine(rng, horninessDcBias: -3).PeekAsync(10, playerShadows, null);

            Assert.Equal(10, horninessNoBias.DC);
            Assert.Equal(7, horninessPosBias.DC);
            Assert.Equal(13, horninessNegBias.DC);
        }
    }
}
