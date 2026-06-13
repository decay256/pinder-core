using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.Text;
using Xunit;

namespace Pinder.Core.Tests.Conversation
{
    [Trait("Category", "Core")]
    public class LlmDispatcherTests
    {
        private class MockLlmAdapter : ILlmAdapter
        {
            public int TrapCalls { get; private set; }
            public int ShadowCalls { get; private set; }

            public string TrapResponse { get; set; } = "Trap Response";
            public string ShadowResponse { get; set; } = "Shadow Response";

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default) => throw new NotImplementedException();
            public Task<string> DeliverMessageAsync(DeliveryContext context, CancellationToken ct = default) => throw new NotImplementedException();
            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, CancellationToken ct = default) => throw new NotImplementedException();
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default) => throw new NotImplementedException();
            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default) => throw new NotImplementedException();

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default)
            {
                ShadowCalls++;
                return Task.FromResult(ShadowResponse == "ECHO" ? message : ShadowResponse);
            }

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
            {
                TrapCalls++;
                return Task.FromResult(TrapResponse == "ECHO" ? message : TrapResponse);
            }
        }

        [Fact]
        public async Task Dispatcher_ReturnsOriginalMessage_WhenNoOverlaysEnabled()
        {
            var llm = new MockLlmAdapter();
            var textDiffs = new List<TextDiff>();

            var result = await LlmDispatcher.DispatchSpeculativeCallsAsync(
                llm,
                "Original",
                runTrap: false, "", "", "",
                runShadow: false, "", ShadowStatType.Dread,
                "", textDiffs, null, 1, null, CancellationToken.None);

            Assert.Equal("Original", result.FinalMessage);
            Assert.False(result.ShadowOverlayApplied);
            Assert.Equal(0, llm.TrapCalls);
            Assert.Equal(0, llm.ShadowCalls);
        }

        [Fact]
        public async Task Dispatcher_CallsOnlyTrap_WhenOnlyTrapEnabled()
        {
            var llm = new MockLlmAdapter { TrapResponse = "Trap Output" };
            var textDiffs = new List<TextDiff>();

            var result = await LlmDispatcher.DispatchSpeculativeCallsAsync(
                llm,
                "Original",
                runTrap: true, "instruction", "trap-name", "datee-context",
                runShadow: false, "", ShadowStatType.Dread,
                "", textDiffs, null, 1, null, CancellationToken.None);

            Assert.Equal("Trap Output", result.FinalMessage);
            Assert.False(result.ShadowOverlayApplied);
            Assert.Equal(1, llm.TrapCalls);
            Assert.Equal(0, llm.ShadowCalls);
        }

        [Fact]
        public async Task Dispatcher_CallsOnlyShadow_WhenOnlyShadowEnabled()
        {
            var llm = new MockLlmAdapter { ShadowResponse = "Shadow Output" };
            var textDiffs = new List<TextDiff>();

            var result = await LlmDispatcher.DispatchSpeculativeCallsAsync(
                llm,
                "Original",
                runTrap: false, "", "", "",
                runShadow: true, "instruction", ShadowStatType.Dread,
                "", textDiffs, null, 1, null, CancellationToken.None);

            Assert.Equal("Shadow Output", result.FinalMessage);
            Assert.True(result.ShadowOverlayApplied);
            Assert.Equal(0, llm.TrapCalls);
            Assert.Equal(1, llm.ShadowCalls);
        }

        [Fact]
        public async Task Dispatcher_RunsSpeculativeShadow_AndUsesSpeculativeResult_IfTrapIsNoop()
        {
            var llm = new MockLlmAdapter
            {
                TrapResponse = "ECHO", // makes trap a noop
                ShadowResponse = "Shadow Output"
            };
            var textDiffs = new List<TextDiff>();

            var result = await LlmDispatcher.DispatchSpeculativeCallsAsync(
                llm,
                "Original",
                runTrap: true, "trap-instruction", "trap-name", "datee-context",
                runShadow: true, "shadow-instruction", ShadowStatType.Dread,
                "", textDiffs, null, 1, null, CancellationToken.None);

            Assert.Equal("Shadow Output", result.FinalMessage);
            Assert.True(result.ShadowOverlayApplied);
            Assert.Equal(1, llm.TrapCalls);
            Assert.Equal(1, llm.ShadowCalls);
        }

        [Fact]
        public async Task Dispatcher_ReRunsShadow_IfTrapChangedMessage()
        {
            var llm = new MockLlmAdapter
            {
                TrapResponse = "Trap Output",
                ShadowResponse = "Shadow Output"
            };
            var textDiffs = new List<TextDiff>();

            var result = await LlmDispatcher.DispatchSpeculativeCallsAsync(
                llm,
                "Original",
                runTrap: true, "trap-instruction", "trap-name", "datee-context",
                runShadow: true, "shadow-instruction", ShadowStatType.Dread,
                "", textDiffs, null, 1, null, CancellationToken.None);

            Assert.Equal("Shadow Output", result.FinalMessage);
            Assert.True(result.ShadowOverlayApplied);
            Assert.Equal(1, llm.TrapCalls);
            // Expected 2 shadow calls: 1 speculative, and 1 sequential fallback re-run because trap changed the message
            Assert.Equal(2, llm.ShadowCalls);
        }
    }
}
