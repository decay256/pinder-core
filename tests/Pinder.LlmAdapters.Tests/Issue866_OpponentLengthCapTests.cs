using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Tests for Issue #866: opponent response length cap (relative window + 600-char ceiling).
    /// Four prompt-injection tests verify the length hint is injected correctly across
    /// the formula's boundary cases. One formula test verifies ComputeResponseCeiling at
    /// the boundary values and the regression scenario from session 707fca72.
    /// </summary>
    public class Issue866_OpponentLengthCapTests
    {
        // ── Helpers ──

        private static OpponentContext MakeOpponentContext(string playerDeliveredMessage)
        {
            return new OpponentContext(
                playerPrompt: "player system prompt",
                opponentPrompt: "opponent system prompt",
                conversationHistory: new List<(string, string)> { ("P", "hey"), ("O", "hi") },
                opponentLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 15,
                playerDeliveredMessage: playerDeliveredMessage,
                interestBefore: 14,
                interestAfter: 15,
                responseDelayMinutes: 2.0,
                playerName: "Velvet",
                opponentName: "Sable");
        }

        // ══════════════════════════════════════════════════════════════
        // AC1: playerLen=200 → ceiling = min(600, max(400, 80)) = 400
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void OpponentPrompt_200CharPlayer_HasCeiling400()
        {
            var msg = new string('x', 200);
            var ctx = MakeOpponentContext(msg);
            var prompt = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("400 characters", prompt);
        }

        // ══════════════════════════════════════════════════════════════
        // AC2: playerLen=1 → ceiling = min(600, max(2, 80)) = 80 (floor)
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void OpponentPrompt_1CharPlayer_HasFloor80()
        {
            var msg = "x"; // 1 char
            var ctx = MakeOpponentContext(msg);
            var prompt = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("80 characters", prompt);
        }

        // ══════════════════════════════════════════════════════════════
        // AC3: playerLen=500 → ceiling = min(600, max(1000, 80)) = 600 (cap)
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void OpponentPrompt_500CharPlayer_HasCap600()
        {
            var msg = new string('y', 500);
            var ctx = MakeOpponentContext(msg);
            var prompt = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("600 characters", prompt);
        }

        // ══════════════════════════════════════════════════════════════
        // AC4: Regression — 707fca72 scenario (1054-char player → cap 600)
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void OpponentPrompt_RegressionScenario_1054CharPlayer_HasCap600()
        {
            var msg = new string('z', 1054);
            var ctx = MakeOpponentContext(msg);
            var prompt = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("600 characters", prompt);
        }

        // ══════════════════════════════════════════════════════════════
        // AC5: ComputeResponseCeiling — formula boundaries + regression
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void ComputeResponseCeiling_BoundaryValues()
        {
            // Floor: 1-char player → 80
            Assert.Equal(80, SessionDocumentBuilder.ComputeResponseCeiling(1));
            Assert.Equal(80, SessionDocumentBuilder.ComputeResponseCeiling(39)); // 39×2=78 < 80
            Assert.Equal(80, SessionDocumentBuilder.ComputeResponseCeiling(40)); // 40×2=80 = floor

            // Window: playerLen × 2
            Assert.Equal(100, SessionDocumentBuilder.ComputeResponseCeiling(50));
            Assert.Equal(200, SessionDocumentBuilder.ComputeResponseCeiling(100));
            Assert.Equal(400, SessionDocumentBuilder.ComputeResponseCeiling(200));

            // Cap: 600
            Assert.Equal(600, SessionDocumentBuilder.ComputeResponseCeiling(300)); // 300×2=600
            Assert.Equal(600, SessionDocumentBuilder.ComputeResponseCeiling(500)); // 500×2=1000 → capped
            Assert.Equal(600, SessionDocumentBuilder.ComputeResponseCeiling(1054)); // 707fca72 scenario
            Assert.Equal(600, SessionDocumentBuilder.ComputeResponseCeiling(5000)); // extreme
        }
    }
}
