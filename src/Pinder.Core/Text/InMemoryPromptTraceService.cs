using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

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

        private readonly ConcurrentDictionary<string, PromptTraceResult> _sessionTraces =
            new ConcurrentDictionary<string, PromptTraceResult>(StringComparer.OrdinalIgnoreCase);

        private readonly List<PromptTraceRunRecord> _sequence =
            new List<PromptTraceRunRecord>();
        private readonly object _lock = new object();
        private readonly AsyncLocal<PromptTraceScope?> _scope = new AsyncLocal<PromptTraceScope?>();
        private const int MaxRunsPerSession = 50;
        private static readonly TimeSpan MaxSessionTraceAge = TimeSpan.FromHours(6);

        private sealed class PromptTraceScope
        {
            public PromptTraceScope(
                string runId,
                string? sessionId,
                string runKind,
                string? provider,
                string? providerModel,
                int? turnNumber,
                int? branchOption)
            {
                RunId = runId;
                SessionId = sessionId;
                RunKind = runKind;
                Provider = provider;
                ProviderModel = providerModel;
                TurnNumber = turnNumber;
                BranchOption = branchOption;
            }

            public string RunId { get; }
            public string? SessionId { get; }
            public string RunKind { get; }
            public string? Provider { get; }
            public string? ProviderModel { get; }
            public int? TurnNumber { get; }
            public int? BranchOption { get; }
        }

        private sealed class ScopeHandle : IDisposable
        {
            private readonly InMemoryPromptTraceService _owner;
            private readonly PromptTraceScope? _previous;
            private bool _disposed;

            public ScopeHandle(InMemoryPromptTraceService owner, PromptTraceScope? previous)
            {
                _owner = owner;
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _owner._scope.Value = _previous;
            }
        }

        /// <summary>
        /// Records prompt traces created inside this async flow under one web session.
        /// </summary>
        public IDisposable BeginSessionScope(
            string? sessionId,
            string? providerModel = null,
            string runKind = "live_turn",
            int? turnNumber = null,
            int? branchOption = null)
        {
            var previous = _scope.Value;
            var provider = ProviderFromModelSpec(providerModel);
            _scope.Value = new PromptTraceScope(
                Guid.NewGuid().ToString("N"),
                string.IsNullOrWhiteSpace(sessionId) ? null : sessionId,
                string.IsNullOrWhiteSpace(runKind) ? "live_turn" : runKind,
                provider,
                string.IsNullOrWhiteSpace(providerModel) ? null : providerModel,
                turnNumber,
                branchOption);
            return new ScopeHandle(this, previous);
        }

        /// <inheritdoc />
        public void RecordTrace(string promptType, PromptTraceResult trace)
        {
            if (string.IsNullOrEmpty(promptType)) throw new ArgumentException("Prompt type cannot be null or empty.", nameof(promptType));
            var nonNullTrace = trace ?? throw new ArgumentNullException(nameof(trace));
            var scope = _scope.Value;
            string? sessionId = scope?.SessionId;
            _traces[promptType] = nonNullTrace;
            if (HasText(sessionId))
            {
                string scopedSessionId = sessionId!;
                _sessionTraces[SessionTraceKey(scopedSessionId, promptType)] = nonNullTrace;
            }
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                _sequence.Add(new PromptTraceRunRecord(
                    scope?.RunId ?? Guid.NewGuid().ToString("N"),
                    sessionId,
                    scope?.RunKind ?? "legacy",
                    promptType,
                    nonNullTrace,
                    now,
                    scope?.Provider,
                    scope?.ProviderModel,
                    scope?.TurnNumber,
                    scope?.BranchOption));
                if (HasText(sessionId))
                {
                    PruneSessionLocked(sessionId!, now);
                }
            }
        }

        /// <inheritdoc />
        public PromptTraceResult? GetLastTrace(string promptType)
        {
            if (string.IsNullOrEmpty(promptType)) return null;
            return _traces.TryGetValue(promptType, out var trace) ? trace : null;
        }

        /// <inheritdoc />
        public PromptTraceResult? GetLastTrace(string promptType, string? sessionId)
        {
            if (string.IsNullOrEmpty(promptType)) return null;
            if (!HasText(sessionId)) return GetLastTrace(promptType);
            return _sessionTraces.TryGetValue(SessionTraceKey(sessionId!, promptType), out var trace) ? trace : null;
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, PromptTraceResult> GetAllTraces()
        {
            return _traces;
        }

        /// <inheritdoc />
        public IReadOnlyList<PromptTraceRunRecord> GetSequence()
        {
            lock (_lock)
            {
                return _sequence.ToArray();
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<PromptTraceRunRecord> GetSequence(string? sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return GetSequence();
            lock (_lock)
            {
                return _sequence.Where(r => string.Equals(r.SessionId, sessionId, StringComparison.Ordinal)).ToArray();
            }
        }

        /// <inheritdoc />
        public void ClearSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return;
            foreach (var key in _sessionTraces.Keys)
            {
                if (key.StartsWith(sessionId + "\u001f", StringComparison.Ordinal))
                {
                    _sessionTraces.TryRemove(key, out _);
                }
            }
            lock (_lock)
            {
                _sequence.RemoveAll(r => string.Equals(r.SessionId, sessionId, StringComparison.Ordinal));
            }
        }

        /// <inheritdoc />
        public void Clear()
        {
            _traces.Clear();
            _sessionTraces.Clear();
            lock (_lock)
            {
                _sequence.Clear();
            }
        }

        private static string SessionTraceKey(string sessionId, string promptType) => sessionId + "\u001f" + promptType;

        private void PruneSessionLocked(string sessionId, DateTime now)
        {
            var oldestAllowed = now - MaxSessionTraceAge;
            _sequence.RemoveAll(r =>
                string.Equals(r.SessionId, sessionId, StringComparison.Ordinal)
                && r.Timestamp < oldestAllowed);

            var sessionRunIds = _sequence
                .Where(r => string.Equals(r.SessionId, sessionId, StringComparison.Ordinal))
                .Select(r => r.RunId)
                .Distinct()
                .ToArray();
            if (sessionRunIds.Length <= MaxRunsPerSession) return;

            var runIdsToRemove = new HashSet<string>(
                sessionRunIds.Take(sessionRunIds.Length - MaxRunsPerSession),
                StringComparer.Ordinal);
            _sequence.RemoveAll(r =>
                string.Equals(r.SessionId, sessionId, StringComparison.Ordinal)
                && runIdsToRemove.Contains(r.RunId));
        }

        private static string? ProviderFromModelSpec(string? modelSpec)
        {
            if (!HasText(modelSpec)) return null;
            string value = modelSpec!.Trim();
            int slash = value.IndexOf('/');
            if (slash <= 0) return null;
            return value.Substring(0, slash);
        }

        private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);
    }
}
