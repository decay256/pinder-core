using System.Collections.Generic;

namespace Pinder.Core.Text
{
    /// <summary>
    /// Service interface to record and retrieve prompt trace data.
    /// </summary>
    public interface IPromptTraceService
    {
        /// <summary>
        /// Record a build trace for a prompt type (e.g., "dialogue-options", "delivery", "opponent").
        /// </summary>
        void RecordTrace(string promptType, PromptTraceResult trace);

        /// <summary>
        /// Retrieves the last recorded trace for a given prompt type.
        /// </summary>
        PromptTraceResult? GetLastTrace(string promptType);

        /// <summary>
        /// Retrieves all recorded traces in the current session.
        /// </summary>
        IReadOnlyDictionary<string, PromptTraceResult> GetAllTraces();

        /// <summary>
        /// Retrieves the chronological sequence of all recorded traces.
        /// </summary>
        IReadOnlyList<(string PromptType, PromptTraceResult Trace, System.DateTime Timestamp)> GetSequence();

        /// <summary>
        /// Clears all recorded traces.
        /// </summary>
        void Clear();
    }
}
