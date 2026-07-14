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
        
        public Task<string> ApplyFailureCorruptionAsync(string message, string instruction, Pinder.Core.Stats.StatType stat, Pinder.Core.Rolls.FailureTier tier, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
        {
            return Task.FromResult(message);
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

        /// <summary>
        /// Regression guard for the #6 Task.Run-removal fix: DispatchSpeculativeCallsAsync
        /// used to wrap the Trap and Shadow overlay calls in separate
        /// <c>Task.Run</c>s so they'd race in parallel via Task.WhenAll. They
        /// are now invoked directly (no thread-pool hop), which only
        /// preserves the "both in flight together" behaviour if both calls
        /// are *started* before either is awaited. This adapter gates both
        /// calls on a shared latch that only opens once BOTH have been
        /// entered, proving the dispatcher still fires them concurrently
        /// rather than sequentially.
        /// </summary>
        private sealed class GatedLlmAdapter : ILlmAdapter
        {
            private readonly TaskCompletionSource<bool> _trapEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _shadowEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default) => throw new NotImplementedException();
            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, CancellationToken ct = default) => throw new NotImplementedException();
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default) => throw new NotImplementedException();
            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default) => throw new NotImplementedException();
            public Task<string> ApplyFailureCorruptionAsync(string message, string instruction, StatType stat, Pinder.Core.Rolls.FailureTier tier, string? archetypeDirective = null, CancellationToken ct = default) => Task.FromResult(message);

            public async Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
            {
                _trapEntered.TrySetResult(true);
                await _shadowEntered.Task; // deadlocks (and the test times out) if shadow was never started
                return "Trap Output";
            }

            public async Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default)
            {
                _shadowEntered.TrySetResult(true);
                await _trapEntered.Task; // deadlocks (and the test times out) if trap was never started
                return "Shadow Output";
            }
        }

        [Fact]
        public async Task Dispatcher_StartsTrapAndShadow_ConcurrentlyNotSequentially()
        {
            var llm = new GatedLlmAdapter();
            var textDiffs = new List<TextDiff>();

            var dispatchTask = LlmDispatcher.DispatchSpeculativeCallsAsync(
                llm,
                "Original",
                runTrap: true, "trap-instruction", "trap-name", "datee-context",
                runShadow: true, "shadow-instruction", ShadowStatType.Dread,
                "", textDiffs, null, 1, null, CancellationToken.None);

            var completed = await Task.WhenAny(dispatchTask, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.Same(dispatchTask, completed);
            var result = await dispatchTask;
            Assert.Equal("Shadow Output", result.FinalMessage);
            Assert.True(result.ShadowOverlayApplied);
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

        [Fact]
        public async Task Dispatcher_EmitsSafeDispatchDecisionTelemetry_WhenBothOverlaysRequested()
        {
            var llm = new MockLlmAdapter
            {
                TrapResponse = "ECHO",
                ShadowResponse = "Shadow Output"
            };
            var textDiffs = new List<TextDiff>();
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var tracker = new SpeculativeWasteTracker();

            await LlmDispatcher.DispatchSpeculativeCallsAsync(
                llm,
                "Original",
                runTrap: true, "trap-instruction", "trap-name", "datee-context",
                runShadow: true, "shadow-instruction", ShadowStatType.Dread,
                "archetype-directive", textDiffs, null, 7, null, CancellationToken.None,
                tracker,
                diagnostics.Add);

            Assert.Equal(2, diagnostics.Count);
            var evt = diagnostics[0];
            Assert.Equal("LlmDispatcher", evt.Source);
            Assert.Equal("SpeculativeOverlayDispatchDecision", evt.EventName);
            Assert.Equal(OperationalDiagnosticSeverity.Info, evt.Severity);
            Assert.Equal(OperationalDiagnosticOperationKind.SpeculativeBranch, evt.OperationKind);
            Assert.Equal(OperationalDiagnosticBranchStatus.Started, evt.BranchStatus);
            Assert.Contains("mode=parallel", evt.Message);
            Assert.Contains("turn=7", evt.Message);
            Assert.Contains("shadow=Dread", evt.Message);
            Assert.Contains("tracker_present=True", evt.Message);
            Assert.DoesNotContain("Original", evt.Message);
            Assert.DoesNotContain("trap-instruction", evt.Message);
            Assert.DoesNotContain("shadow-instruction", evt.Message);
            Assert.DoesNotContain("datee-context", evt.Message);
            Assert.DoesNotContain("Trap Output", evt.Message);
            Assert.DoesNotContain("Shadow Output", evt.Message);

            var adopted = diagnostics[1];
            Assert.Equal("SpeculativeOverlayAdopted", adopted.EventName);
            Assert.Equal(OperationalDiagnosticBranchStatus.Adopted, adopted.BranchStatus);
            Assert.Equal(evt.BranchId, adopted.BranchId);
        }

        [Fact]
        public async Task Dispatcher_EmitsSafeWastedRerunTelemetry_WhenSpeculativeShadowIsDiscarded()
        {
            var llm = new MockLlmAdapter
            {
                TrapResponse = "Trap Output",
                ShadowResponse = "Shadow Output"
            };
            var textDiffs = new List<TextDiff>();
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var tracker = new SpeculativeWasteTracker();

            await LlmDispatcher.DispatchSpeculativeCallsAsync(
                llm,
                "Original",
                runTrap: true, "trap-instruction", "trap-name", "datee-context",
                runShadow: true, "shadow-instruction", ShadowStatType.Dread,
                "archetype-directive", textDiffs, null, 8, null, CancellationToken.None,
                tracker,
                diagnostics.Add);

            Assert.Equal(2, diagnostics.Count);
            var evt = diagnostics[1];
            Assert.Equal("LlmDispatcher", evt.Source);
            Assert.Equal("SpeculativeOverlayWastedRerun", evt.EventName);
            Assert.Equal(OperationalDiagnosticSeverity.Info, evt.Severity);
            Assert.Contains("turn=8", evt.Message);
            Assert.Contains("shadow=Dread", evt.Message);
            Assert.Contains("trap_changed=true", evt.Message);
            Assert.Contains("rerun_shadow=true", evt.Message);
            Assert.Contains("counter_before=0", evt.Message);
            Assert.Contains("counter_after=-1", evt.Message);
            Assert.DoesNotContain("Original", evt.Message);
            Assert.DoesNotContain("trap-instruction", evt.Message);
            Assert.DoesNotContain("shadow-instruction", evt.Message);
            Assert.DoesNotContain("datee-context", evt.Message);
            Assert.DoesNotContain("Trap Output", evt.Message);
            Assert.DoesNotContain("Shadow Output", evt.Message);
        }

        [Fact]
        public async Task Dispatcher_SequentialMode_DoesNotLaunchSpeculativeShadowBeforeTrap()
        {
            var llm = new MockLlmAdapter
            {
                TrapResponse = "Trap Output",
                ShadowResponse = "Shadow Output"
            };
            var textDiffs = new List<TextDiff>();
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var tracker = new SpeculativeWasteTracker(wasteThreshold: 1);
            tracker.RecordWaste();

            var result = await LlmDispatcher.DispatchSpeculativeCallsAsync(
                llm,
                "Original",
                runTrap: true, "trap-instruction", "trap-name", "datee-context",
                runShadow: true, "shadow-instruction", ShadowStatType.Dread,
                "", textDiffs, null, 9, null, CancellationToken.None,
                tracker,
                diagnostics.Add);

            Assert.Equal("Shadow Output", result.FinalMessage);
            Assert.True(result.ShadowOverlayApplied);
            Assert.Equal(1, llm.TrapCalls);
            Assert.Equal(1, llm.ShadowCalls);
            var evt = Assert.Single(diagnostics);
            Assert.Equal("SpeculativeOverlayDispatchDecision", evt.EventName);
            Assert.Contains("mode=sequential", evt.Message);
            Assert.DoesNotContain("SpeculativeOverlayWastedRerun", evt.Message);
        }

        [Fact]
        public async Task Dispatcher_ConcurrentTurns_KeepSpeculativeBranchIdsIsolated()
        {
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var textDiffsA = new List<TextDiff>();
            var textDiffsB = new List<TextDiff>();

            await Task.WhenAll(
                LlmDispatcher.DispatchSpeculativeCallsAsync(
                    new MockLlmAdapter { TrapResponse = "ECHO", ShadowResponse = "Shadow A" },
                    "Original A",
                    runTrap: true, "trap-instruction-a", "trap-a", "datee-context-a",
                    runShadow: true, "shadow-instruction-a", ShadowStatType.Dread,
                    "", textDiffsA, null, 21, null, CancellationToken.None,
                    new SpeculativeWasteTracker(),
                    diagnostics.Add),
                LlmDispatcher.DispatchSpeculativeCallsAsync(
                    new MockLlmAdapter { TrapResponse = "ECHO", ShadowResponse = "Shadow B" },
                    "Original B",
                    runTrap: true, "trap-instruction-b", "trap-b", "datee-context-b",
                    runShadow: true, "shadow-instruction-b", ShadowStatType.Despair,
                    "", textDiffsB, null, 22, null, CancellationToken.None,
                    new SpeculativeWasteTracker(),
                    diagnostics.Add));

            Assert.Contains(diagnostics, d =>
                d.BranchId == "shadow:21:Dread"
                && d.BranchStatus == OperationalDiagnosticBranchStatus.Adopted);
            Assert.Contains(diagnostics, d =>
                d.BranchId == "shadow:22:Despair"
                && d.BranchStatus == OperationalDiagnosticBranchStatus.Adopted);
            Assert.DoesNotContain(diagnostics, d => d.Message.Contains("Original A"));
            Assert.DoesNotContain(diagnostics, d => d.Message.Contains("Original B"));
            Assert.DoesNotContain(diagnostics, d => d.Message.Contains("trap-instruction"));
            Assert.DoesNotContain(diagnostics, d => d.Message.Contains("shadow-instruction"));
        }
    }
}
