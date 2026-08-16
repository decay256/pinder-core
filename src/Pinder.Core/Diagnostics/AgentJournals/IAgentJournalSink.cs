using System;
using System.Threading;
using System.Threading.Tasks;

namespace Pinder.Core.Diagnostics.AgentJournals
{
    public enum AgentJournalSinkFailureMode
    {
        BestEffort,
        FailClosed,
    }

    public interface IAgentJournalSink
    {
        Task PersistAsync(AgentJournalSinkRecord record, CancellationToken cancellationToken);
    }

    public interface IAgentJournalProjectionSink
    {
        Task ProjectAsync(AgentJournalSinkRecord record, CancellationToken cancellationToken);
    }

    public sealed class AgentJournalSinkPersistenceException : Exception
    {
        public AgentJournalSinkPersistenceException(
            string recordId,
            string customType,
            Exception innerException)
            : base(
                "Agent journal host sink failed to persist record '" + recordId + "' (" + customType + ").",
                innerException)
        {
            if (string.IsNullOrWhiteSpace(recordId))
                throw new ArgumentException("Record id is required.", nameof(recordId));
            if (string.IsNullOrWhiteSpace(customType))
                throw new ArgumentException("Custom type is required.", nameof(customType));
            RecordId = recordId;
            CustomType = customType;
        }

        public string RecordId { get; }
        public string CustomType { get; }
    }

    public sealed class AgentJournalPiProjectionException : Exception
    {
        public AgentJournalPiProjectionException(
            string recordId,
            string customType,
            Exception innerException)
            : base(
                "Agent journal Pi projection failed for record '" + recordId + "' (" + customType + ").",
                innerException)
        {
            if (string.IsNullOrWhiteSpace(recordId))
                throw new ArgumentException("Record id is required.", nameof(recordId));
            if (string.IsNullOrWhiteSpace(customType))
                throw new ArgumentException("Custom type is required.", nameof(customType));
            RecordId = recordId;
            CustomType = customType;
        }

        public string RecordId { get; }
        public string CustomType { get; }
    }
}
