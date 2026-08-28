using System;
using System.Threading;
using System.Threading.Tasks;
using Pi.Agent.Core;
using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.LlmAdapters.AgentJournals
{
    public sealed class PiAgentJournalProjectionSink : IAgentJournalProjectionSink
    {
        private readonly ISession<SessionMetadata>? _session;
        private readonly PiAgentJournalEntryCodec _codec;

        public PiAgentJournalProjectionSink(ISession<SessionMetadata>? session)
            : this(session, new PiAgentJournalEntryCodec())
        {
        }

        public PiAgentJournalProjectionSink(
            ISession<SessionMetadata>? session,
            PiAgentJournalEntryCodec codec)
        {
            _session = session;
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        }

        public async Task ProjectAsync(AgentJournalSinkRecord record, CancellationToken cancellationToken)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (_session == null)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            CustomEntry entry = Encode(record);
            cancellationToken.ThrowIfCancellationRequested();
            await _session.AppendCustomEntryAsync(entry.CustomType, entry.Data).ConfigureAwait(false);
        }

        private CustomEntry Encode(AgentJournalSinkRecord record)
        {
            switch (record.CustomType)
            {
                case AgentJournalSchemaNames.LlmInvocationV1:
                    var invocation = record.Record as LlmInvocationRecord;
                    if (invocation == null)
                    {
                        throw InvalidRecordType(record, nameof(LlmInvocationRecord));
                    }
                    return _codec.Encode(invocation);

                case AgentJournalSchemaNames.LlmResultV1:
                    var result = record.Record as LlmResultRecord;
                    if (result == null)
                    {
                        throw InvalidRecordType(record, nameof(LlmResultRecord));
                    }
                    return _codec.Encode(result);

                case AgentJournalSchemaNames.MessageLinkV1:
                    var link = record.Record as MessageLinkRecord;
                    if (link == null)
                    {
                        throw InvalidRecordType(record, nameof(MessageLinkRecord));
                    }
                    return _codec.Encode(link);

                case AgentJournalSchemaNames.RoleFactPolicyDecisionV1:
                    var decision = record.Record as AgentJournalRoleFactPolicyDecisionRecord;
                    if (decision == null)
                        throw InvalidRecordType(record, nameof(AgentJournalRoleFactPolicyDecisionRecord));
                    return _codec.Encode(decision);

                case AgentJournalSchemaNames.DateeResponsePlanV1:
                    var responsePlan = record.Record as AgentJournalDateeResponsePlanRecord;
                    if (responsePlan == null)
                        throw InvalidRecordType(record, nameof(AgentJournalDateeResponsePlanRecord));
                    return _codec.Encode(responsePlan);

                default:
                    throw new InvalidOperationException("Unsupported Pinder agent journal custom type '" + record.CustomType + "'.");
            }
        }

        private static Exception InvalidRecordType(AgentJournalSinkRecord record, string expectedType)
            => new InvalidOperationException("Record '" + record.RecordId + "' must contain " + expectedType + ".");
    }
}
