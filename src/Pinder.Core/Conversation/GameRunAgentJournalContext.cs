using System;
using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.Core.Conversation
{
    public enum GameRunConversationBranchKind
    {
        Main = 0,
        Prefetch = 1,
        Speculative = 2,
    }

    /// <summary>
    /// Immutable host correlation carried by one Game Run or one branch clone.
    /// Identifier shape is validated by the Agent Journal boundary before persistence.
    /// </summary>
    public sealed class GameRunAgentJournalContext
    {
        public GameRunAgentJournalContext(
            string gameRunId,
            string agentSessionId,
            string? requestId = null,
            string? branchId = null,
            GameRunConversationBranchKind branchKind = GameRunConversationBranchKind.Main,
            IAgentJournalSink? hostSink = null)
        {
            GameRunId = Required(gameRunId, nameof(gameRunId));
            AgentSessionId = Required(agentSessionId, nameof(agentSessionId));
            RequestId = requestId;
            BranchId = branchId;
            BranchKind = branchKind;
            HostSink = hostSink;
        }

        public string GameRunId { get; }
        public string AgentSessionId { get; }
        public string? RequestId { get; }
        public string? BranchId { get; }
        public GameRunConversationBranchKind BranchKind { get; }
        public IAgentJournalSink? HostSink { get; }

        public GameRunAgentJournalContext ForRequest(string requestId)
        {
            return new GameRunAgentJournalContext(
                GameRunId,
                AgentSessionId,
                Required(requestId, nameof(requestId)),
                BranchId,
                BranchKind,
                HostSink);
        }

        public GameRunAgentJournalContext ForBranch(
            GameRunConversationBranchKind branchKind,
            string branchId)
        {
            if (branchKind != GameRunConversationBranchKind.Prefetch
                && branchKind != GameRunConversationBranchKind.Speculative)
            {
                throw new ArgumentOutOfRangeException(nameof(branchKind));
            }

            return new GameRunAgentJournalContext(
                GameRunId,
                AgentSessionId,
                RequestId,
                Required(branchId, nameof(branchId)),
                branchKind,
                HostSink);
        }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty opaque identifier is required.", parameterName);
            return value;
        }
    }
}
