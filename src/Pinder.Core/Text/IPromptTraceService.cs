using System.Collections.Generic;
using System;

namespace Pinder.Core.Text
{
    public sealed class PromptTraceRunRecord
    {
        public PromptTraceRunRecord(
            string runId,
            string? sessionId,
            string runKind,
            string promptType,
            PromptTraceResult trace,
            DateTime timestamp,
            string? provider,
            string? providerModel,
            int? turnNumber,
            int? branchOption)
        {
            RunId = runId;
            SessionId = sessionId;
            RunKind = runKind;
            PromptType = promptType;
            Trace = trace;
            Timestamp = timestamp;
            Provider = provider;
            ProviderModel = providerModel;
            TurnNumber = turnNumber;
            BranchOption = branchOption;
        }

        public string RunId { get; }
        public string? SessionId { get; }
        public string RunKind { get; }
        public string PromptType { get; }
        public PromptTraceResult Trace { get; }
        public DateTime Timestamp { get; }
        public string? Provider { get; }
        public string? ProviderModel { get; }
        public int? TurnNumber { get; }
        public int? BranchOption { get; }
        public string? CallId { get; internal set; }
        public string? ModelResponse { get; internal set; }
        public DateTime? ResponseTimestamp { get; internal set; }
    }

    /// <summary>
    /// Service interface to record and retrieve prompt trace data.
    /// </summary>
    public interface IPromptTraceService
    {
        /// <summary>
        /// Record a build trace for a prompt type (e.g., "dialogue-options", "delivery", "datee").
        /// </summary>
        void RecordTrace(string promptType, PromptTraceResult trace);

        /// <summary>Attaches the completed raw model response to the pending traces in the current call scope.</summary>
        void RecordModelResponse(string response, string? callId = null);

        /// <summary>
        /// Retrieves the last recorded trace for a given prompt type.
        /// </summary>
        PromptTraceResult? GetLastTrace(string promptType);

        /// <summary>
        /// Retrieves the last recorded trace for a given prompt type in a session.
        /// </summary>
        PromptTraceResult? GetLastTrace(string promptType, string? sessionId);

        /// <summary>
        /// Retrieves all recorded traces in the current session.
        /// </summary>
        IReadOnlyDictionary<string, PromptTraceResult> GetAllTraces();

        /// <summary>
        /// Retrieves the chronological sequence of all recorded traces.
        /// </summary>
        IReadOnlyList<PromptTraceRunRecord> GetSequence();

        /// <summary>
        /// Retrieves the chronological sequence of recorded traces for a session.
        /// </summary>
        IReadOnlyList<PromptTraceRunRecord> GetSequence(string? sessionId);

        /// <summary>
        /// Clears recorded traces for one session.
        /// </summary>
        void ClearSession(string sessionId);

        /// <summary>
        /// Clears all recorded traces.
        /// </summary>
        void Clear();
    }
}
