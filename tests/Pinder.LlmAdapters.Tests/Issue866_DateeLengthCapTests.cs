using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Regression tests for #1392, which supersedes #866's artificial response cap.
    /// The datee's designated texting-style length axis now controls natural length.
    /// </summary>
    public class Issue866_DateeLengthCapTests
    {
        // ── Helpers ──

        private static DateeContext MakeDateeContext(string playerDeliveredMessage)
        {
            return new DateeContext(
                dateePrompt: "datee system prompt",
                conversationHistory: new List<(string, string)> { ("P", "hey"), ("O", "hi") },
                dateeLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 15,
                playerDeliveredMessage: playerDeliveredMessage,
                interestBefore: 14,
                interestAfter: 15,
                responseDelayMinutes: 2.0,
                playerName: "Velvet",
                dateeName: "Sable");
        }

        // ══════════════════════════════════════════════════════════════
        // AC1: playerLen=200 → ceiling = min(600, max(400, 80)) = 400
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void DateePrompt_200CharPlayer_UsesDesignatedLengthAxis()
        {
            var msg = new string('x', 200);
            var ctx = MakeDateeContext(msg);
            var prompt = DateePromptTestBuilder.Build(ctx);

            Assert.Contains("guided by your designated texting-style length axis", prompt);
            Assert.DoesNotContain("characters regardless of your texting style", prompt);
        }

        // ══════════════════════════════════════════════════════════════
        // AC2: playerLen=1 → ceiling = min(600, max(2, 80)) = 80 (floor)
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void DateePrompt_1CharPlayer_HasNoArtificialFloor()
        {
            var msg = "x"; // 1 char
            var ctx = MakeDateeContext(msg);
            var prompt = DateePromptTestBuilder.Build(ctx);

            Assert.DoesNotContain("80 characters", prompt);
        }

        // ══════════════════════════════════════════════════════════════
        // AC3: playerLen=500 → ceiling = min(600, max(1000, 80)) = 600 (cap)
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void DateePrompt_500CharPlayer_HasNoArtificialCap()
        {
            var msg = new string('y', 500);
            var ctx = MakeDateeContext(msg);
            var prompt = DateePromptTestBuilder.Build(ctx);

            Assert.DoesNotContain("600 characters", prompt);
        }

        // ══════════════════════════════════════════════════════════════
        // AC4: Regression — 707fca72 scenario (1054-char player → cap 600)
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void DateePrompt_RegressionScenario_1054CharPlayer_HasNoOverrideLanguage()
        {
            var msg = new string('z', 1054);
            var ctx = MakeDateeContext(msg);
            var prompt = DateePromptTestBuilder.Build(ctx);

            Assert.DoesNotContain("engine-specified ceiling", prompt);
            Assert.DoesNotContain("NOT a hard engine cap", prompt);
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
