namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Coarse progress stages emitted during <see cref="GameSession.ResolveTurnAsync"/>.
    /// Each value corresponds to a boundary before a distinct LLM call (or the
    /// completion of one). Consumers (e.g. an SSE endpoint) map these 1:1 to
    /// wire events.
    /// </summary>
    public enum TurnProgressStage
    {
        /// <summary>Dice rolled and internal math done; about to call the delivery LLM.</summary>
        DeliveryStarted,

        /// <summary>Delivery LLM returned; <see cref="TurnProgressEvent.Text"/> carries the delivered message.</summary>
        DeliveryCompleted,

        /// <summary>About to attempt the steering roll / steering LLM call.</summary>
        SteeringStarted,

        /// <summary>Steering resolved; <see cref="TurnProgressEvent.Text"/> carries the steering question if any, else null.</summary>
        SteeringCompleted,

        /// <summary>Horniness overlay LLM call is about to run.</summary>
        HorninessOverlayStarted,

        /// <summary>Horniness overlay LLM returned; Text carries the rewritten message.</summary>
        HorninessOverlayCompleted,

        /// <summary>Shadow corruption LLM call is about to run.</summary>
        ShadowCorruptionStarted,

        /// <summary>Shadow corruption LLM returned; Text carries the rewritten message.</summary>
        ShadowCorruptionCompleted,

        /// <summary>Trap overlay LLM call is about to run (issue #371; persistence turns only).</summary>
        TrapOverlayStarted,

        /// <summary>Trap overlay LLM returned; Text carries the rewritten message.</summary>
        TrapOverlayCompleted,

        /// <summary>About to call the opponent-response LLM.</summary>
        OpponentResponseStarted,

        /// <summary>Opponent response LLM returned; Text carries the opponent message.</summary>
        OpponentResponseCompleted,
    }

    /// <summary>
    /// One progress event emitted by <see cref="GameSession.ResolveTurnAsync"/>.
    /// Immutable.
    /// </summary>
    public sealed class TurnProgressEvent
    {
        public TurnProgressStage Stage { get; }

        /// <summary>
        /// Optional string payload (e.g. the delivered message for DeliveryCompleted,
        /// or the opponent reply for OpponentResponseCompleted). May be <c>null</c>
        /// for "started" stages.
        /// </summary>
        public string? Text { get; }

        public TurnProgressEvent(TurnProgressStage stage, string? text = null)
        {
            Stage = stage;
            Text = text;
        }
    }
}
