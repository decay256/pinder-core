using System;
using System.Collections.Generic;
using Pi.Agent.Core;
using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.LlmAdapters.AgentJournals
{
    public static class PiAgentJournalRegistry
    {
        private static readonly AgentMessage[] EmptyMessages = new AgentMessage[0];

        public static IReadOnlyDictionary<string, CustomEntryContextMessageProjector> CreateZeroContextProjectors()
            => new Dictionary<string, CustomEntryContextMessageProjector>(StringComparer.Ordinal)
            {
                [AgentJournalSchemaNames.LlmInvocationV1] = ProjectZeroContext,
                [AgentJournalSchemaNames.LlmResultV1] = ProjectZeroContext,
                [AgentJournalSchemaNames.MessageLinkV1] = ProjectZeroContext,
                [AgentJournalSchemaNames.RoleFactPolicyDecisionV1] = ProjectZeroContext,
                [AgentJournalSchemaNames.DateeResponsePlanV1] = ProjectZeroContext,
            };

        public static IReadOnlyList<AgentMessage> ProjectZeroContext(
            CustomEntry entry,
            int index,
            IReadOnlyList<SessionTreeEntry> entries)
            => EmptyMessages;
    }
}
