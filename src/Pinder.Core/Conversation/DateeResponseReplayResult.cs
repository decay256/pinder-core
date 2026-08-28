using System;

namespace Pinder.Core.Conversation
{
    /// <summary>Accepted result of a response-only replay.</summary>
    public sealed class DateeResponseReplayResult
    {
        public DateeResponseReplayResult(string dateeMessage, GameStateSnapshot stateAfter)
        {
            DateeMessage = string.IsNullOrWhiteSpace(dateeMessage)
                ? throw new ArgumentException("A non-empty DATEE message is required.", nameof(dateeMessage))
                : dateeMessage;
            StateAfter = stateAfter ?? throw new ArgumentNullException(nameof(stateAfter));
        }

        public string DateeMessage { get; }
        public GameStateSnapshot StateAfter { get; }
    }
}
