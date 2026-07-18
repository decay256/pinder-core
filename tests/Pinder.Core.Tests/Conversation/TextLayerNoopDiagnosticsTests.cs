using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Xunit;

namespace Pinder.Core.Tests.Conversation
{
    [Trait("Category", "Core")]
    public sealed class TextLayerNoopDiagnosticsTests
    {
        [Fact]
        public void Emit_UsesStableEightByteSha256Hashes()
        {
            var captured = new List<TextLayerNoopEvent>();

            TextLayerNoopDiagnostics.Emit(
                captured.Add,
                turnNumber: 12,
                layer: "Shadow (Dread)",
                beforeText: "before text",
                afterText: "after text");

            var evt = Assert.Single(captured);
            Assert.Equal(12, evt.TurnNumber);
            Assert.Equal("Shadow (Dread)", evt.Layer);
            Assert.Equal("aa046c87a2e4e552", evt.BeforeHash);
            Assert.Equal("a624242e9a7a82a6", evt.AfterHash);
        }

        [Fact]
        public void Emit_SwallowsDiagnosticCallbackFailures()
        {
            TextLayerNoopDiagnostics.Emit(
                _ => throw new InvalidOperationException("diagnostic sink failed"),
                turnNumber: 1,
                layer: "Trap",
                beforeText: "same",
                afterText: "same");
        }

        [Fact]
        public void Emit_DoesNothingWhenCallbackIsNotConfigured()
        {
            TextLayerNoopDiagnostics.Emit(
                null,
                turnNumber: 1,
                layer: "Trap",
                beforeText: "same",
                afterText: "same");
        }
    }
}
