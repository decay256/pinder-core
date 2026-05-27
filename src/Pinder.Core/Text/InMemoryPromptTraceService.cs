using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Pinder.Core.Text
{
    /// <summary>
    /// Thread-safe in-memory implementation of the prompt trace registry.
    /// Exposes trace data globally or per-session.
    /// </summary>
    public sealed class InMemoryPromptTraceService : IPromptTraceService
    {
        private static readonly Lazy<InMemoryPromptTraceService> LazyInstance =
            new Lazy<InMemoryPromptTraceService>(() => new InMemoryPromptTraceService());

        /// <summary>
        /// Singleton instance for easy global reference if DI registration is not used/possible.
        /// </summary>
        public static InMemoryPromptTraceService Instance => LazyInstance.Value;

        private readonly ConcurrentDictionary<string, PromptTraceResult> _traces =
            new ConcurrentDictionary<string, PromptTraceResult>(StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc />
        public void RecordTrace(string promptType, PromptTraceResult trace)
        {
            if (string.IsNullOrEmpty(promptType)) throw new ArgumentException("Prompt type cannot be null or empty.", nameof(promptType));
            _traces[promptType] = trace ?? throw new ArgumentNullException(nameof(trace));
        }

        /// <inheritdoc />
        public PromptTraceResult? GetLastTrace(string promptType)
        {
            if (string.IsNullOrEmpty(promptType)) return null;
            return _traces.TryGetValue(promptType, out var trace) ? trace : null;
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, PromptTraceResult> GetAllTraces()
        {
            return _traces;
        }

        /// <inheritdoc />
        public void Clear()
        {
            _traces.Clear();
        }
    }
}
