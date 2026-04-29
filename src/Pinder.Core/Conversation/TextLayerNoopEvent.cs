namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Issue #314: structured event payload for the
    /// <c>GameSessionConfig.OnTextLayerNoop</c> callback.
    ///
    /// Fired when a text-transform layer (Horniness / Shadow / Trap overlay)
    /// ran an LLM call but the post-layer text equals the pre-layer text
    /// byte-for-byte. The diff would otherwise be silently dropped, making
    /// "layer ran but no-op" indistinguishable from "layer didn't run at all"
    /// in the audit/UI \u2014 this event lets the host log a breadcrumb so the
    /// triple-fail-turn ambiguity (PR #310 / issue #305) is resolved.
    /// </summary>
    public sealed class TextLayerNoopEvent
    {
        /// <summary>1-based turn number the no-op fired on.</summary>
        public int TurnNumber { get; }

        /// <summary>
        /// Layer label \u2014 one of <c>"Horniness"</c>, <c>"Shadow ({stat})"</c>,
        /// or <c>"Trap ({trapDisplayName})"</c>. Mirrors the
        /// <c>TextDiff.Layer</c> label so logs and diffs are easy to correlate.
        /// </summary>
        public string Layer { get; }

        /// <summary>
        /// Stable hash of the pre-layer text (same input the layer LLM call
        /// saw). Hash is stable across runs but does not need to be
        /// cryptographic \u2014 it's an audit ID, not a security primitive.
        /// </summary>
        public string BeforeHash { get; }

        /// <summary>
        /// Stable hash of the post-layer text. Equal to <see cref="BeforeHash"/>
        /// by construction (the no-op event only fires when the strings are
        /// byte-identical), but emitted explicitly for downstream tooling
        /// that expects both fields.
        /// </summary>
        public string AfterHash { get; }

        public TextLayerNoopEvent(int turnNumber, string layer, string beforeHash, string afterHash)
        {
            TurnNumber = turnNumber;
            Layer = layer ?? string.Empty;
            BeforeHash = beforeHash ?? string.Empty;
            AfterHash = afterHash ?? string.Empty;
        }
    }
}
